using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

public sealed record SourceEvidence(string Url, string Text);
public sealed record CorroboratedClaim(string Text, IReadOnlyList<string> Sources);
public sealed record CorroborationResult(IReadOnlyList<CorroboratedClaim> Claims, IReadOnlyList<string> Sources,
    IReadOnlyList<string> Disagreements, int SitesRead, IReadOnlyList<CorroboratedClaim> TentativeClaims);

// Conservative claim matching, not a general natural-language truth verifier.
// Only individually supported claims are retained, never an unchecked paragraph.
public static class SourceCorroboration
{
    private sealed record Claim(string Text, string Key, string Subject, string Object, bool Negative, string NumberKey, string Numbers);
    private sealed class Evidence
    {
        public required Claim Claim { get; init; }
        public Dictionary<string, string> Sites { get; } = new(StringComparer.Ordinal);
        public bool Disputed { get; set; }
    }
    private static readonly HashSet<string> CompoundSuffixes = new(StringComparer.Ordinal) {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.nz", "co.jp", "co.in", "com.br", "com.cn", "com.sg", "co.za"
    };
    private static readonly HashSet<string> QueryWords = new(StringComparer.Ordinal) {
        "what", "which", "where", "when", "does", "explain", "please", "tell", "about", "with", "from",
        "that", "this", "have", "were", "will", "know", "is", "are", "was", "the", "can", "and", "how", "for", "not", "a", "an"
    };

    public static string Site(string url)
    {
        var uri = new Uri(url);
        string host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(host, out _)) return host;
        var labels = host.Split('.');
        if (labels.Length < 3) return host;
        string suffix = string.Join('.', labels.TakeLast(2));
        return string.Join('.', labels.TakeLast(CompoundSuffixes.Contains(suffix) ? 3 : 2));
    }

    public static CorroborationResult Assess(string query, IEnumerable<SourceEvidence> documents, int minimumSites = 2)
    {
        if (minimumSites < 2 || minimumSites > 3) throw new ArgumentOutOfRangeException(nameof(minimumSites));
        var sources = new Dictionary<string, SourceEvidence>(StringComparer.Ordinal);
        var pageCopies = new HashSet<string>(StringComparer.Ordinal);
        var claims = new Dictionary<string, Evidence>(StringComparer.Ordinal);
        foreach (var document in documents) {
            string site = Site(document.Url);
            if (sources.ContainsKey(site) || !pageCopies.Add(Normalize(document.Text))) continue;
            sources.Add(site, document);
            foreach (string sentence in Regex.Split(document.Text, @"(?<=[.!?])\s+|\r?\n+")) {
                var claim = Parse(sentence);
                if (claim is null) continue;
                string key = claim.Key + (claim.Negative ? "|negative" : "|positive");
                if (!claims.TryGetValue(key, out var evidence)) claims[key] = evidence = new Evidence { Claim = claim };
                evidence.Sites.TryAdd(site, document.Url);
            }
        }
        var values = claims.Values.ToArray();
        var disagreements = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < values.Length; i++) for (int j = i + 1; j < values.Length; j++) {
            var a = values[i]; var b = values[j];
            bool negated = a.Claim.Key == b.Claim.Key && a.Claim.Negative != b.Claim.Negative;
            bool numberConflict = a.Claim.NumberKey == b.Claim.NumberKey && a.Claim.Numbers.Length > 0 && b.Claim.Numbers.Length > 0 && a.Claim.Numbers != b.Claim.Numbers;
            if (!negated && !numberConflict) continue;
            if (!a.Sites.Keys.Any(left => b.Sites.Keys.Any(right => left != right))) continue;
            a.Disputed = b.Disputed = true;
            disagreements.Add(a.Claim.Text + " <> " + b.Claim.Text);
        }
        var shared = values.Where(evidence => !evidence.Disputed && evidence.Sites.Count >= minimumSites).ToArray();
        var topic = Normalize(string.Join(' ', Normalize(query).Split(' ').Where(word => !QueryWords.Contains(word))));
        Evidence[] best = Array.Empty<Evidence>();
        string[] bestSites = Array.Empty<string>();
        // Choose one common supporting cohort: every retained claim is supported
        // by every listed citation. This keeps the native provenance truthful.
        foreach (var cohort in Cohorts(sources.Keys.ToArray(), minimumSites)) {
            var supported = shared.Where(evidence => cohort.All(evidence.Sites.ContainsKey)).ToArray();
            var retained = Relevant(supported, topic, 12);
            if (retained.Length <= best.Length) continue;
            best = retained; bestSites = cohort;
        }
        // When no statement reaches the corroboration threshold, keep the most
        // supported non-disputed statements as clearly-labelled best-effort notes.
        // Their individual per-claim citations stay truthful because they are not
        // presented as cross-confirmed.
        var tentative = best.Length > 0 ? Array.Empty<Evidence>()
            : Relevant(values.Where(evidence => !evidence.Disputed).ToArray(), topic, 12);
        if (best.Length == 0 && tentative.Length > 0)
            bestSites = tentative.SelectMany(evidence => evidence.Sites.Keys).Distinct(StringComparer.Ordinal)
                .OrderByDescending(site => tentative.Count(evidence => evidence.Sites.ContainsKey(site)))
                .ThenBy(site => site, StringComparer.Ordinal).ToArray();
        return new CorroborationResult(best.Select(evidence => new CorroboratedClaim(evidence.Claim.Text, evidence.Sites.Values.ToArray())).ToArray(),
            bestSites.Select(site => sources[site].Url).ToArray(), disagreements.Take(10).ToArray(), sources.Count,
            tentative.Select(evidence => new CorroboratedClaim(evidence.Claim.Text, evidence.Sites.Values.ToArray())).ToArray());
    }

    // Greedily grows a set of statements connected to the query topic, preferring
    // statements seen on more sites. Relevance is a connection walk from the topic
    // term, so a supporting premise can follow the claim it supports.
    private static Evidence[] Relevant(Evidence[] candidates, string topic, int cap)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal) { topic };
        var ordered = candidates.OrderByDescending(evidence => evidence.Sites.Count).ToArray();
        var selected = new List<Evidence>();
        bool changed;
        do {
            changed = false;
            foreach (var evidence in ordered) {
                if (selected.Contains(evidence)) continue;
                bool relevant = reached.Contains(evidence.Claim.Subject) ||
                    (topic.Length > 0 && Normalize(evidence.Claim.Text).Contains(topic, StringComparison.Ordinal));
                if (!relevant) continue;
                selected.Add(evidence); changed = true;
                if (evidence.Claim.Object.Length > 0) reached.Add(evidence.Claim.Object);
                if (selected.Count >= cap) return selected.ToArray();
            }
        } while (changed && selected.Count < cap);
        return selected.ToArray();
    }

    private static IEnumerable<string[]> Cohorts(string[] sites, int count)
    {
        for (int a = 0; a < sites.Length; a++) for (int b = a + 1; b < sites.Length; b++) {
            if (count == 2) { yield return new[] { sites[a], sites[b] }; continue; }
            for (int c = b + 1; c < sites.Length; c++) yield return new[] { sites[a], sites[b], sites[c] };
        }
    }

    private static Claim? Parse(string input)
    {
        string text = Regex.Replace(input, @"\s+", " ").Trim();
        if (text.Length < 12 || text.Length > 600 || text.EndsWith('?')) return null;
        string normalized = Article(Normalize(text.Replace("isn't", "is not", StringComparison.OrdinalIgnoreCase)
            .Replace("aren't", "are not", StringComparison.OrdinalIgnoreCase).Replace("doesn't", "does not", StringComparison.OrdinalIgnoreCase)
            .Replace("cannot", "can not", StringComparison.OrdinalIgnoreCase)));
        bool negative = Regex.IsMatch(normalized, @"\b(not|never|cannot)\b");
        string neutral = Regex.Replace(normalized, @"\b(not|never)\s+", "");
        string subject = "", target = "", key = neutral;
        var relation = Regex.Match(neutral, @"^(?<subject>.+?) (?<verb>is|are|has|have|needs|need) (?<object>.+)$");
        var does = Regex.Match(neutral, @"^(?<subject>.+?) does (?<verb>have|need) (?<object>.+)$");
        if (does.Success) relation = does;
        if (relation.Success) {
            string verb = relation.Groups["verb"].Value;
            subject = Article(relation.Groups["subject"].Value);
            target = relation.Groups["object"].Value;
            bool category = (verb == "is" || verb == "are") && (Regex.IsMatch(target, @"^(a|an) ") || verb == "are");
            string predicate = category ? "is_a" : verb is "has" or "have" ? "has" : verb is "needs" or "need" ? "needs" : "is";
            target = Article(target);
            if (verb is "are" or "have" or "need") subject = Singular(subject);
            if (category && verb == "are") target = Singular(target);
            key = subject + "|" + predicate + "|" + target;
        }
        string numbers = string.Join(',', Regex.Matches(key, @"[+-]?\d+(?:\.\d+)*").Select(match => match.Value));
        return new Claim(text, key, subject, target, negative, Regex.Replace(key, @"[+-]?\d+(?:\.\d+)*", "#"), numbers);
    }

    private static string Normalize(string text) {
        string normalized = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9+#.\-]+", " ");
        normalized = Regex.Replace(normalized, @"(?<!\d)\.|\.(?!\d)", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }
    private static string Article(string text) => Regex.Replace(text, @"^(a|an|the) ", "");
    private static string Singular(string text) => text.Length > 3 && text.EndsWith('s') && !text.EndsWith("ss") && !text.EndsWith("us") && !text.EndsWith("is") && !text.EndsWith("species") ? text[..^1] : text;
}
