using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed class SkillRegistry
{
    private readonly Dictionary<string, IAgentSkill> _skills = new(StringComparer.Ordinal);

    public void Register(IAgentSkill skill)
    {
        if (!Regex.IsMatch(skill.Name, @"\A[a-z][a-z0-9_.-]{1,63}\z") || !_skills.TryAdd(skill.Name, skill))
            throw new ArgumentException($"Invalid or duplicate skill name: {skill.Name}");
    }

    public object[] Describe() => _skills.Values.OrderBy(skill => skill.Name).Select(skill => (object)new {
        name = skill.Name, description = skill.Description, parameters = skill.Parameters,
        requires_model = skill.RequiresModel, requires_workspace = skill.RequiresWorkspace, available = skill.Available
    }).ToArray();

    public async Task<SkillResult> ExecuteAsync(string name, SkillContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!_skills.TryGetValue(name, out var skill)) throw new ArgumentException($"Unknown skill: {name}");
        if (arguments.ValueKind != JsonValueKind.Object) throw new ArgumentException("Skill arguments must be a JSON object.");
        if (skill.RequiresModel && !context.HasModel) throw new ArgumentException($"Skill '{name}' requires a model.");
        if (skill.RequiresWorkspace && context.Workspace is null) throw new ArgumentException($"Skill '{name}' requires a selected workspace.");
        cancellationToken.ThrowIfCancellationRequested();
        return await skill.ExecuteAsync(context, arguments, cancellationToken);
    }

    public static SkillRegistry CreateDefault()
    {
        var registry = new SkillRegistry();
        registry.Register(new BrainQuerySkill());
        registry.Register(new BrainObserveSkill());
        var search = new WebSearchSkill(useWikipediaFallback: true);
        registry.Register(search);
        registry.Register(new WebLearnSkill(search));
        foreach (string operation in new[] { "list", "read", "search", "write", "replace", "run" }) registry.Register(new WorkspaceSkill(operation));
        registry.Register(new CodeSynthesisSkill());
        return registry;
    }
}
