using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal static class SkillChecks
{
    public static async Task Run()
    {
        var registry = new SkillRegistry();
        registry.Register(new EchoSkill());
        var result = await registry.ExecuteAsync("test.echo", new SkillContext(), SkillContext.Schema("""{"message":"a new extension"}"""));
        Check(result.Ok && result.Text == "a new extension", "A new skill executes through the shared API without changing the engine");
        bool duplicate = false;
        try { registry.Register(new EchoSkill()); } catch (ArgumentException) { duplicate = true; }
        Check(duplicate, "Skill registration rejects ambiguous duplicate names");

        registry.Register(new WebSearchSkill(apiKey: ""));
        var disabled = await registry.ExecuteAsync("web.search", new SkillContext(), SkillContext.Schema("""{"query":"science"}"""));
        Check(!disabled.Ok && disabled.Error!.Contains("BRAVE_SEARCH_API_KEY"), "Unconfigured web search returns a clear structured result");
        using var client = new HttpClient(new SearchProvider());
        var configured = new SkillRegistry();
        configured.Register(new WebSearchSkill(client, apiKey: "test-key"));
        var search = await configured.ExecuteAsync("web.search", new SkillContext(), SkillContext.Schema("""{"query":"test topic","count":1}"""));
        var data = JsonSerializer.SerializeToElement(search.Data);
        Check(search.Ok && data.GetProperty("results").GetArrayLength() == 1 &&
            data.GetProperty("results")[0].GetProperty("url").GetString() == "https://example.org/source" &&
            data.GetProperty("results")[0].GetProperty("snippet").GetString() == "A useful source.",
            "The search adapter returns source URLs and clean snippets from an injected provider");
    }

    private sealed class SearchProvider : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request.Headers.Contains("X-Subscription-Token") && request.RequestUri!.Query.Contains("test%20topic"),
                "Search requests use encoded arguments and provider authentication headers");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("""{"web":{"results":[{"title":"Source","url":"https://example.org/source","description":"A <strong>useful</strong> source."}]}}""", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class EchoSkill : IAgentSkill
    {
        public string Name => "test.echo";
        public string Description => "Test-only extension.";
        public bool RequiresModel => false;
        public JsonElement Parameters => SkillContext.Schema("""{"type":"object","properties":{"message":{"type":"string"}},"required":["message"]}""");
        public Task<SkillResult> ExecuteAsync(SkillContext context, JsonElement arguments, CancellationToken cancellationToken)
            => Task.FromResult(new SkillResult(true, SkillContext.RequiredString(arguments, "message")));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
