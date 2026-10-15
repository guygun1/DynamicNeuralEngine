using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed class WebLearnSkill : IAgentSkill
{
    private static readonly HttpClient SharedReader = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly IAgentSkill _search;
    private readonly HttpClient _reader;
    public WebLearnSkill(IAgentSkill search, HttpClient? reader = null) { _search = search; _reader = reader ?? SharedReader; }
    public string Name => "web.learn";
    public string Description => "Cross-check claims on at least two different site domains, retain only corroborated statements, and cite their sources. Agreement is evidence, not proof of truth.";
    public bool RequiresModel => true;
    public bool Available => _search.Available;
    public JsonElement Parameters => SkillContext.Schema("""{"type":"object","properties":{"query":{"type":"string","maxLength":1000},"question":{"type":"string","maxLength":1000},"min_sources":{"type":"integer","minimum":2,"maximum":3},"max_sites":{"type":"integer","minimum":2,"maximum":6}},"required":["query"]}""");

    public async Task<SkillResult> ExecuteAsync(SkillContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        string query = SkillContext.RequiredString(arguments, "query");
        string question = arguments.TryGetProperty("question", out _) ? SkillContext.RequiredString(arguments, "question") : query;
        if (query.Length > 1000 || question.Length > 1000) throw new ArgumentException("Learning queries must not exceed 1000 characters.");
        // Search the inferred topic, not request boilerplate ("create me a simple
        // website" searches for "website" rather than matching "Simple Minds").
        string searchQuery = Topic(query);
        int Integer(string key, int fallback) => !arguments.TryGetProperty(key, out var value) ? fallback : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : throw new ArgumentException($"{key} must be an integer.");
        int minimum = Integer("min_sources", 2), maximum = Integer("max_sites", 4);
        if (minimum < 2 || minimum > 3 || maximum < minimum || maximum > 6) throw new ArgumentException("Use 2-3 minimum sources and between that minimum and 6 maximum sites.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(18));
        try
        {
            var search = await _search.ExecuteAsync(context, JsonSerializer.SerializeToElement(new { query = searchQuery, count = 10 }), budget.Token);
            if (!search.Ok) return search;
            var resultData = JsonSerializer.SerializeToElement(search.Data);
            if (!resultData.TryGetProperty("results", out var hits)) return new SkillResult(false, Error: "No source results were returned.");
            string Canonical(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9+#]+", " ").Trim();
            var ranked = hits.EnumerateArray().OrderByDescending(hit => hit.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String && Canonical(title.GetString()!) == Canonical(searchQuery));
            var candidates = new List<Uri>();
            var sites = new HashSet<string>(StringComparer.Ordinal);
            void Add(string? url) {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Eligible(uri) || !sites.Add(SourceCorroboration.Site(uri.AbsoluteUri))) return;
                candidates.Add(uri);
            }
            foreach (var hit in ranked)
            {
                if (hit.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String) Add(url.GetString());
            }
            if (candidates.Count < minimum) {
                var wikipedia = candidates.FirstOrDefault(uri => uri.Host == "en.wikipedia.org" && uri.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal));
                if (wikipedia is not null) foreach (string reference in await WikipediaReferences(wikipedia, budget.Token)) Add(reference);
            }
            using var readers = new SemaphoreSlim(3);
            async Task<SourceEvidence?> Read(Uri uri) {
                await readers.WaitAsync(budget.Token);
                using var pageBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                pageBudget.CancelAfter(TimeSpan.FromSeconds(5));
                try {
                    var page = await ReadSource(uri, pageBudget.Token);
                    if (page is null) return null;
                    string passage = SelectPassages(searchQuery, page.Text);
                    return passage.Length >= 80 ? page with { Text = passage } : null;
                }
                catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or RegexMatchTimeoutException) { return null; }
                catch (OperationCanceledException) when (!budget.IsCancellationRequested) { return null; }
                finally { readers.Release(); }
            }
            var pages = await Task.WhenAll(candidates.Take(maximum).Select(Read));
            var documents = pages.Where(page => page is not null).Select(page => page!).ToArray();
            var evidence = SourceCorroboration.Assess(searchQuery, documents, minimum);
            budget.Token.ThrowIfCancellationRequested();
            string retrieved = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (evidence.Claims.Count > 0) {
                var retained = new List<CorroboratedClaim>();
                int length = 0;
                foreach (var claim in evidence.Claims) {
                    if (length + claim.Text.Length + 2 > 3000) break;
                    retained.Add(claim); length += claim.Text.Length + 2;
                }
                string source = string.Join("\n", evidence.Sources.Select(url => url + " (retrieved " + retrieved + ")"));
                context.LearnFromSource(question, string.Join("\n", retained.Select(claim => claim.Text)), source);
                return new SkillResult(true, $"Corroborated statements were learned from {evidence.Sources.Count} different site domains.", new {
                    query, question, verification = "multi-site-corroborated", confidence = "confirmed", minimum_sources = minimum,
                    sites_read = evidence.SitesRead, sources = evidence.Sources, claims = retained,
                    disagreements = evidence.Disagreements, characters = length,
                    attribution = "Agreement between attributed sources is not independent proof of truth."
                });
            }
            if (documents.Length == 0)
                return new SkillResult(false, Data: new {
                    verification = "unconfirmed", minimum_sources = minimum, sites_read = evidence.SitesRead,
                    sources_read = Array.Empty<string>(), disagreements = evidence.Disagreements
                }, Error: "No readable source could be retrieved for this topic.");
            // Best effort: the sources were limited, unstructured, or in disagreement.
            // Keep the most supported statements, or the single most relevant passage,
            // clearly labelled as not cross-confirmed, so the answer reflects real
            // material instead of a bare refusal.
            if (evidence.TentativeClaims.Count > 0) {
                var retained = new List<CorroboratedClaim>();
                int length = 0;
                foreach (var claim in evidence.TentativeClaims) {
                    if (length + claim.Text.Length + 2 > 3000) break;
                    retained.Add(claim); length += claim.Text.Length + 2;
                }
                string source = string.Join("\n", evidence.Sources.Select(url => url + " (retrieved " + retrieved + "; not cross-confirmed)"));
                context.LearnFromSource(question, string.Join("\n", retained.Select(claim => claim.Text)), source);
                return new SkillResult(true, $"Best-effort notes were learned from {evidence.Sources.Count} source(s) that could not be fully cross-confirmed.", new {
                    query, question, verification = "tentative-single-source", confidence = "tentative", minimum_sources = minimum,
                    sites_read = evidence.SitesRead, sources = evidence.Sources, claims = retained,
                    disagreements = evidence.Disagreements, characters = length,
                    attribution = "Tentative best-effort from limited or disagreeing sources; treat as unverified."
                });
            }
            var bestDocument = documents.OrderByDescending(document => Relevance(searchQuery, document.Text)).First();
            if (Relevance(searchQuery, bestDocument.Text) == 0)
                return new SkillResult(false, Data: new {
                    verification = "unconfirmed", minimum_sources = minimum, sites_read = evidence.SitesRead,
                    sources_read = documents.Select(page => page.Url), disagreements = evidence.Disagreements
                }, Error: "The readable sources were not relevant to this topic. Nothing was learned.");
            string passage = bestDocument.Text.Length > 3000 ? bestDocument.Text[..3000] : bestDocument.Text;
            context.LearnFromSource(question, passage, bestDocument.Url + " (retrieved " + retrieved + "; not cross-confirmed)");
            return new SkillResult(true, "A best-effort passage was learned from one source because the sources could not be cross-confirmed.", new {
                query, question, verification = "tentative-unstructured", confidence = "tentative", minimum_sources = minimum,
                sites_read = evidence.SitesRead, sources = new[] { bestDocument.Url },
                claims = Array.Empty<CorroboratedClaim>(), disagreements = evidence.Disagreements, characters = passage.Length,
                attribution = "Unstructured best-effort passage from one source; not cross-confirmed."
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new SkillResult(false, Error: "Multi-site checking exceeded its time budget. No unconfirmed passage was learned.");
        }
    }

    private static bool Eligible(Uri uri) => uri.Scheme == "https" && !uri.IsLoopback && uri.AbsoluteUri.Length <= 500;

    private static readonly HashSet<string> RequestWords = new(StringComparer.Ordinal) {
        "create", "make", "made", "build", "write", "give", "show", "tell", "please", "me", "a", "an", "the", "for",
        "of", "to", "simple", "can", "could", "would", "should", "you", "your", "i", "we", "want", "wants", "need",
        "some", "any", "about", "explain", "describe", "how", "what", "which", "who", "when", "where", "why", "is",
        "are", "was", "were", "do", "does", "did", "code", "it", "this", "that", "with", "and", "or"
    };

    // Reduces a user request to its meaningful topic words so the search targets
    // the subject rather than request boilerplate.
    private static string Topic(string query)
    {
        var words = Regex.Matches(query.ToLowerInvariant(), @"[a-z][a-z0-9+#-]{1,}")
            .Select(match => match.Value).Where(word => !RequestWords.Contains(word)).Distinct().ToArray();
        return words.Length == 0 ? query.Trim() : string.Join(' ', words);
    }

    private static int Relevance(string query, string text)
    {
        var terms = Regex.Matches(query.ToLowerInvariant(), @"[a-z][a-z0-9+#-]{2,}").Select(match => match.Value).Distinct().ToArray();
        if (terms.Length == 0) return 0;
        string lower = text.ToLowerInvariant();
        return terms.Count(term => lower.Contains(term, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<string>> WikipediaReferences(Uri article, CancellationToken cancellationToken)
    {
        try {
            string title = Uri.UnescapeDataString(article.AbsolutePath[6..]).Replace('_', ' ');
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://en.wikipedia.org/w/api.php?action=query&prop=extlinks&ellimit=30&format=json&formatversion=2&titles=" + Uri.EscapeDataString(title));
            request.Headers.UserAgent.ParseAdd("DynamicNeuralEngine/0.1 (local learning experiment)");
            using var response = await _reader.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<string>();
            using var json = JsonDocument.Parse(await ReadBody(response, cancellationToken));
            var links = new List<string>();
            if (json.RootElement.TryGetProperty("query", out var query) && query.TryGetProperty("pages", out var pages))
                foreach (var page in pages.EnumerateArray()) if (page.TryGetProperty("extlinks", out var external))
                    foreach (var item in external.EnumerateArray()) {
                        if ((item.TryGetProperty("url", out var url) || item.TryGetProperty("*", out url)) && url.ValueKind == JsonValueKind.String) links.Add(url.GetString()!);
                    }
            return links;
        } catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException) { return Array.Empty<string>(); }
    }

    private async Task<SourceEvidence?> ReadSource(Uri uri, CancellationToken cancellationToken)
    {
        bool wikipedia = uri.Host.Equals("en.wikipedia.org", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal);
        string address = wikipedia ? "https://en.wikipedia.org/w/api.php?action=query&prop=extracts&explaintext=1&exintro=1&redirects=1&format=json&formatversion=2&titles=" +
            Uri.EscapeDataString(Uri.UnescapeDataString(uri.AbsolutePath[6..]).Replace('_', ' ')) : uri.AbsoluteUri;
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("DynamicNeuralEngine/0.1 (local learning experiment)");
        using var response = await _reader.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var final = wikipedia ? uri : response.RequestMessage?.RequestUri ?? uri;
        if (!Eligible(final)) return null;
        string? media = response.Content.Headers.ContentType?.MediaType;
        if (!wikipedia && media is not null && media != "text/html" && media != "text/plain" && media != "application/xhtml+xml") return null;
        string body = await ReadBody(response, cancellationToken);
        if (wikipedia) {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
            foreach (var page in pages.EnumerateArray()) if (page.TryGetProperty("extract", out var extract) && extract.ValueKind == JsonValueKind.String) return new SourceEvidence(final.AbsoluteUri, extract.GetString()!);
            return null;
        }
        string cleaned = Regex.Replace(body, @"<(script|style|nav|header|footer)\b[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        cleaned = Regex.Replace(cleaned, @"</(p|div|section|article|h[1-6])\s*>", "\n\n", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        cleaned = Regex.Replace(cleaned, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200));
        return new SourceEvidence(final.AbsoluteUri, WebUtility.HtmlDecode(cleaned));
    }

    private static async Task<string> ReadBody(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new MemoryStream();
        var buffer = new byte[4096];
        while (limited.Length < 256 * 1024) {
            int count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, 256 * 1024 - limited.Length)), cancellationToken);
            if (count == 0) break;
            limited.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(limited.ToArray());
    }

    private static string SelectPassages(string query, string source)
    {
        var ignored = new HashSet<string>(new[] { "what", "which", "where", "when", "does", "explain", "please", "tell", "about", "with", "from", "that", "this", "have", "were", "will", "know", "is", "are", "was", "the", "can", "and", "how", "for", "not" }, StringComparer.OrdinalIgnoreCase);
        var terms = Regex.Matches(query.ToLowerInvariant(), @"[a-z][a-z0-9+#-]{1,}").Select(match => match.Value).Where(term => !ignored.Contains(term)).Distinct().ToArray();
        var paragraphs = source.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select((text, index) => new { text = Regex.Replace(text, @"\s+", " ").Trim(), index })
            .Where(part => part.text.Length >= 80)
            .Select(part => new { part.text, part.index, score = terms.Count(term => part.text.Contains(term, StringComparison.OrdinalIgnoreCase)) })
            .Where(part => part.score > 0).OrderByDescending(part => part.score).ThenBy(part => part.index).Take(3).OrderBy(part => part.index);
        var selected = new StringBuilder();
        foreach (var paragraph in paragraphs) {
            int remaining = 3000 - selected.Length - (selected.Length > 0 ? 2 : 0);
            if (remaining < 80) break;
            string text = paragraph.text;
            if (text.Length > remaining) {
                int stop = text.LastIndexOf('.', remaining - 1, remaining);
                if (stop < 80) continue;
                text = text[..(stop + 1)];
            }
            if (selected.Length > 0) selected.Append("\n\n");
            selected.Append(text);
        }
        return selected.ToString();
    }
}
