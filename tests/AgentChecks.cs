using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal static class AgentChecks
{
    public static async Task Run(string scratch)
    {
        string workspace = Path.Combine(scratch, "coding-workspace");
        Directory.CreateDirectory(workspace);
        var files = new WorkspaceFiles(workspace);
        var created = files.Write("src/Example.cs", "// todo\npublic class Example { }\n");
        var snapshot = files.Read("src/Example.cs");
        Check(snapshot.Sha256 == created.Sha256 && files.List("*.cs").Contains("src/Example.cs"), "Workspace tools list and read real source files with stable hashes");
        Check(JsonSerializer.Serialize(files.Search("todo")).Contains("src/Example.cs"), "Workspace search locates text in source files");
        var edit = files.Replace("src/Example.cs", "todo", "done", snapshot.Sha256);
        Check(files.Read("src/Example.cs").Content.Contains("done") && edit.Diff.Contains("+// done"), "Precise source edits produce an inspectable diff");
        bool stale = false, outside = false;
        try { files.Write("src/Example.cs", "bad", snapshot.Sha256); } catch (ArgumentException) { stale = true; }
        try { files.Write("../outside.cs", "bad"); } catch (ArgumentException) { outside = true; }
        Check(stale && outside, "Stale edits and paths outside the chosen workspace are rejected");
        var command = await files.RunAsync("dotnet", new[] { "--version" }, 10000, CancellationToken.None);
        Check(command.ExitCode == 0 && !string.IsNullOrWhiteSpace(command.Stdout), "The coding interface runs an actual tool and captures its result");

        var registry = SkillRegistry.CreateDefault();
        var context = new SkillContext(workspace: files);
        var workflow = SkillContext.Schema("""{"steps":[{"skill":"workspace.read","arguments":{"path":"src/Example.cs"}},{"skill":"workspace.replace","arguments":{"path":"src/Example.cs","old_text":"done","new_text":"verified","expected_hash":{"$ref":"steps.0.data.sha256"}}}]}""");
        var flow = await AgentWorkflow.RunAsync(registry, context, workflow);
        Check(flow.Ok && flow.Steps.Count == 2 && files.Read("src/Example.cs").Content.Contains("verified"), "A multi-step workflow uses a real previous tool result as an edit precondition");
        var failed = await AgentWorkflow.RunAsync(registry, context, SkillContext.Schema("""{"steps":[{"skill":"workspace.read","arguments":{"path":"missing.txt"}},{"skill":"workspace.write","arguments":{"path":"should-not-exist.txt","content":"bad"}}]}"""));
        Check(!failed.Ok && failed.Steps.Count == 1 && !File.Exists(Path.Combine(workspace, "should-not-exist.txt")), "A failed tool stops subsequent workflow steps");

        using var brain = new NeuralEngine();
        var coding = new SkillContext("learner", (prompt, maximum) => brain.Generate(prompt, maximum), brain.TrainText, files, brain.TrainDialogue);
        var specification = SkillContext.Schema("""{"name":"TwicePlusOne","variables":["x"],"examples":[{"inputs":[1],"output":3},{"inputs":[2],"output":5},{"inputs":[3],"output":7}],"validation":[{"inputs":[10],"output":21},{"inputs":[-4],"output":-7}],"path":"src/Learned.cs"}""");
        SkillResult synthesis = await registry.ExecuteAsync("code.synthesize", coding, specification);
        Check(synthesis.Ok, "A new arithmetic program is discovered from examples and passes actual C# compilation/execution on separate cases: " + synthesis.Error);
        Check(files.Read("src/Learned.cs").Content.Contains("public static long @TwicePlusOne") &&
            brain.Generate("learned code TwicePlusOne").Contains("public static long @TwicePlusOne"),
            "Validated code is written to the workspace and retained verbatim in the brain");
        var contradictory = SkillContext.Schema("""{"name":"BadFunction","variables":["x"],"examples":[{"inputs":[1],"output":3},{"inputs":[2],"output":5},{"inputs":[3],"output":7}],"validation":[{"inputs":[10],"output":22}]}""");
        var rejected = await registry.ExecuteAsync("code.synthesize", coding, contradictory);
        Check(!rejected.Ok && brain.WordNeuronId("badfunction") < 0, "A training-fit program that fails separate validation is not learned");
        string memory = Path.Combine(scratch, "coding.brain");
        Check(brain.SaveState(memory), "Coding knowledge saves in the same compact brain format");
        using var restored = new NeuralEngine();
        Check(restored.LoadState(memory) && restored.Generate("learned code TwicePlusOne").Contains("public static long @TwicePlusOne"),
            "Learned source code remains executable-looking text after a brain round trip");

        using var models = new ModelRegistry(Path.Combine(scratch, "agent-models"), Path.Combine(AppContext.BaseDirectory, "TeachingMaterials"), workspace);
        models.Create("coder", memory);
        var input = new StringReader("/files *.cs\n/search verified\n/tool workspace.read {\"path\":\"src/Example.cs\"}\n/exit\n");
        var output = new StringWriter();
        int exit = await AgentShell.RunAsync(models, "coder", input, output);
        Check(exit == 0 && output.ToString().Contains("Coding workspace") && output.ToString().Contains("[tool] workspace.list") &&
            output.ToString().Contains("verified") && !output.ToString().Contains("[error]"), "The .NET coding workspace interface executes an end-to-end scripted tool session");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
