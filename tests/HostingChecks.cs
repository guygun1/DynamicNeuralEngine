using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

internal static class HostingChecks
{
    public static async Task Run(string scratch)
    {
        string models = Path.Combine(scratch, "models");
        string workspace = Path.Combine(scratch, "http-workspace");
        Directory.CreateDirectory(workspace);
        using var registry = new ModelRegistry(models, Path.Combine(AppContext.BaseDirectory, "TeachingMaterials"), workspace);
        registry.Create("test-model");
        Check(Directory.GetFiles(models).Length == 1 && File.Exists(Path.Combine(models, "test-model.brain")),
            "A local model consists of one brain file");
        await using var app = ModelServer.Build(registry, 0);
        await app.StartAsync();
        try
        {
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            string tags = await client.GetStringAsync("/api/tags");
            Check(tags.Contains("test-model"), "Ollama-style model listing works over localhost HTTP");
            string skillList = await client.GetStringAsync("/api/skills");
            Check(skillList.Contains("brain.query") && skillList.Contains("web.search") && skillList.Contains("parameters"),
                "HTTP skill discovery exposes tool metadata and argument schemas");
            var skill = await client.PostAsJsonAsync("/api/skills/execute", new {
                model = "test-model", skill = "brain.query", arguments = new { query = "13 * 7" }
            });
            var skillResult = await skill.Content.ReadFromJsonAsync<JsonElement>();
            Check(skillResult.GetProperty("ok").GetBoolean() && skillResult.GetProperty("text").GetString()!.Contains("= 91"),
                "A registered brain skill executes through the local HTTP API");
            var task = await client.PostAsJsonAsync("/api/agent/tasks", JsonSerializer.Deserialize<JsonElement>("""{"model":"test-model","steps":[{"skill":"workspace.write","arguments":{"path":"note.txt","content":"verified workspace"}},{"skill":"workspace.read","arguments":{"path":"note.txt"}}]}"""));
            Check(task.IsSuccessStatusCode && (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ok").GetBoolean() &&
                File.ReadAllText(Path.Combine(workspace, "note.txt")) == "verified workspace", "The agent HTTP endpoint executes explicit workspace workflows");
            var generated = await client.PostAsJsonAsync("/api/generate", new { model = "test-model", prompt = "what is 83*7?", stream = false });
            string math = (await generated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("response").GetString()!;
            Check(generated.IsSuccessStatusCode && math.EndsWith("= 581."), "HTTP generation uses the real arithmetic engine");
            var chat = await client.PostAsJsonAsync("/api/chat", new {
                model = "test-model", messages = new[] { new { role = "user", content = "what is a zorbiflux?" } }, stream = false
            });
            string reply = (await chat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetProperty("content").GetString()!;
            Check(reply.Contains("not sure"), "HTTP chat uses learned uncertainty");
            var session = await client.PostAsJsonAsync("/api/ui/workspace", new { path = workspace });
            string workspaceId = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("workspace_id").GetString()!;
            var writeIt = await client.PostAsJsonAsync("/api/chat", new {
                model = "test-model", workspace_id = workspaceId, active_path = "out.cs", messages = new[] {
                    new { role = "user", content = "explain copy on write" },
                    new { role = "assistant", content = "Copy on write shares data until a write happens." },
                    new { role = "user", content = "write it" }
                }, stream = false
            });
            var action = (await writeIt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("action");
            Check(action.GetProperty("path").GetString() == "out.cs" && File.Exists(Path.Combine(workspace, "out.cs")) &&
                File.ReadAllText(Path.Combine(workspace, "out.cs")).Contains("Copy on write shares data"),
                "An imperative follow-up writes the previous answer into the open file instead of researching");
            var followup = await client.PostAsJsonAsync("/api/chat", new {
                model = "test-model", messages = new[] {
                    new { role = "user", content = "what is a solar eclipse?" },
                    new { role = "assistant", content = "A previous reply." },
                    new { role = "user", content = "explain" }
                }, stream = false
            });
            var contextual = await followup.Content.ReadFromJsonAsync<JsonElement>();
            Check(contextual.GetProperty("context_used").GetBoolean() &&
                contextual.GetProperty("message").GetProperty("content").GetString()!.Contains("Moon"),
                "HTTP follow-ups use the preceding user topic from message history");
            var streaming = await client.PostAsJsonAsync("/api/generate", new { model = "test-model", prompt = "what was world war two?", stream = true });
            var chunks = (await streaming.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
            string joined = string.Concat(chunks.Select(chunk => chunk.GetProperty("response").GetString()));
            Check(chunks.Length > 2 && chunks[^1].GetProperty("done").GetBoolean() && joined.Length > 512 && joined.Contains("1945"),
                "Long responses are transported as valid NDJSON chunks ending with done");
            var limited = await client.PostAsJsonAsync("/api/generate", new { model = "test-model", prompt = "what is photosynthesis?", stream = false, options = new { max_chars = 40 } });
            Check((await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("response").GetString()!.Length <= 40,
                "HTTP response length options are enforced");
            var missing = await client.PostAsJsonAsync("/api/generate", new { model = "missing", prompt = "hello", stream = false });
            Check(missing.StatusCode == HttpStatusCode.NotFound, "Missing models return a structured 404");
            var traversal = await client.PostAsJsonAsync("/api/show", new { model = "../outside" });
            Check(traversal.StatusCode == HttpStatusCode.BadRequest, "Model names resolve only within the registry");
            var invalid = await client.PostAsJsonAsync("/api/generate", new { model = "test-model", prompt = "hello", options = new { temperature = 0.5 } });
            Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Unsupported model options are reported rather than silently ignored");
        }
        finally { await app.StopAsync(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
