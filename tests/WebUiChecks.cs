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

internal static class WebUiChecks
{
    public static async Task Run(string scratch)
    {
        string first = Path.Combine(scratch, "ui-first"), second = Path.Combine(scratch, "ui-second");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "Example.cs"), "// first workspace");
        File.WriteAllText(Path.Combine(second, "Example.cs"), "// second workspace");
        using var registry = new ModelRegistry(Path.Combine(scratch, "ui-models"), Path.Combine(AppContext.BaseDirectory, "TeachingMaterials"), first);
        registry.Create("ui-model");
        await using var app = ModelServer.Build(registry, 0, directoryPicker: _ => Task.FromResult<string?>(second));
        await app.StartAsync();
        try {
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            string html = await client.GetStringAsync("/");
            var javascript = await client.GetAsync("/assets/app.js");
            string js = await javascript.Content.ReadAsStringAsync();
            string css = await client.GetStringAsync("/assets/app.css");
            Check(html.Contains("Choose folder") && html.Contains("chatForm") && html.Contains("editorView") &&
                javascript.IsSuccessStatusCode && js.Contains("/api/ui/workspace") && css.Length > 1000 && html.Length + js.Length + css.Length < 100000,
                "The local website serves its embedded chat, folder-picker, editor, and lightweight assets");
            var picked = await client.PostAsJsonAsync("/api/ui/pick-directory", new { });
            var selection = await picked.Content.ReadFromJsonAsync<JsonElement>();
            Check(selection.GetProperty("selected").GetBoolean() && selection.GetProperty("path").GetString() == second,
                "The folder-picker endpoint returns a selected directory through its injectable native adapter");

            async Task<string> Select(string path) {
                var response = await client.PostAsJsonAsync("/api/ui/workspace", new { path });
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("workspace_id").GetString()!;
            }
            string a = await Select(first), b = await Select(second);
            Check(await Select(first) == a, "Reopening the same folder reuses its session rather than accumulating workspace state");
            async Task<JsonElement> Read(string id) {
                var response = await client.PostAsJsonAsync("/api/skills/execute", new { model = "ui-model", workspace_id = id, skill = "workspace.read", arguments = new { path = "Example.cs" } });
                response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            }
            var left = await Read(a); var right = await Read(b);
            Check(left.GetProperty("content").GetString()!.Contains("first") && right.GetProperty("content").GetString()!.Contains("second"),
                "Separate browser workspace selections resolve the same relative path independently");
            var saved = await client.PostAsJsonAsync("/api/skills/execute", new { model = "ui-model", workspace_id = b, skill = "workspace.write", arguments = new {
                path = "Example.cs", content = "// edited in the web UI", expected_hash = right.GetProperty("sha256").GetString()
            } });
            Check(saved.IsSuccessStatusCode && File.ReadAllText(Path.Combine(second, "Example.cs")).Contains("edited") &&
                File.ReadAllText(Path.Combine(first, "Example.cs")).Contains("first"), "Browser edits apply to the selected directory and respect its file hash");
            var duplicate = await client.PostAsJsonAsync("/api/skills/execute", new { model = "ui-model", workspace_id = b, skill = "workspace.write", arguments = new {
                path = "Example.cs", content = "clobber", create_only = true
            } });
            Check(duplicate.StatusCode == HttpStatusCode.BadRequest && File.ReadAllText(Path.Combine(second, "Example.cs")).Contains("edited"),
                "The new-file action cannot overwrite an existing source file");
            var outside = await client.PostAsJsonAsync("/api/skills/execute", new { model = "ui-model", workspace_id = a, skill = "workspace.read", arguments = new { path = "../ui-second/Example.cs" } });
            Check(outside.StatusCode == HttpStatusCode.BadRequest, "The website's chosen root remains enforced by backend file operations");
            var invalid = await client.PostAsJsonAsync("/api/skills/execute", new { model = "ui-model", workspace_id = "unknown", skill = "workspace.list", arguments = new { } });
            Check(invalid.StatusCode == HttpStatusCode.BadRequest, "An expired or unknown browser workspace is not silently redirected to a different folder");
            var created = await client.PostAsJsonAsync("/api/ui/models", new { name = "ui-created" });
            Check(created.IsSuccessStatusCode && (await client.GetStringAsync("/api/tags")).Contains("ui-created"), "Models can be created and selected through the website API");
            var chat = await client.PostAsJsonAsync("/api/chat", new { model = "ui-model", messages = new[] { new { role = "user", content = "1+1" } }, stream = false, reset_context = true });
            Check(chat.IsSuccessStatusCode && (await chat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetProperty("content").GetString()!.Contains("= 2"),
                "The website's chat payload uses the actual engine and supports fresh conversation context");
        } finally { await app.StopAsync(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
