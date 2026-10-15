using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

public interface IAgentSkill
{
    string Name { get; }
    string Description { get; }
    JsonElement Parameters { get; }
    bool RequiresModel { get; }
    bool RequiresWorkspace => false;
    bool Available => true;
    Task<SkillResult> ExecuteAsync(SkillContext context, JsonElement arguments, CancellationToken cancellationToken);
}

public sealed record SkillResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("data")] object? Data = null,
    [property: JsonPropertyName("error")] string? Error = null);

public sealed class SkillContext
{
    public string? Model { get; }
    private readonly Func<string, int, string>? _query;
    private readonly Action<string>? _observe;
    private readonly Action<string, string>? _learnDialogue;
    private readonly Action<string, string, string>? _learnFromSource;
    public WorkspaceFiles? Workspace { get; }
    public bool HasModel => _query is not null;

    public SkillContext(string? model = null, Func<string, int, string>? query = null, Action<string>? observe = null,
        WorkspaceFiles? workspace = null, Action<string, string>? learnDialogue = null, Action<string, string, string>? learnFromSource = null)
    { Model = model; _query = query; _observe = observe; Workspace = workspace; _learnDialogue = learnDialogue; _learnFromSource = learnFromSource; }

    public string Query(string prompt, int maxChars = 8192) => (_query ?? throw new InvalidOperationException("This skill requires a loaded model."))(prompt, maxChars);
    public void Observe(string statement) => (_observe ?? throw new InvalidOperationException("This skill requires a writable model."))(statement);
    public void LearnDialogue(string prompt, string response) => (_learnDialogue ?? throw new InvalidOperationException("This skill requires a writable model."))(prompt, response);
    public void LearnFromSource(string prompt, string passage, string source) => (_learnFromSource ?? throw new InvalidOperationException("This skill requires source-backed model learning."))(prompt, passage, source);

    public static string RequiredString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"A nonempty '{name}' string is required.");
        return value.GetString()!;
    }
    public static JsonElement Schema(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
}
