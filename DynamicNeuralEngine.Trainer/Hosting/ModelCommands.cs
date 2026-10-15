using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Hosting;

public static class ModelCommands
{
    private static readonly string[] Commands = { "run", "create", "list", "show", "serve", "ui", "simulate", "skills", "skill", "agent", "models-help" };
    public static bool Handles(string[] args) => args.Length > 0 && Commands.Contains(args[0]);

    public static int Run(string[] args)
    {
        string command = args[0];
        if (command == "models-help") { Help(); return 0; }
        string directory = Environment.GetEnvironmentVariable("DNE_MODELS") ?? Path.Combine(AppContext.BaseDirectory, "models");
        string materials = Path.Combine(AppContext.BaseDirectory, "TeachingMaterials");
        string? name = null, from = null;
        string? skillModel = null;
        string? workspace = null;
        bool learnWeb = false;
        string skillArguments = "{}";
        int port = command == "ui" ? 11436 : 11435;
        int episodes = 300;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--models": directory = Value(args, ref i); break;
                case "--curriculum": materials = Path.GetFullPath(Value(args, ref i)); break;
                case "--from": from = Value(args, ref i); break;
                case "--model": skillModel = Value(args, ref i); break;
                case "--workspace": workspace = Path.GetFullPath(Value(args, ref i)); break;
                case "--learn-web": learnWeb = true; break;
                case "--json": skillArguments = Value(args, ref i); break;
                case "--episodes":
                    if (!int.TryParse(Value(args, ref i), out episodes) || episodes < 1 || episodes > 10000) throw new ArgumentException("Episodes must be between 1 and 10000.");
                    break;
                case "--port":
                    if (!int.TryParse(Value(args, ref i), out port) || port < 1 || port > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
                    break;
                default:
                    if (args[i].StartsWith("--") || name is not null) throw new ArgumentException($"Unexpected argument: {args[i]}");
                    name = args[i]; break;
            }
        }
        if (command is "run" or "create" or "show" or "simulate" or "skill" or "agent" && name is null) throw new ArgumentException($"A name is required for {command}.");
        if (command is "list" or "serve" or "skills" && name is not null) throw new ArgumentException($"{command} does not take a name.");
        if (from is not null && command != "create") throw new ArgumentException("--from is only supported by create.");
        if (command is "agent" or "ui") workspace ??= Directory.GetCurrentDirectory();
        if (command == "ui" && OpenExistingUi(port)) return 0;
        using var registry = new ModelRegistry(directory, materials, workspace, learnWeb);
        var skills = SkillRegistry.CreateDefault();
        switch (command)
        {
            case "create": registry.Create(name!, from); Console.WriteLine($"Created {name}: {registry.PathFor(name!)}"); return 0;
            case "list": Console.WriteLine(JsonSerializer.Serialize(new { models = registry.List() }, new JsonSerializerOptions { WriteIndented = true })); return 0;
            case "skills": Console.WriteLine(JsonSerializer.Serialize(new { skills = skills.Describe() }, new JsonSerializerOptions { WriteIndented = true })); return 0;
            case "skill":
                var arguments = JsonSerializer.Deserialize<JsonElement>(skillArguments);
                var result = skills.ExecuteAsync(name!, registry.SkillContextFor(skillModel), arguments).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                return result.Ok ? 0 : 1;
            case "show": Console.WriteLine(JsonSerializer.Serialize(registry.Get(name!).Describe(name!), new JsonSerializerOptions { WriteIndented = true })); return 0;
            case "run":
                if (!File.Exists(registry.PathFor(name!))) registry.Create(name!);
                return Program.RunText(new[] { "--brain", registry.PathFor(name!), "--curriculum", materials }.Concat(learnWeb ? new[] { "--learn-web" } : Array.Empty<string>()).ToArray());
            case "simulate":
                if (!File.Exists(registry.PathFor(name!))) registry.Create(name!);
                return Program.RunText(new[] { "--brain", registry.PathFor(name!), "--curriculum", materials, "--simulate", episodes.ToString() });
            case "agent":
                if (!File.Exists(registry.PathFor(name!))) registry.Create(name!);
                return AgentShell.RunAsync(registry, name!).GetAwaiter().GetResult();
            case "serve":
                using (var app = ModelServer.Build(registry, port))
                {
                    Console.WriteLine($"DynamicNeuralEngine API: http://127.0.0.1:{port} | Models: {registry.DirectoryPath}");
                    app.RunAsync().GetAwaiter().GetResult();
                }
                return 0;
            case "ui":
                name ??= "current";
                if (!File.Exists(registry.PathFor(name))) registry.Create(name);
                using (var app = ModelServer.Build(registry, port)) {
                    app.StartAsync().GetAwaiter().GetResult();
                    string address = $"http://127.0.0.1:{port}/";
                    Console.WriteLine($"DynamicNeuralEngine web workspace: {address}\nModels: {registry.DirectoryPath}\nPress Ctrl+C here to stop the local server.");
                    OpenBrowser(address);
                    app.WaitForShutdownAsync().GetAwaiter().GetResult();
                }
                return 0;
            default: Help(); return 1;
        }
    }

    private static string Value(string[] args, ref int index)
    {
        if (++index >= args.Length) throw new ArgumentException("Missing option value.");
        return args[index];
    }

    private static bool OpenExistingUi(int port)
    {
        string address = $"http://127.0.0.1:{port}/";
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var config = JsonSerializer.Deserialize<JsonElement>(client.GetStringAsync(address + "api/ui/config").GetAwaiter().GetResult());
            if (config.TryGetProperty("application", out var name) && name.GetString() == "DynamicNeuralEngine" && config.TryGetProperty("ui_version", out _)) {
                OpenBrowser(address); Console.WriteLine("Opened the running web workspace: " + address); return true;
            }
        } catch (Exception error) when (error is HttpRequestException or System.Threading.Tasks.TaskCanceledException or JsonException or InvalidOperationException) { }
        return false;
    }

    private static void OpenBrowser(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { Console.WriteLine("Open this URL in your browser: " + address); }
    }

    private static void Help() => Console.WriteLine("DynamicNeuralEngine local runner\n  dne ui [NAME] [--workspace DIRECTORY] [--learn-web] [--port 11436]\n  dne run NAME [--learn-web]\n  dne agent NAME [--workspace DIRECTORY] [--learn-web]\n  dne create NAME [--from path.brain]\n  dne list\n  dne show NAME\n  dne simulate NAME [--episodes 300]\n  dne skills\n  dne skill SKILL [--model NAME] --json JSON\n  dne serve [--port 11435] [--workspace DIRECTORY] [--learn-web]\nOptions: --models DIRECTORY, --curriculum DIRECTORY\nCoding learning target: C#. Each model is one .brain file.");
}
