using System.Text.Json.Nodes;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.Ai;

/// <summary>TypeSafe AI's Jev, asked a single Choice question per call.</summary>
/// <remarks>API reference: https://docs.typesafe.ai/api.md</remarks>
public sealed class JevDecisionModel(HttpClient http, string apiKey, string model = JevDecisionModel.DefaultModel) : IDecisionModel
{
    public const string DefaultModel = "jev-latest";

    private const string QuestionId = "answer";
    private const int MaxOptions = 255;
    private static readonly Uri Endpoint = new("https://api.typesafe.ai/v1/systemone");

    public async Task<Decision> ChooseAsync(
        string question,
        string context,
        IReadOnlyList<string> options,
        CancellationToken cancellationToken)
    {
        if (options.Count > MaxOptions)
        {
            // Jev cannot choose among this many; a zero-confidence answer hands the question on.
            return new Decision(options[0], 0);
        }

        var criteria = new JsonObject();
        foreach (var option in options)
        {
            criteria[option] = null;
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["state"] = context,
            ["questions"] = new JsonObject
            {
                [QuestionId] = new JsonObject
                {
                    ["type"] = "choice",
                    ["instructions"] = question,
                    ["criteria"] = criteria,
                },
            },
        };

        var response = await AiHttp.PostJsonAsync(http, Endpoint, apiKey, body, "Jev", cancellationToken);
        var answer = response["answers"]?[QuestionId]
            ?? throw new AiServiceException("Jev svarade utan något svar på frågan.");
        return new Decision(
            answer["choice"]?.GetValue<string>() ?? "",
            answer["confidence"]?.GetValue<double>() ?? 0);
    }
}
