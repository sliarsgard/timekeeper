using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Timekeeper.Ai;

/// <summary>A model API refused or failed a request. The message is shown to the user.</summary>
public sealed class AiServiceException(string message) : Exception(message);

internal static class AiHttp
{
    private const int MaxAttempts = 4;

    // 529 is Jev's "overloaded"; both it and rate limits clear up on their own.
    private static readonly HashSet<HttpStatusCode> Transient =
        [HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway, (HttpStatusCode)529];

    public static async Task<JsonNode> PostJsonAsync(
        HttpClient http,
        Uri endpoint,
        string apiKey,
        JsonObject body,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return JsonNode.Parse(text) ?? throw new AiServiceException($"{serviceName} gav ett tomt svar.");
            }

            if (!Transient.Contains(response.StatusCode) || attempt == MaxAttempts)
            {
                throw new AiServiceException($"{serviceName} svarade {(int)response.StatusCode}: {ErrorMessage(text)}");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var message = node?["error"]?["message"] ?? node?["error"] ?? node?["detail"] ?? node?["message"];
            if (message is not null)
            {
                return message is JsonValue value ? value.ToString() : message.ToJsonString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return body.Length > 300 ? body[..300] : body;
    }
}
