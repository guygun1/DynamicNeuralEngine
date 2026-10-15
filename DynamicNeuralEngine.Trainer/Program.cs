using System;
using System.IO;
using System.Text.Json;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            return ModelCommands.Handles(args) ? ModelCommands.Run(args) : RunText(args);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            Console.Error.WriteLine($"[Error] {error.Message}");
            return 1;
        }
    }

    internal static int RunText(string[] args)
    {
        string brainPath = Path.GetFullPath(Environment.GetEnvironmentVariable("DNE_DEFAULT_BRAIN") ?? "agi_mind.brain");
        string materialPath = Path.Combine(AppContext.BaseDirectory, "TeachingMaterials");
        bool train = false;
        bool trainOnly = false;
        bool evaluate = false;
        string? experimentPath = null;
        int? simulationEpisodes = null;
        bool learnWeb = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--brain": brainPath = Path.GetFullPath(NextValue(args, ref i)); break;
                case "--curriculum": materialPath = Path.GetFullPath(NextValue(args, ref i)); break;
                case "--train-curriculum": train = true; break;
                case "--train-only": train = trainOnly = true; break;
                case "--evaluate": evaluate = true; break;
                case "--learn-web": learnWeb = true; break;
                case "--experiment": experimentPath = Path.GetFullPath(NextValue(args, ref i)); break;
                case "--simulate":
                    if (!int.TryParse(NextValue(args, ref i), out int count) || count < 1 || count > 10000) throw new ArgumentException("Simulation episodes must be between 1 and 10000.");
                    simulationEpisodes = count; break;
                case "--help":
                    Console.WriteLine("Usage: dotnet run -- [--brain path] [--curriculum directory] [--train-curriculum] [--train-only] [--evaluate] [--experiment reference-file] [--simulate episodes] [--learn-web]\nCoding learning target: C#.");
                    return 0;
                default: throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }

        Console.WriteLine("=== DynamicNeuralEngine: English Text Trainer ===");
        Console.WriteLine($"[Brain] {brainPath}");
        using var brain = new NeuralEngine(50);
        var skills = SkillRegistry.CreateDefault();
        var skillContext = new SkillContext("current", (prompt, limit) => brain.Generate(prompt, limit), statement => { brain.TrainText(statement); Save(brain, brainPath); },
            learnDialogue: (prompt, response) => { brain.TrainDialogue(prompt, response); Save(brain, brainPath); },
            learnFromSource: (prompt, passage, source) => { brain.LearnFromSource(prompt, passage, source); Save(brain, brainPath); });
        bool existing = File.Exists(brainPath);
        if (existing && !brain.LoadState(brainPath))
            throw new IOException($"Could not load existing brain: {brainPath}");
        if (existing && brain.VocabularyCount <= 18)
            throw new IOException("This brain has no trained token vocabulary. Use --brain with a new filename to train a text brain.");

        EnglishCurriculum? availableCurriculum = Directory.Exists(materialPath)
            ? EnglishCurriculum.Load(materialPath, includeBootstrap: !existing) : null;
        bool update = availableCurriculum is not null && brain.CurriculumFingerprint != availableCurriculum.Fingerprint;
        if (!existing || train || update)
        {
            var curriculum = availableCurriculum ?? EnglishCurriculum.Load(materialPath, includeBootstrap: !existing);
            if (existing && update) Console.WriteLine("[Curriculum Update] Teaching updated lesson files; learned identity contexts are retained.");
            new EnglishCurriculumTrainer(brain).Train(curriculum);
            Save(brain, brainPath);
        }
        else
        {
            Console.WriteLine($"[Loaded] {brain.NeuronCount} neurons, {brain.VocabularyCount} vocabulary entries.");
            Console.WriteLine("[Curriculum] Saved lessons are current, or source teaching files are unavailable.");
        }

        if (evaluate)
            CurriculumEvaluation.Run(brain, Path.Combine(materialPath, "english.checks.txt"));
        if (experimentPath is not null)
        {
            TextExperimentRunner.Run(brain, TextExperimentRunner.Load(experimentPath));
            Save(brain, brainPath);
        }
        if (simulationEpisodes is not null)
        {
            LearningSandbox.Run(brain, simulationEpisodes.Value);
            Save(brain, brainPath);
        }
        if (trainOnly || evaluate || experimentPath is not null || simulationEpisodes is not null) return 0;

        Console.WriteLine("Commands: /teach question | answer, /text sentence, /curriculum, /experiment [reference-file], /simulate [episodes], /skills, /skill name JSON, /word word, /stats, exit\n");
        while (true)
        {
            Console.Write("User > ");
            string? input = Console.ReadLine()?.Trim();
            if (input is null || input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
            if (input.Length == 0) continue;

            if (input.Equals("/curriculum", StringComparison.OrdinalIgnoreCase))
            {
                new EnglishCurriculumTrainer(brain).Train(EnglishCurriculum.Load(materialPath));
                Save(brain, brainPath);
            }
            else if (input.Equals("/stats", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[Stats] {brain.NeuronCount} neurons; {brain.VocabularyCount} vocabulary entries.");
            }
            else if (input.Equals("/skills", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(JsonSerializer.Serialize(skills.Describe(), new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (input.StartsWith("/skill ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string call = input[7..].Trim();
                    int separator = call.IndexOf(' ');
                    string name = separator < 0 ? call : call[..separator];
                    var arguments = JsonSerializer.Deserialize<JsonElement>(separator < 0 ? "{}" : call[(separator + 1)..]);
                    var result = skills.ExecuteAsync(name, skillContext, arguments).GetAwaiter().GetResult();
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception error) when (error is ArgumentException or JsonException) { Console.WriteLine($"[Skill error] {error.Message}"); }
            }
            else if (input.StartsWith("/word ", StringComparison.OrdinalIgnoreCase))
            {
                string word = input[6..].Trim();
                int id = brain.WordNeuronId(word);
                Console.WriteLine(id >= 0 ? $"[Word] {word}: neuron #{id}" : $"[Word] No learned neuron for '{word}'.");
            }
            else if (input.Equals("/simulate", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/simulate ", StringComparison.OrdinalIgnoreCase))
            {
                int count = 300;
                if (input.Length > 9 && (!int.TryParse(input[10..].Trim(), out count) || count < 1 || count > 10000))
                {
                    Console.WriteLine("[Usage] /simulate [episodes from 1 to 10000]"); continue;
                }
                LearningSandbox.Run(brain, count);
                Save(brain, brainPath);
            }
            else if (input.Equals("/experiment", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/experiment ", StringComparison.OrdinalIgnoreCase))
            {
                string path = input.Length > 11 ? input[12..].Trim().Trim('"') : Path.Combine(materialPath, "text.experiments.txt");
                TextExperimentRunner.Run(brain, TextExperimentRunner.Load(path));
                Save(brain, brainPath);
            }
            else if (input.StartsWith("/teach ", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = input[7..].Split('|');
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                {
                    Console.WriteLine("[Usage] /teach question | answer");
                    continue;
                }
                for (int repeat = 0; repeat < 3; repeat++)
                    brain.TrainDialogue(parts[0].Trim(), parts[1].Trim());
                Save(brain, brainPath);
                Console.WriteLine("[Learning] Dialogue association trained.");
            }
            else if (input.StartsWith("/text ", StringComparison.OrdinalIgnoreCase))
            {
                brain.TrainText(input[6..].Trim());
                Save(brain, brainPath);
                Console.WriteLine($"[Learning] Text trained; vocabulary: {brain.VocabularyCount}.");
            }
            else
            {
                ModelAnswer Local(string prompt) { string text = brain.Chat(prompt); return new ModelAnswer(text, brain.AnswerState == 1, brain.AnswerState == 2, brain.ConversationTopic); }
                var local = Local(input);
                if (learnWeb && local.CanSearch) {
                    Console.WriteLine("[learn] Searching and reading a source for missing knowledge.");
                    var learned = LiveWebLearner.AnswerAsync(input, local, skills, skillContext, Local).GetAwaiter().GetResult();
                    if (learned.LearningError is not null) Console.WriteLine("[learn] " + learned.LearningError);
                    local = learned.Answer;
                }
                Console.WriteLine($"AI > {local.Text}");
                Save(brain, brainPath);
            }
        }
        return 0;
    }

    private static string NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
            throw new ArgumentException($"Missing value for {args[index]}.");
        return args[++index];
    }

    private static void Save(NeuralEngine brain, string path)
    {
        if (!brain.SaveState(path)) throw new IOException($"Could not save brain: {path}");
    }
}
