using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed record TextExperiment(string Prompt, string Reference);
public sealed record ExperimentResult(int Tasks, int InitiallyCorrect, int ValidatedProposals, int Corrections, int FinalCorrect);

public static class TextExperimentRunner
{
    public static IReadOnlyList<TextExperiment> Load(string path)
    {
        var tasks = new List<TextExperiment>();
        var prompts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int number = 0;
        foreach (string raw in File.ReadLines(path))
        {
            number++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split('|');
            if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"{path}:{number}: expected prompt | reference answer.");
            string prompt = parts[0].Trim();
            if (!prompts.Add(Normalize(prompt)))
                throw new InvalidDataException($"{path}:{number}: duplicate experiment prompt.");
            tasks.Add(new TextExperiment(prompt, parts[1].Trim()));
        }
        if (tasks.Count == 0) throw new InvalidDataException("The experiment suite has no tasks.");
        return tasks;
    }

    public static ExperimentResult Run(NeuralEngine brain, IReadOnlyList<TextExperiment> tasks, int attempts = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(attempts, 100);
        if (tasks.Count == 0 || tasks.Any(task => string.IsNullOrWhiteSpace(task.Prompt) || string.IsNullOrWhiteSpace(task.Reference)))
            throw new ArgumentException("Experiment tasks require a prompt and an independent reference answer.");

        // Measure all starting answers before revealing any reference to training.
        var initial = tasks.Select(task => brain.Generate(task.Prompt, 480)).ToArray();
        int initiallyCorrect = 0;
        int validated = 0;
        int corrections = 0;
        for (int index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            bool correct = Equal(initial[index], task.Reference);
            Console.WriteLine($"[Test] {task.Prompt}\n  Initial proposal: {initial[index]} -> {(correct ? "matches reference" : "does not match reference")}");
            if (correct) { initiallyCorrect++; continue; }

            bool accepted = false;
            var seen = new HashSet<string> { Normalize(initial[index]) };
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                string proposal = brain.Propose(task.Prompt, seed: (uint)(index * 101 + attempt), maxChars: 480);
                if (!seen.Add(Normalize(proposal))) continue;
                bool passed = Equal(proposal, task.Reference);
                Console.WriteLine($"  Hypothesis {attempt}: {proposal} -> {(passed ? "matches reference" : "rejected")}");
                if (!passed) continue;
                brain.TrainDialogue(task.Prompt, proposal);
                accepted = true;
                validated++;
                break;
            }
            if (!accepted)
            {
                // Failed guesses never train themselves as facts. A supplied reference
                // acts as teacher feedback when search cannot find a valid hypothesis.
                brain.TrainDialogue(task.Prompt, task.Reference);
                corrections++;
                Console.WriteLine($"  Teacher correction: {task.Reference}");
            }
        }
        int finalCorrect = tasks.Count(task => Equal(brain.Generate(task.Prompt, 480), task.Reference));
        Console.WriteLine($"[Experiments] Before: {initiallyCorrect}/{tasks.Count}; validated hypotheses: {validated}; teacher corrections: {corrections}; after: {finalCorrect}/{tasks.Count}.");
        Console.WriteLine("[Experiments] The after score checks retention of feedback on these same tasks, not independent generalization.");
        return new ExperimentResult(tasks.Count, initiallyCorrect, validated, corrections, finalCorrect);
    }

    private static string Normalize(string value)
    {
        string normalized = value.ToLowerInvariant().Trim().TrimEnd('.', '!', '?');
        normalized = Regex.Replace(normalized, @"([+*/%^=()\-])", " $1 ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }
    private static bool Equal(string left, string right) => Normalize(left) == Normalize(right);
}
