using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed record SearchHit(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("snippet")] string Snippet);

public sealed class WebSearchSkill : IAgentSkill
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;
    private readonly string? _apiKey;
    private readonly bool _wikipediaFallback;
    public WebSearchSkill(HttpClient? client = null, string? apiKey = null, bool useWikipediaFallback = false)
    { _client = client ?? SharedClient; _apiKey = apiKey ?? Environment.GetEnvironmentVariable("BRAVE_SEARCH_API_KEY"); _wikipediaFallback = useWikipediaFallback; }
    public string Name => "web.search";
    public string Description => "Search Brave when configured, or Wikipedia references when fallback is enabled. Returns provider-labelled source URLs and snippets.";
    public bool RequiresModel => false;
    public bool Available => !string.IsNullOrWhiteSpace(_apiKey) || _wikipediaFallback;
    public JsonElement Parameters => SkillContext.Schema("""{"type":"object","properties":{"query":{"type":"string","maxLength":4000},"count":{"type":"integer","minimum":1,"maximum":10}},"required":["query"]}""");

    public async Task<SkillResult> ExecuteAsync(SkillContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        string query = SkillContext.RequiredString(arguments, "query");
        if (query.Length > 4000) throw new ArgumentException("Search query must not exceed 4000 characters.");
        int count = 5;
        if (arguments.TryGetProperty("count", out var requested) && (!requested.TryGetInt32(out count) || count < 1 || count > 10))
            throw new ArgumentException("Search count must be between 1 and 10.");
        if (!Available) return new SkillResult(false, Error: "Set BRAVE_SEARCH_API_KEY before starting the runner to enable web.search.");
        bool wikipedia = string.IsNullOrWhiteSpace(_apiKey);
        string uri = wikipedia ? $"https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srsearch={Uri.EscapeDataString(query)}&srlimit={count}" :
            $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={count}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!wikipedia) request.Headers.Add("X-Subscription-Token", _apiKey);
        request.Headers.UserAgent.ParseAdd("DynamicNeuralEngine/0.1 (local learning experiment)");
        request.Headers.Add("Accept", "application/json");
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return new SkillResult(false, Error: $"Search provider returned HTTP {(int)response.StatusCode}.");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var results = new List<SearchHit>();
            if (wikipedia && document.RootElement.TryGetProperty("query", out var wiki) && wiki.TryGetProperty("search", out var pages))
            {
                foreach (var page in pages.EnumerateArray()) {
                    string title = page.GetProperty("title").GetString()!;
                    string snippet = page.GetProperty("snippet").GetString() ?? "";
                    results.Add(new SearchHit(title, "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')),
                        WebUtility.HtmlDecode(Regex.Replace(snippet, "<[^>]+>", ""))));
                }
            }
            else if (document.RootElement.TryGetProperty("web", out var web) && web.TryGetProperty("results", out var hits))
            {
                foreach (var hit in hits.EnumerateArray())
                {
                    if (results.Count == count) break;
                    string Value(string name) => hit.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString()! : "";
                    results.Add(new SearchHit(Value("title"), Value("url"), WebUtility.HtmlDecode(Regex.Replace(Value("description"), "<[^>]+>", ""))));
                }
            }
            return new SkillResult(true, Data: new { query, provider = wikipedia ? "wikipedia-reference-search" : "brave-web-search", results });
        }
        catch (HttpRequestException) { return new SkillResult(false, Error: "The search provider could not be reached."); }
        catch (JsonException) { return new SkillResult(false, Error: "The search provider returned an invalid response."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new SkillResult(false, Error: "The search request timed out."); }
    }
}
