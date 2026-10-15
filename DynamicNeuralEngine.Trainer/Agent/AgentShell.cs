using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

public static class AgentShell
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task<int> RunAsync(ModelRegistry models, string name, TextReader? input = null, TextWriter? output = null)
    {
        input ??= Console.In; output ??= Console.Out;
        if (OperatingSystem.IsWindows() && ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected)
            Console.Title = "DynamicNeuralEngine - C# coding workspace";
        if (models.Workspace is null) throw new ArgumentException("The coding interface needs a workspace.");
        var model = models.Get(name);
        var skills = SkillRegistry.CreateDefault();
        var context = models.SkillContextFor(name);
        await output.WriteLineAsync($"DynamicNeuralEngine | Coding workspace\nModel: {name} | Workspace: {models.Workspace.Root}\nCoding target: C# | Live web learning: {(models.LiveWebLearning ? "on" : "off")}\n/help lists tools; /synthesize accepts example-based C# tasks; plain input talks to the experimental brain.");
        while (true)
        {
            await output.WriteAsync("\ndne > ");
            string? line = (await input.ReadLineAsync())?.Trim();
            if (line is null || line.Equals("/exit", StringComparison.OrdinalIgnoreCase) || line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
            if (line.Length == 0) continue;
            try
            {
                if (line == "/help") {
                    await output.WriteLineAsync("/files [pattern] | /read path | /search text | /web query | /learn-web query\n/tool SKILL JSON | /synthesize JSON | /task JSON | /skills | /model | /simulate [episodes] | /exit\nWorkspace edits return diffs. Coding synthesis covers C# integer functions. --learn-web enables automatic source acquisition for unsupported knowledge questions.");
                    continue;
                }
                if (line == "/skills") { await Render(output, skills.Describe()); continue; }
                if (line == "/model") { await Render(output, model.Describe(name)); continue; }
                if (line.StartsWith("/task ", StringComparison.Ordinal)) {
                    var task = JsonSerializer.Deserialize<JsonElement>(line[6..]);
                    var result = await AgentWorkflow.RunAsync(skills, context, task, (step, skill) => output.WriteLine($"[step {step}] {skill}"));
                    await Render(output, result); continue;
                }
                if (line == "/simulate" || line.StartsWith("/simulate ", StringComparison.Ordinal)) {
                    int episodes = line == "/simulate" ? 300 : int.Parse(line[10..]);
                    await Render(output, model.Simulate(episodes)); continue;
                }
                string? tool = null;
                object arguments = new { };
                if (line == "/files" || line.StartsWith("/files ", StringComparison.Ordinal)) { tool = "workspace.list"; arguments = new { pattern = line.Length > 6 ? line[7..] : "*" }; }
                else if (line.StartsWith("/read ", StringComparison.Ordinal)) { tool = "workspace.read"; arguments = new { path = line[6..].Trim('"') }; }
                else if (line.StartsWith("/search ", StringComparison.Ordinal)) { tool = "workspace.search"; arguments = new { text = line[8..] }; }
                else if (line.StartsWith("/web ", StringComparison.Ordinal)) { tool = "web.search"; arguments = new { query = line[5..] }; }
                else if (line.StartsWith("/learn-web ", StringComparison.Ordinal)) { tool = "web.learn"; arguments = new { query = line[11..] }; }
                else if (line.StartsWith("/synthesize ", StringComparison.Ordinal)) { tool = "code.synthesize"; arguments = JsonSerializer.Deserialize<JsonElement>(line[12..]); }
                else if (line.StartsWith("/tool ", StringComparison.Ordinal)) {
                    string call = line[6..]; int space = call.IndexOf(' ');
                    tool = space < 0 ? call : call[..space];
                    arguments = JsonSerializer.Deserialize<JsonElement>(space < 0 ? "{}" : call[(space + 1)..]);
                }
                if (tool is not null) {
                    await output.WriteLineAsync($"[tool] {tool}");
                    var result = await skills.ExecuteAsync(tool, context, JsonSerializer.SerializeToElement(arguments));
                    await Render(output, result); continue;
                }
                var local = model.Answer(line, 8192, chat: true);
                if (models.LiveWebLearning && local.CanSearch) {
                    await output.WriteLineAsync("[learn] Searching and reading a source for missing knowledge.");
                    var learned = await LiveWebLearner.AnswerAsync(line, local, skills, context, query => model.Answer(query, 8192, chat: true));
                    if (learned.LearningError is not null) await output.WriteLineAsync("[learn] " + learned.LearningError);
                    local = learned.Answer;
                }
                await output.WriteLineAsync(local.Text);
            }
            catch (Exception error) when (error is ArgumentException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException) {
                await output.WriteLineAsync("[error] " + error.Message);
            }
        }
        return 0;
    }

    private static Task Render(TextWriter output, object value)
    {
        string text = JsonSerializer.Serialize(value, JsonOptions);
        if (text.Length > 16000) text = text[..16000] + "\n[display truncated]";
        return output.WriteLineAsync(text);
    }
}
