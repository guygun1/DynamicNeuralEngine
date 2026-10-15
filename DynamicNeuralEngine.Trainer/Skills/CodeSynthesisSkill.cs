using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed class CodeSynthesisSkill : IAgentSkill
{
    public string Name => "code.synthesize";
    public string Description => "Synthesize a small C# integer function from examples, compile it, test separate validation cases, and retain successful source code. Bounded arithmetic grammar, not arbitrary application generation.";
    public bool RequiresModel => true;
    public bool RequiresWorkspace => true;
    public JsonElement Parameters => SkillContext.Schema("""{"type":"object","properties":{"name":{"type":"string"},"variables":{"type":"array","items":{"type":"string"}},"examples":{"type":"array","minItems":2,"items":{"type":"object","properties":{"inputs":{"type":"array","items":{"type":"integer"}},"output":{"type":"integer"}}}},"validation":{"type":"array"},"path":{"type":"string"}},"required":["name","variables","examples"]}""");

    public async Task<SkillResult> ExecuteAsync(SkillContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        string name = SkillContext.RequiredString(arguments, "name");
        ValidateIdentifier(name);
        if (!arguments.TryGetProperty("variables", out var variableArray) || variableArray.ValueKind != JsonValueKind.Array) throw new ArgumentException("variables must be an array.");
        string[] variables = variableArray.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ArgumentException("Variable names must be strings.")).ToArray();
        if (variables.Length < 1 || variables.Length > 2 || variables.Distinct(StringComparer.Ordinal).Count() != variables.Length) throw new ArgumentException("Provide one or two distinct variable names.");
        foreach (string variable in variables) ValidateIdentifier(variable);
        var examples = ReadExamples(arguments, "examples", variables.Length, required: true);
        var validation = ReadExamples(arguments, "validation", variables.Length, required: false);
        var expression = ProgramSynthesis.Find(variables, examples);
        if (expression is null) return new SkillResult(false, Error: "No expression was found within the supplied arithmetic grammar and search budget.");
        try {
            if (validation.Any(example => expression.Evaluate(example.Inputs) != example.Output))
                return new SkillResult(false, Error: "The first training-fit hypothesis failed separate validation. It was not learned.");
        } catch (ArithmeticException) { return new SkillResult(false, Error: "The hypothesis is undefined on a validation case. It was not learned."); }

        string signature = string.Join(", ", variables.Select(variable => "long @" + variable));
        string code = $"public static partial class LearnedFunctions\n{{\n    public static long @{name}({signature}) => checked({expression.Source});\n}}\n";
        string parent = Path.Combine(Path.GetTempPath(), "opencode");
        Directory.CreateDirectory(parent);
        string temporary = Path.Combine(parent, "dne-code-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "Synthesis.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><UseAppHost>false</UseAppHost><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>""");
            File.WriteAllText(Path.Combine(temporary, "LearnedFunctions.cs"), code);
            var harness = new StringBuilder();
            foreach (var example in examples.Concat(validation))
                harness.AppendLine($"if (LearnedFunctions.@{name}({string.Join(", ", example.Inputs.Select(Literal))}) != {Literal(example.Output)}) throw new Exception(\"Verification failed\");");
            harness.AppendLine("Console.WriteLine(\"All supplied cases passed.\");");
            File.WriteAllText(Path.Combine(temporary, "Program.cs"), harness.ToString());
            var compiler = new WorkspaceFiles(temporary);
            var build = await compiler.RunAsync("dotnet", new[] { "build", "Synthesis.csproj", "--nologo", "--verbosity", "quiet", "--configuration", "Release", "--output", "out", "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false" }, 60000, cancellationToken);
            if (build.ExitCode != 0 || build.TimedOut) return new SkillResult(false, Data: build, Error: "The synthesized source did not compile.");
            var run = await compiler.RunAsync("dotnet", new[] { Path.Combine(temporary, "out", "Synthesis.dll") }, 10000, cancellationToken);
            if (run.ExitCode != 0 || run.TimedOut) return new SkillResult(false, Data: run, Error: "The compiled hypothesis failed its executable checks.");
            FileChange? change = null;
            if (arguments.TryGetProperty("path", out var target))
            {
                if (target.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(target.GetString())) throw new ArgumentException("path must be a nonempty string.");
                string path = target.GetString()!;
                if (File.Exists(context.Workspace!.Resolve(path))) throw new ArgumentException("The synthesis output file already exists. Use workspace.replace for existing source.");
                change = context.Workspace.Write(path, code, createOnly: true);
            }
            context.LearnDialogue("learned code " + name, "```csharp\n" + code + "```");
            return new SkillResult(true, "A bounded arithmetic program was synthesized, compiled, checked, and retained.", new {
                code, expression = expression.Source, candidates = expression.Candidates,
                training_cases = examples.Count, validation_cases = validation.Count, compiled = true, file = change,
                limitation = "Passing finite examples does not prove general correctness; this skill synthesizes only small integer expressions."
            });
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static string Literal(long value) => value == long.MinValue ? "long.MinValue" : value.ToString(CultureInfo.InvariantCulture) + "L";
    private static void ValidateIdentifier(string value) { if (!Regex.IsMatch(value, @"\A[a-zA-Z_][a-zA-Z0-9_]{0,63}\z")) throw new ArgumentException("Use a simple C# identifier for names and variables."); }
    private static List<CodeExample> ReadExamples(JsonElement arguments, string name, int arity, bool required)
    {
        var results = new List<CodeExample>();
        if (!arguments.TryGetProperty(name, out var array)) { if (required) throw new ArgumentException($"{name} is required."); return results; }
        if (array.ValueKind != JsonValueKind.Array) throw new ArgumentException($"{name} must be an array.");
        foreach (var item in array.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array ||
                !item.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Number || !output.TryGetInt64(out long answer)) throw new ArgumentException("Each example needs an integer inputs array and integer output.");
            long[] values = inputs.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : throw new ArgumentException("Example inputs must be integers.")).ToArray();
            if (values.Length != arity) throw new ArgumentException("Example input count must match the variables.");
            results.Add(new CodeExample(values, answer));
            if (results.Count > 32) throw new ArgumentException("At most 32 cases per set are supported.");
        }
        if (required && results.Count < 2) throw new ArgumentException("At least two training examples are required.");
        return results;
    }
}
