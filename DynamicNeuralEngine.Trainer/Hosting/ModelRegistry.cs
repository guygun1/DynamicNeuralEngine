using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class ModelSession : IDisposable
{
    private readonly object _gate = new();
    private readonly NeuralEngine _brain;
    private readonly string _path;

    public ModelSession(string path, string curriculumDirectory, bool create = false)
    {
        _path = path;
        _brain = new NeuralEngine();
        try
        {
            bool existing = File.Exists(path);
            if (!existing && !create) throw new FileNotFoundException("Model does not exist. Create it first.", path);
            if (existing && !_brain.LoadState(path)) throw new IOException($"Cannot load model: {path}");
            if (Directory.Exists(curriculumDirectory))
            {
                var lessons = EnglishCurriculum.Load(curriculumDirectory, includeBootstrap: !existing);
                if (_brain.CurriculumFingerprint != lessons.Fingerprint)
                {
                    new EnglishCurriculumTrainer(_brain).Train(lessons);
                    Save();
                }
            }
            else if (!existing) throw new DirectoryNotFoundException("Teaching materials are required to create a model.");
        }
        catch { _brain.Dispose(); throw; }
    }

    public string Generate(string prompt, int maxChars, bool chat, string? previousUserMessage = null)
        => Answer(prompt, maxChars, chat, previousUserMessage).Text;

    public ModelAnswer Answer(string prompt, int maxChars, bool chat, string? previousUserMessage = null)
    {
        lock (_gate)
        {
            if (chat && previousUserMessage is not null) _brain.SetConversationTopic(previousUserMessage);
            string response = chat ? _brain.Chat(prompt, maxChars) : _brain.Generate(prompt, maxChars);
            Save();
            return new ModelAnswer(response, _brain.AnswerState == 1, _brain.AnswerState == 2, _brain.ConversationTopic);
        }
    }

    public object Describe(string name)
    {
        lock (_gate) return new {
            name, format = "DNC1/AGI7", family = "dynamic-neural-engine", capabilities = new[] { "completion", "chat", "controlled-fact-inference", "toy-simulation", "source-backed-learning" },
            model_info = new { neurons = _brain.NeuronCount, vocabulary = _brain.VocabularyCount, curriculum = _brain.CurriculumFingerprint }
        };
    }

    public void Observe(string statement)
    {
        lock (_gate) { _brain.TrainText(statement); Save(); }
    }

    public void LearnDialogue(string prompt, string response)
    {
        lock (_gate) { _brain.TrainDialogue(prompt, response); Save(); }
    }

    public void LearnFromSource(string prompt, string passage, string source)
    {
        lock (_gate) { _brain.LearnFromSource(prompt, passage, source); Save(); }
    }

    public SimulationReport Simulate(int episodes)
    {
        lock (_gate) { var report = LearningSandbox.Run(_brain, episodes); Save(); return report; }
    }

    private void Save() { if (!_brain.SaveState(_path)) throw new IOException($"Cannot save model: {_path}"); }
    public void Dispose() { lock (_gate) _brain.Dispose(); }
}

public sealed class ModelRegistry : IDisposable
{
    public string DirectoryPath { get; }
    private readonly string _curriculumDirectory;
    public WorkspaceFiles? Workspace { get; }
    public bool LiveWebLearning { get; }
    private readonly Dictionary<string, ModelSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ModelRegistry(string directory, string curriculumDirectory, string? workspace = null, bool liveWebLearning = false)
    {
        DirectoryPath = Path.GetFullPath(directory);
        _curriculumDirectory = curriculumDirectory;
        Workspace = workspace is null ? null : new WorkspaceFiles(workspace);
        LiveWebLearning = liveWebLearning;
        Directory.CreateDirectory(DirectoryPath);
    }

    public string PathFor(string name)
    {
        if (!Regex.IsMatch(name, @"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}\z"))
            throw new ArgumentException("Model names must contain 1-64 letters, digits, underscores, or hyphens and start with a letter or digit.");
        return Path.Combine(DirectoryPath, name + ".brain");
    }

    public object[] List() => Directory.GetFiles(DirectoryPath, "*.brain").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .Select(path => { var file = new FileInfo(path); return (object)new {
            name = Path.GetFileNameWithoutExtension(path), model = Path.GetFileNameWithoutExtension(path),
            modified_at = file.LastWriteTimeUtc.ToString("O"), size = file.Length,
            details = new { format = "dynamic-brain", family = "dynamic-neural-engine" }
        }; }).ToArray();

    public void Create(string name, string? from = null)
    {
        lock (_gate)
        {
            string path = PathFor(name);
            if (File.Exists(path)) throw new ArgumentException($"Model '{name}' already exists.");
            if (from is not null)
            {
                using var probe = new NeuralEngine();
                if (!probe.LoadState(Path.GetFullPath(from))) throw new IOException("Cannot load the source brain.");
                // Re-serialize, rather than copying an unvalidated source into the registry.
                if (!probe.SaveState(path)) throw new IOException("Cannot create the model brain file.");
            }
            else using (var session = new ModelSession(path, _curriculumDirectory, create: true)) { }
        }
    }

    public ModelSession Get(string name)
    {
        lock (_gate)
        {
            string path = PathFor(name);
            if (!File.Exists(path)) throw new FileNotFoundException($"Model '{name}' does not exist. Use 'dne create {name}'.");
            if (!_sessions.TryGetValue(name, out var session))
                _sessions[name] = session = new ModelSession(path, _curriculumDirectory);
            return session;
        }
    }

    public SkillContext SkillContextFor(string? model, WorkspaceFiles? workspace = null)
    {
        if (model is null) return new SkillContext(workspace: workspace ?? Workspace);
        var session = Get(model);
        return new SkillContext(model, (prompt, limit) => session.Generate(prompt, limit, chat: false), session.Observe, workspace ?? Workspace, session.LearnDialogue, session.LearnFromSource);
    }

    public void Dispose()
    {
        lock (_gate) { foreach (var session in _sessions.Values) session.Dispose(); _sessions.Clear(); }
    }
}
