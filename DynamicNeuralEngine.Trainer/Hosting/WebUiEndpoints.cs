using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

public sealed class WebWorkspaceSessions
{
    private readonly ConcurrentDictionary<string, WorkspaceFiles> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public (string Id, WorkspaceFiles Workspace) Select(string path)
    {
        var workspace = new WorkspaceFiles(path);
        lock (_gate) {
            foreach (var entry in _sessions)
                if (entry.Value.Root.Equals(workspace.Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return (entry.Key, entry.Value);
            if (_sessions.Count >= 128) throw new ArgumentException("Too many workspace selections. Restart the local server to clear old sessions.");
            string id = Guid.NewGuid().ToString("N");
            _sessions[id] = workspace;
            return (id, workspace);
        }
    }
    public WorkspaceFiles? FromRequest(JsonElement body)
    {
        if (!body.TryGetProperty("workspace_id", out var value)) return null;
        if (value.ValueKind != JsonValueKind.String || !_sessions.TryGetValue(value.GetString()!, out var workspace))
            throw new ArgumentException("Select a development directory again; the workspace session is unknown.");
        return workspace;
    }
}

public static class WebUiEndpoints
{
    public static void Map(WebApplication app, ModelRegistry models, WebWorkspaceSessions sessions,
        Func<CancellationToken, Task<string?>>? picker = null)
    {
        app.MapGet("/", () => Asset("index.html", "text/html; charset=utf-8"));
        app.MapGet("/assets/app.css", () => Asset("app.css", "text/css; charset=utf-8"));
        app.MapGet("/assets/app.js", () => Asset("app.js", "text/javascript; charset=utf-8"));
        app.MapGet("/api/ui/config", () => new {
            application = "DynamicNeuralEngine", ui_version = 1, coding_language = "C#",
            workspace = models.Workspace?.Root ?? Directory.GetCurrentDirectory(),
            live_web_learning = models.LiveWebLearning, native_picker = OperatingSystem.IsWindows()
        });
        app.MapPost("/api/ui/workspace", async (HttpContext context) => {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
            var selected = sessions.Select(SkillContext.RequiredString(body, "path"));
            await context.Response.WriteAsJsonAsync(new { workspace_id = selected.Id, root = selected.Workspace.Root });
        });
        app.MapPost("/api/ui/pick-directory", async (HttpContext context) => {
            string? path = await (picker ?? NativeDirectoryPicker.PickAsync)(context.RequestAborted);
            await context.Response.WriteAsJsonAsync(new { selected = path is not null, path });
        });
        app.MapPost("/api/ui/models", async (HttpContext context) => {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
            string name = SkillContext.RequiredString(body, "name");
            models.Create(name);
            await context.Response.WriteAsJsonAsync(models.Get(name).Describe(name));
        });
    }

    private static IResult Asset(string file, string contentType)
    {
        var assembly = typeof(WebUiEndpoints).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".WebUI." + file)
            ?? throw new InvalidOperationException("The web UI resource is missing. Rebuild the application.");
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        return Results.Bytes(memory.ToArray(), contentType);
    }
}

public static class NativeDirectoryPicker
{
    public static async Task<string?> PickAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Use the directory path field on this platform.");
        const string script = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); Add-Type -AssemblyName System.Windows.Forms; " +
            "$dialog = [System.Windows.Forms.FolderBrowserDialog]::new(); $dialog.Description = 'Choose your development directory'; " +
            "$dialog.ShowNewFolderButton = $true; if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($dialog.SelectedPath) }; $dialog.Dispose()";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in new[] { "-NoProfile", "-STA", "-Command", script }) start.ArgumentList.Add(argument);
        start.StandardOutputEncoding = System.Text.Encoding.UTF8;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not open the folder picker.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested) throw;
            throw new ArgumentException("Directory selection timed out. You can also enter a path directly.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("Could not open the folder picker: " + await stderr);
        string path = (await stdout).Trim();
        return path.Length == 0 ? null : path;
    }
}
