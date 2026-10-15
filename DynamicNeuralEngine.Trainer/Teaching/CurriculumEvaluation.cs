using System;
using System.IO;

public static class CurriculumEvaluation
{
    public static void Run(NeuralEngine brain, string path)
    {
        int passed = 0;
        int total = 0;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split('|');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                throw new InvalidDataException($"Invalid evaluation row: {line}");
            string answer = brain.Generate(parts[0].Trim(), maxChars: 180);
            bool correct = answer.Contains(parts[1].Trim(), StringComparison.OrdinalIgnoreCase);
            total++;
            if (correct) passed++;
            Console.WriteLine($"[{(correct ? "HIT" : "MISS")}] {parts[0].Trim()} -> {answer} (expected phrase: {parts[1].Trim()})");
        }
        if (total == 0) throw new InvalidDataException("Evaluation file has no checks.");
        Console.WriteLine($"[Evaluation] {passed}/{total} expected phrases found. This measures phrase recall, not correctness or general English understanding.");
    }
}
