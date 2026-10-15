using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed record FileSnapshot(string Path, string Content, string Sha256);
public sealed record FileChange(string Path, string Sha256, string Diff);
public sealed record CommandOutcome(int ExitCode, string Stdout, string Stderr, bool TimedOut);

public sealed class WorkspaceFiles
{
    public string Root { get; }
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase) { ".git", ".idea", ".dne", "bin", "obj", "node_modules" };
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private const int MaximumFileBytes = 2 * 1024 * 1024;

    public WorkspaceFiles(string root)
    {
        string full = System.IO.Path.GetFullPath(root);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Workspace does not exist: {full}");
        var info = new DirectoryInfo(full);
        Root = (info.Attributes & FileAttributes.ReparsePoint) != 0 ? info.ResolveLinkTarget(true)!.FullName : full;
        Root = System.IO.Path.TrimEndingDirectorySeparator(Root);
    }

    private bool Inside(string path) => path.Equals(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
        path.StartsWith(System.IO.Path.EndsInDirectorySeparator(Root) ? Root : Root + System.IO.Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public string Resolve(string path)
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, path));
        if (!Inside(full)) throw new ArgumentException("The path is outside the selected workspace.");
        string relative = System.IO.Path.GetRelativePath(Root, full);
        string cursor = Root;
        foreach (string part in relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals(".git", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Git metadata is not a workspace editing target.");
            cursor = System.IO.Path.Combine(cursor, part);
            if (!File.Exists(cursor) && !Directory.Exists(cursor)) continue;
            var attributes = File.GetAttributes(cursor);
            if ((attributes & FileAttributes.ReparsePoint) == 0) continue;
            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(cursor) : new FileInfo(cursor);
            var target = info.ResolveLinkTarget(true);
            if (target is null || !Inside(System.IO.Path.GetFullPath(target.FullName))) throw new ArgumentException("The path resolves outside the selected workspace.");
        }
        return full;
    }

    public string Relative(string path) => System.IO.Path.GetRelativePath(Root, path).Replace('\\', '/');

    public IReadOnlyList<string> List(string pattern = "*", int limit = 500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 2000);
        var results = new List<string>();
        var directories = new Stack<string>(); directories.Push(Root);
        while (directories.Count > 0 && results.Count < limit)
        {
            string directory = directories.Pop();
            foreach (string child in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
                string name = System.IO.Path.GetFileName(child);
                if (Ignored.Contains(name) || name.StartsWith("cmake-build", StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                directories.Push(child);
            }
            foreach (string file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                string relative = Relative(file);
                if (!FileSystemName.MatchesSimpleExpression(pattern, relative, ignoreCase: true)) continue;
                results.Add(relative); if (results.Count == limit) break;
            }
        }
        return results;
    }

    public FileSnapshot Read(string path)
    {
        string full = Resolve(path);
        if (new FileInfo(full).Length > MaximumFileBytes) throw new ArgumentException("File exceeds the 2 MiB text limit.");
        byte[] bytes = File.ReadAllBytes(full);
        if (bytes.Take(1024).Contains((byte)0) && !(bytes.Length > 1 && (bytes[0] == 0xff && bytes[1] == 0xfe || bytes[0] == 0xfe && bytes[1] == 0xff)))
            throw new ArgumentException("The selected file appears to be binary.");
        return new FileSnapshot(Relative(full), File.ReadAllText(full), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public FileChange Write(string path, string content, string? expectedHash = null, bool createOnly = false)
    {
        if (Utf8.GetByteCount(content) > MaximumFileBytes) throw new ArgumentException("Content exceeds the 2 MiB text limit.");
        string full = Resolve(path);
        if (createOnly && File.Exists(full)) throw new ArgumentException("The file already exists. Open it in the editor instead.");
        string before = "";
        if (File.Exists(full)) {
            var old = Read(path); before = old.Content;
            if (expectedHash is not null && !old.Sha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The file changed since it was read.");
        } else if (expectedHash is not null) throw new ArgumentException("The expected existing file is missing.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".writing";
        try { File.WriteAllText(temporary, content, Utf8); File.Move(temporary, full, overwrite: !createOnly); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        string diff = $"--- {Relative(full)}\n+++ {Relative(full)}\n" +
            string.Join('\n', before.Split('\n').Take(80).Select(line => "-" + line)) + "\n" +
            string.Join('\n', content.Split('\n').Take(80).Select(line => "+" + line));
        if (diff.Length > 16000) diff = diff[..16000] + "\n[diff display truncated]";
        return new FileChange(Relative(full), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))), diff);
    }

    public FileChange Replace(string path, string oldText, string newText, string? expectedHash = null)
    {
        if (oldText.Length == 0) throw new ArgumentException("old_text must not be empty.");
        var old = Read(path);
        int start = old.Content.IndexOf(oldText, StringComparison.Ordinal);
        if (start < 0 || old.Content.IndexOf(oldText, start + oldText.Length, StringComparison.Ordinal) >= 0) throw new ArgumentException("old_text must match exactly one occurrence.");
        if (expectedHash is not null && !old.Sha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The file changed since it was read.");
        return Write(path, old.Content[..start] + newText + old.Content[(start + oldText.Length)..], old.Sha256);
    }

    public object Search(string text, string pattern = "*", int limit = 50)
    {
        if (string.IsNullOrEmpty(text) || limit < 1 || limit > 200) throw new ArgumentException("Search needs text and a limit from 1 to 200.");
        var matches = new List<object>();
        foreach (string file in List(pattern, 2000)) {
            FileSnapshot snapshot;
            try { snapshot = Read(file); } catch (ArgumentException) { continue; }
            string[] lines = snapshot.Content.Split('\n');
            for (int line = 0; line < lines.Length; line++) {
                if (!lines[line].Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
                matches.Add(new { path = file, line = line + 1, text = lines[line][..Math.Min(lines[line].Length, 1000)] });
                if (matches.Count == limit) return new { matches, limited = true };
            }
        }
        return new { matches, limited = false };
    }

    public async Task<CommandOutcome> RunAsync(string executable, string[] arguments, int timeoutMs, CancellationToken cancellationToken)
    {
        if (timeoutMs < 100 || timeoutMs > 120000 || arguments.Length > 64) throw new ArgumentException("Command timeout must be 100-120000 ms, with at most 64 arguments.");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); } catch (Win32Exception error) { return new CommandOutcome(-1, "", error.Message, false); }
        var stdout = Capture(process.StandardOutput); var stderr = Capture(process.StandardError);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        bool timedOut = false;
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (cancellationToken.IsCancellationRequested) throw;
            timedOut = true;
        }
        return new CommandOutcome(process.ExitCode, await stdout, await stderr, timedOut);
    }

    private static async Task<string> Capture(StreamReader reader)
    {
        var output = new StringBuilder(); var buffer = new char[2048];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0) {
            int remaining = 24000 - output.Length;
            if (remaining > 0) output.Append(buffer, 0, Math.Min(read, remaining));
        }
        return output.ToString();
    }
}
