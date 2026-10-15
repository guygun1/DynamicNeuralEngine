using System;

internal static class CompositionalChecks
{
    public static void Run(EnglishCurriculum curriculum)
    {
        using var brain = new NeuralEngine();
        new EnglishCurriculumTrainer(brain).Train(curriculum);
        int nodes = brain.NeuronCount;
        Check(brain.Chat("what was the frence revolution").Contains("1789"),
            "Learned neighboring-word connections resolve French versus France without a memorized typo mapping");
        string full = brain.Chat("what was the frence revolution and the american revolution");
        Check(full.Contains("1789") && full.Contains("1776"), "A new coordinated question combines two separately learned topics");
        string ellipsis = brain.Chat("what was the frence revolution and the american one");
        Check(ellipsis.Contains("1789") && ellipsis.Contains("1776"), "The second topic can reuse the first topic's omitted noun");
        string astronomy = brain.Chat("what is a solar eclipse and a lunar one");
        Check(astronomy.Contains("solar eclipse", StringComparison.OrdinalIgnoreCase) && astronomy.Contains("lunar eclipse", StringComparison.OrdinalIgnoreCase),
            "The same composition/reference mechanism transfers to a different topic pair");
        Check(brain.NeuronCount == nodes, "Compositional inference reuses learned representations");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
