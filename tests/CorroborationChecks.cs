using System;
using System.Linq;

internal static class CorroborationChecks
{
    public static void Run()
    {
        const string common = "A zarpleaf is a plant. A plant is a living organism. A plant needs water. ";
        var first = new SourceEvidence("https://www.example.org/one", common + "A zarpleaf has flowers.");
        var second = new SourceEvidence("https://reference.net/two", "The zarpleaf is a plant. Plants are living organisms. Plants need water. This report describes zarpleaf roots.");
        var confirmed = SourceCorroboration.Assess("zarpleaf", new[] { first, second });
        Check(confirmed.Sources.Count == 2 && confirmed.Claims.Count == 3 && !confirmed.Claims.Any(claim => claim.Text.Contains("flowers")),
            "Only shared claims and connected supporting premises are corroborated across sites");
        Check(confirmed.TentativeClaims.Count == 0, "A corroborated result does not also carry tentative best-effort notes");
        var lone = SourceCorroboration.Assess("zarpleaf", new[] { first });
        Check(lone.Claims.Count == 0 && lone.TentativeClaims.Count > 0 && lone.TentativeClaims.Any(claim => claim.Text.Contains("plant")),
            "A single relevant source is offered as a labelled best-effort note when nothing is corroborated");
        var sameSite = SourceCorroboration.Assess("zarpleaf", new[] { first, second with { Url = "https://news.example.org/two" } });
        Check(sameSite.Claims.Count == 0, "Two subdomains of one publisher do not count as two websites");
        var copied = SourceCorroboration.Assess("zarpleaf", new[] { first, first with { Url = "https://copied.net/one" } });
        Check(copied.Claims.Count == 0, "Identical copied page content is not independent corroboration");
        var conflict = SourceCorroboration.Assess("zarpleaf", new[] { first, second,
            new SourceEvidence("https://third.edu/review", "A zarpleaf is not a plant. This source explicitly disputes the zarpleaf classification.") });
        Check(conflict.Claims.Count == 0 && conflict.Disagreements.Count > 0, "A contradicting third site prevents a two-vote majority from confirming the disputed claim");
        var numeric = SourceCorroboration.Assess("zarpleaf", new[] {
            new SourceEvidence("https://first.org/a", common + "A zarpleaf weighs 10 grams."),
            new SourceEvidence("https://second.net/b", common + "A zarpleaf weighs 20 grams.") });
        Check(numeric.Disagreements.Count > 0 && numeric.Claims.All(claim => !claim.Text.Contains("weighs")), "Conflicting numeric claims are withheld while unrelated corroborated facts remain usable");
        Check(SourceCorroboration.Assess("zarpleaf", new[] { first, second }, minimumSites: 3).Claims.Count == 0,
            "A three-site requirement cannot be satisfied by only two websites");
        Check(SourceCorroboration.Site("https://news.publisher.co.uk/a") == SourceCorroboration.Site("https://www.publisher.co.uk/b") &&
            SourceCorroboration.Site("https://other.co.uk/a") != SourceCorroboration.Site("https://publisher.co.uk/b"),
            "Common country-code publisher domains are grouped correctly");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
