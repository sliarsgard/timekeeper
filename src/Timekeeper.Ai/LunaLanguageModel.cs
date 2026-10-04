using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.Ai;

/// <summary>OpenAI's GPT-6 Luna through the Responses API.</summary>
/// <remarks>Model reference: https://developers.openai.com/api/docs/models/gpt-6-luna</remarks>
public sealed class LunaLanguageModel(HttpClient http, string apiKey, string model = LunaLanguageModel.DefaultModel) : ILanguageModel
{
    public const string DefaultModel = "gpt-6-luna";

    private const string ClassifyInstructions =
        "You classify computer work at a Swedish accounting firm from window titles, addresses, "
        + "screen text and screenshots. Pick the option that fits best and give an honest confidence "
        + "between 0 and 1: low when the evidence does not clearly point to one option.";

    private const string CommentInstructions =
        "Du skriver kommentarer till tidrapporter på en svensk redovisningsbyrå. Skriv en kort kommentar "
        + "på svenska, högst tio ord, om vad som gjorts, i stil med \"Bokföring leverantörsfakturor, "
        + "avstämning skattekonto\". Nämn inte kundens namn, datum eller program. Svara bara med kommentaren.";

    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");

    public async Task<Decision> ChooseAsync(
        string question,
        string context,
        IReadOnlyList<string> options,
        string? imagePath,
        CancellationToken cancellationToken)
    {
        var content = new JsonArray
        {
            new JsonObject { ["type"] = "input_text", ["text"] = $"{question}\n\n{context}" },
        };
        if (imagePath is not null && File.Exists(imagePath))
        {
            var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            content.Add(new JsonObject
            {
                ["type"] = "input_image",
                ["image_url"] = $"data:image/jpeg;base64,{Convert.ToBase64String(image)}",
                ["detail"] = "auto",
            });
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["choice"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(options.Distinct().Select(o => (JsonNode)JsonValue.Create(o)!).ToArray()),
                },
                ["confidence"] = new JsonObject { ["type"] = "number" },
            },
            ["required"] = new JsonArray("choice", "confidence"),
            ["additionalProperties"] = false,
        };

        var body = Request(ClassifyInstructions, content);
        body["text"] = new JsonObject
        {
            ["format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["name"] = "decision",
                ["strict"] = true,
                ["schema"] = schema,
            },
        };

        var output = await SendAsync(body, cancellationToken);
        try
        {
            var decision = JsonNode.Parse(output)!;
            return new Decision(
                decision["choice"]!.GetValue<string>(),
                Math.Clamp(decision["confidence"]!.GetValue<double>(), 0, 1));
        }
        catch (Exception ex) when (ex is JsonException or NullReferenceException or InvalidOperationException)
        {
            throw new AiServiceException($"Luna gav ett svar som inte gick att tolka: {output}");
        }
    }

    public async Task<string> WriteCommentAsync(string context, CancellationToken cancellationToken)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "input_text", ["text"] = context } };
        return await SendAsync(Request(CommentInstructions, content), cancellationToken);
    }

    private JsonObject Request(string instructions, JsonArray content) => new()
    {
        ["model"] = model,
        ["instructions"] = instructions,
        ["reasoning"] = new JsonObject { ["effort"] = "low" },
        ["input"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = content } },
    };

    /// <summary>Returns the text of the message items; reasoning items in the output are skipped.</summary>
    private async Task<string> SendAsync(JsonObject body, CancellationToken cancellationToken)
    {
        var response = await AiHttp.PostJsonAsync(http, Endpoint, apiKey, body, "Luna", cancellationToken);
        var text = new StringBuilder();
        foreach (var item in response["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "message")
            {
                continue;
            }

            foreach (var part in item["content"]?.AsArray() ?? [])
            {
                if (part?["type"]?.GetValue<string>() == "output_text")
                {
                    text.Append(part["text"]?.GetValue<string>());
                }
            }
        }

        return text.Length > 0 ? text.ToString() : throw new AiServiceException("Luna svarade utan text.");
    }
}
