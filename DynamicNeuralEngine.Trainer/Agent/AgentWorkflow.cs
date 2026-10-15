using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

public sealed record WorkflowStepResult(string Skill, SkillResult Result);
public sealed record WorkflowResult(bool Ok, IReadOnlyList<WorkflowStepResult> Steps);

// Explicit, inspectable tool dataflow. This runner does not pretend that a model
// invented a plan: user/client-provided steps execute with real result feedback.
public static class AgentWorkflow
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<WorkflowResult> RunAsync(SkillRegistry skills, SkillContext context, JsonElement task,
        Action<int, string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!task.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() < 1 || steps.GetArrayLength() > 16)
            throw new ArgumentException("A workflow needs 1-16 explicit skill steps.");
        var results = new List<WorkflowStepResult>();
        var values = new List<JsonNode>();
        foreach (var step in steps.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = SkillContext.RequiredString(step, "skill");
            if (!step.TryGetProperty("arguments", out var raw)) throw new ArgumentException("Each step needs arguments.");
            var node = Resolve(JsonNode.Parse(raw.GetRawText()), values);
            using var document = JsonDocument.Parse(node?.ToJsonString() ?? "null");
            progress?.Invoke(results.Count + 1, name);
            SkillResult result;
            try { result = await skills.ExecuteAsync(name, context, document.RootElement, cancellationToken); }
            catch (Exception error) when (error is ArgumentException or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException) {
                result = new SkillResult(false, Error: error.Message);
            }
            results.Add(new WorkflowStepResult(name, result));
            values.Add(JsonSerializer.SerializeToNode(result, JsonOptions)!);
            if (!result.Ok) return new WorkflowResult(false, results);
        }
        return new WorkflowResult(true, results);
    }

    private static JsonNode? Resolve(JsonNode? node, IReadOnlyList<JsonNode> results)
    {
        if (node is JsonObject obj)
        {
            if (obj.Count == 1 && obj.TryGetPropertyValue("$ref", out var reference))
            {
                string[] path = (reference?.GetValue<string>() ?? "").Split('.');
                if (path.Length < 2 || path[0] != "steps" || !int.TryParse(path[1], out int index) || index < 0 || index >= results.Count)
                    throw new ArgumentException("A workflow reference must address an earlier step, for example steps.0.data.sha256.");
                JsonNode? value = results[index];
                for (int i = 2; i < path.Length; i++) {
                    value = value is JsonArray array && int.TryParse(path[i], out int element) && element >= 0 && element < array.Count ? array[element] :
                        value is JsonObject fields && fields.TryGetPropertyValue(path[i], out var field) ? field : throw new ArgumentException("Workflow reference was not found.");
                }
                return value?.DeepClone();
            }
            var resolved = new JsonObject();
            foreach (var field in obj) resolved[field.Key] = Resolve(field.Value, results);
            return resolved;
        }
        if (node is JsonArray items) { var resolved = new JsonArray(); foreach (var item in items) resolved.Add(Resolve(item, results)); return resolved; }
        return node?.DeepClone();
    }
}
