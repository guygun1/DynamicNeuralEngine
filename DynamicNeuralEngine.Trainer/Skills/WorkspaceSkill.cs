using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed class WorkspaceSkill : IAgentSkill
{
    private readonly string _operation;
    public WorkspaceSkill(string operation) => _operation = operation;
    public string Name => "workspace." + _operation;
    public bool RequiresModel => false;
    public bool RequiresWorkspace => true;
    public string Description => _operation switch {
        "list" => "List files in the selected workspace, excluding generated directories.",
        "read" => "Read a text file and its hash for subsequent edits.",
        "search" => "Search workspace text files for a literal phrase.",
        "write" => "Write a UTF-8 source file and return a diff.",
        "replace" => "Replace exactly one matching text occurrence and return a diff.",
        "run" => "Run an explicit executable with argument-array input in the workspace and capture bounded output.",
        _ => throw new ArgumentException("Unknown workspace operation.")
    };
    public JsonElement Parameters => SkillContext.Schema(_operation switch {
        "list" => """{"type":"object","properties":{"pattern":{"type":"string"},"limit":{"type":"integer"}}}""",
        "read" => """{"type":"object","properties":{"path":{"type":"string"},"max_chars":{"type":"integer","maximum":200000}},"required":["path"]}""",
        "search" => """{"type":"object","properties":{"text":{"type":"string"},"pattern":{"type":"string"},"limit":{"type":"integer"}},"required":["text"]}""",
        "write" => """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"},"expected_hash":{"type":"string"},"create_only":{"type":"boolean"}},"required":["path","content"]}""",
        "replace" => """{"type":"object","properties":{"path":{"type":"string"},"old_text":{"type":"string"},"new_text":{"type":"string"},"expected_hash":{"type":"string"}},"required":["path","old_text","new_text"]}""",
        "run" => """{"type":"object","properties":{"executable":{"type":"string"},"arguments":{"type":"array","items":{"type":"string"}},"timeout_ms":{"type":"integer","maximum":120000}},"required":["executable"]}""",
        _ => throw new ArgumentException("Unknown workspace operation.")
    });

    public async Task<SkillResult> ExecuteAsync(SkillContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var files = context.Workspace!;
        string Required(string key) => SkillContext.RequiredString(arguments, key);
        string? Optional(string key) => !arguments.TryGetProperty(key, out var value) ? null : value.ValueKind == JsonValueKind.String ? value.GetString() : throw new ArgumentException($"{key} must be a string.");
        string Content(string key) => arguments.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ArgumentException($"A '{key}' string is required.");
        int Integer(string key, int fallback) => !arguments.TryGetProperty(key, out var value) ? fallback : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : throw new ArgumentException($"{key} must be an integer.");
        object data;
        switch (_operation)
        {
            case "list": data = new { files = files.List(Optional("pattern") ?? "*", Integer("limit", 500)) }; break;
            case "read":
                var snapshot = files.Read(Required("path"));
                int maximum = Integer("max_chars", 20000);
                if (maximum < 1 || maximum > 200000) throw new ArgumentException("max_chars must be between 1 and 200000.");
                data = new { path = snapshot.Path, content = snapshot.Content[..Math.Min(maximum, snapshot.Content.Length)], sha256 = snapshot.Sha256, truncated = snapshot.Content.Length > maximum };
                break;
            case "search": data = files.Search(Required("text"), Optional("pattern") ?? "*", Integer("limit", 50)); break;
            case "write":
                bool createOnly = false;
                if (arguments.TryGetProperty("create_only", out var create)) {
                    if (create.ValueKind != JsonValueKind.True && create.ValueKind != JsonValueKind.False) throw new ArgumentException("create_only must be a boolean.");
                    createOnly = create.GetBoolean();
                }
                data = files.Write(Required("path"), Content("content"), Optional("expected_hash"), createOnly); break;
            case "replace": data = files.Replace(Required("path"), Required("old_text"), Content("new_text"), Optional("expected_hash")); break;
            case "run":
                if (arguments.TryGetProperty("arguments", out var invalid) && invalid.ValueKind != JsonValueKind.Array) throw new ArgumentException("Command arguments must be an array.");
                string[] values = arguments.TryGetProperty("arguments", out var array) && array.ValueKind == JsonValueKind.Array
                    ? array.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new ArgumentException("Command arguments must be strings.")).ToArray() : Array.Empty<string>();
                var outcome = await files.RunAsync(Required("executable"), values, Integer("timeout_ms", 30000), cancellationToken);
                return new SkillResult(outcome.ExitCode == 0 && !outcome.TimedOut, Data: outcome, Error: outcome.TimedOut ? "Command timed out." : outcome.ExitCode != 0 ? "Command failed." : null);
            default: throw new ArgumentException("Unknown workspace operation.");
        }
        return new SkillResult(true, Data: data);
    }
}
