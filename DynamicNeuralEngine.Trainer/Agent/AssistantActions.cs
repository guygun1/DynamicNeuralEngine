using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public sealed record AssistantAction(string Verb, string Path, string Content);

// Turns a small, explicit set of imperative requests into a concrete workspace
// edit. This is a bounded action router, not general task planning: it writes
// supplied or previously-produced text into a file the user named or has open.
public static class AssistantActions
{
    private static readonly HashSet<string> Verbs = new(StringComparer.OrdinalIgnoreCase) {
        "write", "save", "create", "make", "add", "append", "update", "fix", "put", "generate", "apply", "insert"
    };
    private static readonly Regex PathToken = new(@"(?<![\w./-])((?:[A-Za-z0-9_.-]+[\\/])*[A-Za-z0-9_.-]+\.[A-Za-z0-9]{1,6})(?![\w./-])", RegexOptions.Compiled);
    private static readonly Regex Fence = new("```[^\n]*\n([\\s\\S]*?)```", RegexOptions.Compiled);

    public static bool IsAction(string prompt)
        => !string.IsNullOrWhiteSpace(prompt) && Verbs.Contains(FirstWord(prompt));

    public static AssistantAction? Plan(string prompt, string? activePath, string? previousAssistant)
    {
        if (!IsAction(prompt)) return null;
        string? path = FindPath(prompt) ?? Normalize(activePath);
        if (string.IsNullOrWhiteSpace(path)) return null;
        string content = Content(prompt, previousAssistant ?? "", path!);
        if (string.IsNullOrWhiteSpace(content)) return null;
        return new AssistantAction(FirstWord(prompt), path!, content);
    }

    private static string FirstWord(string prompt)
        => prompt.TrimStart().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('.', ',', ':', '!', '?');

    private static string? FindPath(string prompt)
    {
        string? best = null;
        foreach (Match match in PathToken.Matches(prompt))
        {
            string value = match.Groups[1].Value;
            if (value.Contains("://", StringComparison.Ordinal) || value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (value.Contains('@')) continue;
            best = value;
        }
        return Normalize(best);
    }

    private static string? Normalize(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Replace('\\', '/').Trim();

    private static string Content(string prompt, string previousAssistant, string path)
    {
        var quoted = Regex.Match(prompt, "\"([^\"]{1,4000})\"");
        if (!quoted.Success) quoted = Regex.Match(prompt, "'([^']{1,4000})'");
        if (quoted.Success) return quoted.Groups[1].Value;
        string cleaned = Clean(previousAssistant);
        if (cleaned.Length == 0) return "";
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var fence = Fence.Match(cleaned);
            if (fence.Success) return fence.Groups[1].Value.TrimEnd() + "\n";
        }
        return cleaned;
    }

    // Strips the research framing so a follow-up write stores the answer itself,
    // not the citation block or the uncertainty caveat.
    public static string Clean(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith("Source:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Learned source material:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("I could not confirm this across sources", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Best-effort:", StringComparison.OrdinalIgnoreCase)
                && !line.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase));
        return string.Join('\n', lines).Trim();
    }
}
