using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Timekeeper.Ai;

namespace Timekeeper.Tests;

public class AiClientTests
{
    private static readonly string[] Options = ["Bageriet i Lund AB", "Internt"];

    [Fact]
    public async Task Jev_sends_a_choice_question_and_reads_the_answer()
    {
        var handler = new FakeHandler(
            """{"model":"jev-1.13.0","answers":{"answer":{"type":"choice","choice":"Bageriet i Lund AB","confidence":0.82,"probabilities":{}}}}""");
        var jev = new JevDecisionModel(new HttpClient(handler), "key");

        var decision = await jev.ChooseAsync("Which client?", "Fönstertitel: Bageriet.xlsx", Options, CancellationToken.None);

        Assert.Equal("Bageriet i Lund AB", decision.Choice);
        Assert.Equal(0.82, decision.Confidence);
        var request = handler.Requests.Single();
        Assert.Equal("https://api.typesafe.ai/v1/systemone", request.Uri);
        Assert.Equal("Bearer key", request.Authorization);
        var question = request.Body["questions"]!["answer"]!;
        Assert.Equal("choice", question["type"]!.GetValue<string>());
        Assert.Equal(Options, question["criteria"]!.AsObject().Select(p => p.Key));
        Assert.Equal("Fönstertitel: Bageriet.xlsx", request.Body["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task Jev_retries_when_overloaded()
    {
        var handler = new FakeHandler(
            ((HttpStatusCode)529, """{"error":{"message":"overloaded"}}"""),
            (HttpStatusCode.OK, """{"answers":{"answer":{"choice":"Internt","confidence":0.9}}}"""));
        var jev = new JevDecisionModel(new HttpClient(handler), "key");

        var decision = await jev.ChooseAsync("q", "c", Options, CancellationToken.None);

        Assert.Equal("Internt", decision.Choice);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Errors_are_reported_with_the_service_message()
    {
        var handler = new FakeHandler((HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API key"}}"""));
        var luna = new LunaLanguageModel(new HttpClient(handler), "bad");

        var error = await Assert.ThrowsAsync<AiServiceException>(() => luna.WriteCommentAsync("c", CancellationToken.None));

        Assert.Equal("Luna svarade 401: Invalid API key", error.Message);
    }

    [Fact]
    public async Task Luna_asks_for_structured_output_with_the_screenshot_and_reads_the_message_text()
    {
        var image = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        await File.WriteAllBytesAsync(image, [1, 2, 3]);
        var handler = new FakeHandler(
            """
            {"output":[
              {"type":"reasoning","summary":[]},
              {"type":"message","content":[{"type":"output_text","text":"{\"choice\":\"Bageriet i Lund AB\",\"confidence\":0.7}"}]}
            ]}
            """);
        var luna = new LunaLanguageModel(new HttpClient(handler), "key");

        var decision = await luna.ChooseAsync("Which client?", "context", Options, image, CancellationToken.None);
        File.Delete(image);

        Assert.Equal(new("Bageriet i Lund AB", 0.7), decision);
        var body = handler.Requests.Single().Body;
        Assert.Equal("gpt-6-luna", body["model"]!.GetValue<string>());
        Assert.Equal("json_schema", body["text"]!["format"]!["type"]!.GetValue<string>());
        Assert.Equal(Options, body["text"]!["format"]!["schema"]!["properties"]!["choice"]!["enum"]!.AsArray().Select(o => o!.GetValue<string>()));
        var imagePart = body["input"]![0]!["content"]![1]!;
        Assert.Equal("data:image/jpeg;base64,AQID", imagePart["image_url"]!.GetValue<string>());
    }

    private sealed class FakeHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;

        public FakeHandler(string body)
            : this((HttpStatusCode.OK, body))
        {
        }

        public List<(string Uri, string? Authorization, JsonNode Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            var (status, text) = responses[Math.Min(_next++, responses.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }
}
