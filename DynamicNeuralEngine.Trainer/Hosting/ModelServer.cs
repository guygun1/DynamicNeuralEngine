using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

public static class ModelServer
{
    public static WebApplication Build(ModelRegistry registry, int port, SkillRegistry? skills = null, Func<System.Threading.CancellationToken, Task<string?>>? directoryPicker = null)
    {
        var skillRegistry = skills ?? SkillRegistry.CreateDefault();
        var workspaces = new WebWorkspaceSessions();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1048576);
        var app = builder.Build();
        app.Use(async (context, next) => {
            try { await next(context); }
            catch (Exception error) when (error is ArgumentException or System.IO.IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
            {
                if (context.Response.HasStarted) { context.Abort(); return; }
                context.Response.StatusCode = error is System.IO.FileNotFoundException ? 404 : error is UnauthorizedAccessException ? 403 : error is System.IO.IOException ? 500 : 400;
                await context.Response.WriteAsJsonAsync(new { error = error.Message });
            }
        });
        app.MapGet("/api/version", () => new { version = "0.1.0-dne" });
        WebUiEndpoints.Map(app, registry, workspaces, directoryPicker);
        app.MapGet("/api/tags", () => new { models = registry.List() });
        app.MapGet("/api/skills", () => new { skills = skillRegistry.Describe() });
        app.MapPost("/api/skills/execute", async (HttpContext context) => {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
            string skill = RequiredString(body, "skill");
            if (!body.TryGetProperty("arguments", out var arguments)) throw new ArgumentException("Skill arguments are required.");
            string? model = body.TryGetProperty("model", out _) ? RequiredString(body, "model") : null;
            var result = await skillRegistry.ExecuteAsync(skill, registry.SkillContextFor(model, workspaces.FromRequest(body)), arguments, context.RequestAborted);
            await context.Response.WriteAsJsonAsync(result);
        });
        app.MapPost("/api/agent/tasks", async (HttpContext context) => {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
            string model = RequiredString(body, "model");
            var result = await AgentWorkflow.RunAsync(skillRegistry, registry.SkillContextFor(model, workspaces.FromRequest(body)), body, cancellationToken: context.RequestAborted);
            await context.Response.WriteAsJsonAsync(result);
        });
        app.MapPost("/api/show", async (HttpContext context) => {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
            string name = RequiredString(body, "model");
            await context.Response.WriteAsJsonAsync(registry.Get(name).Describe(name));
        });
        app.MapPost("/api/generate", (HttpContext context) => Generate(context, registry, skillRegistry, workspaces, chat: false));
        app.MapPost("/api/chat", (HttpContext context) => Generate(context, registry, skillRegistry, workspaces, chat: true));
        return app;
    }

    private static string RequiredString(JsonElement body, string property)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"A nonempty '{property}' string is required.");
        return value.GetString()!;
    }

    private static async Task Generate(HttpContext context, ModelRegistry registry, SkillRegistry skills, WebWorkspaceSessions workspaces, bool chat)
    {
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
        string name = RequiredString(body, "model");
        string prompt;
        string? previousUser = null;
        string? previousAssistant = null;
        if (chat)
        {
            if (!body.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("A messages array is required.");
            string? lastUser = null, lastAssistant = null;
            foreach (var message in messages.EnumerateArray())
            {
                string role = RequiredString(message, "role");
                string content = RequiredString(message, "content");
                if (role == "system") throw new ArgumentException("System prompt instructions are not supported by this text engine.");
                if (role != "user" && role != "assistant") throw new ArgumentException("Message role must be user or assistant.");
                if (role == "user") { previousUser = lastUser; previousAssistant = lastAssistant; lastUser = content; }
                else lastAssistant = content;
            }
            prompt = lastUser ?? throw new ArgumentException("A user message is required.");
        }
        else prompt = RequiredString(body, "prompt");
        if (chat && previousUser is null && body.TryGetProperty("reset_context", out var reset) && reset.ValueKind == JsonValueKind.True) previousUser = "";
        int limit = 8192;
        if (body.TryGetProperty("options", out var options))
        {
            if (options.ValueKind != JsonValueKind.Object) throw new ArgumentException("options must be an object.");
            foreach (var option in options.EnumerateObject())
            {
                if (option.Name != "max_chars" || !option.Value.TryGetInt32(out limit)) throw new ArgumentException("The supported generation option is max_chars (integer).");
            }
        }
        if (limit < 0 || limit > 65536) throw new ArgumentException("max_chars must be between 0 and 65536.");
        bool stream = true;
        if (body.TryGetProperty("stream", out var streaming))
        {
            if (streaming.ValueKind != JsonValueKind.True && streaming.ValueKind != JsonValueKind.False) throw new ArgumentException("stream must be a boolean.");
            stream = streaming.GetBoolean();
        }
        var model = registry.Get(name);
        var local = model.Answer(prompt, limit, chat, previousUser);
        WorkspaceFiles? workspace = null;
        if (chat) { try { workspace = workspaces.FromRequest(body); } catch (ArgumentException) { workspace = null; } }
        string? activePath = body.TryGetProperty("active_path", out var active) && active.ValueKind == JsonValueKind.String ? active.GetString() : null;
        object? actionResult = null;
        LiveAnswer learned;
        if (workspace is not null && AssistantActions.Plan(prompt, activePath, previousAssistant) is { } action)
        {
            bool creates = !File.Exists(workspace.Resolve(action.Path));
            var change = workspace.Write(action.Path, action.Content, createOnly: false);
            actionResult = new { path = change.Path, created = creates, diff = change.Diff };
            string summary = (creates ? "Created " : "Updated ") + change.Path + ".";
            learned = new LiveAnswer(new ModelAnswer(summary, true, false, ""), false);
        }
        else learned = registry.LiveWebLearning ? await LiveWebLearner.AnswerAsync(prompt, local, skills, registry.SkillContextFor(name), query => model.Answer(query, limit, chat), context.RequestAborted) : new LiveAnswer(local, false);
        string reply = learned.Answer.Text;
        if (learned.Tentative && reply.Length > 0)
            reply += "\n\nI could not confirm this across sources, so treat it as a best-effort answer and check anything important.";
        object Payload(string text, bool done) => chat
            ? new { model = name, created_at = DateTime.UtcNow.ToString("O"), message = new { role = "assistant", content = text }, done, context_used = !string.IsNullOrEmpty(previousUser), learned_from_web = learned.LearnedFromWeb, tentative = learned.Tentative, learning_error = learned.LearningError, action = actionResult }
            : (object)new { model = name, created_at = DateTime.UtcNow.ToString("O"), response = text, done, learned_from_web = learned.LearnedFromWeb, tentative = learned.Tentative, learning_error = learned.LearningError };
        if (!stream) { await context.Response.WriteAsJsonAsync(Payload(reply, true)); return; }
        // The engine finishes inference first; NDJSON chunks transport that result.
        context.Response.ContentType = "application/x-ndjson";
        for (int offset = 0; offset < reply.Length; offset += 128)
        {
            await context.Response.WriteAsync(JsonSerializer.Serialize(Payload(reply.Substring(offset, Math.Min(128, reply.Length - offset)), false)) + "\n");
            await context.Response.Body.FlushAsync();
        }
        await context.Response.WriteAsync(JsonSerializer.Serialize(Payload("", true)) + "\n");
    }
}
