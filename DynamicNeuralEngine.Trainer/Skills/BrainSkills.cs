using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed class BrainQuerySkill : IAgentSkill
{
    public string Name => "brain.query";
    public string Description => "Query a model's learned knowledge, controlled-fact inference, or arithmetic skill.";
    public bool RequiresModel => true;
    public JsonElement Parameters => SkillContext.Schema("""{"type":"object","properties":{"query":{"type":"string"},"max_chars":{"type":"integer","minimum":0,"maximum":65536}},"required":["query"]}""");
    public Task<SkillResult> ExecuteAsync(SkillContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        string query = SkillContext.RequiredString(arguments, "query");
        int maximum = 8192;
        if (arguments.TryGetProperty("max_chars", out var length) && (!length.TryGetInt32(out maximum) || maximum < 0 || maximum > 65536))
            throw new ArgumentException("max_chars must be between 0 and 65536.");
        return Task.FromResult(new SkillResult(true, context.Query(query, maximum)));
    }
}

public sealed class BrainObserveSkill : IAgentSkill
{
    public string Name => "brain.observe";
    public string Description => "Explicitly teach a text observation or controlled-English premise to the selected model and retain it.";
    public bool RequiresModel => true;
    public JsonElement Parameters => SkillContext.Schema("""{"type":"object","properties":{"statement":{"type":"string"}},"required":["statement"]}""");
    public Task<SkillResult> ExecuteAsync(SkillContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        string statement = SkillContext.RequiredString(arguments, "statement");
        context.Observe(statement);
        return Task.FromResult(new SkillResult(true, "Observation recorded."));
    }
}
