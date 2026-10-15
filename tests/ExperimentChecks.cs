using System;
using System.IO;
using System.Linq;

internal static class ExperimentChecks
{
    public static void Run(string scratch)
    {
        using var brain = new NeuralEngine();
        brain.TrainDialogue("describe a red bird", "red bird sings.");
        brain.TrainDialogue("describe a blue bird", "blue bird rests.");
        int before = brain.VocabularyCount;
        string[] proposals = Enumerable.Range(1, 64).Select(seed => brain.Propose("describe a red bird", (uint)seed)).ToArray();
        Check(proposals.Contains("Red bird rests."), "Hypothesis generation recombines learned transitions into an untaught sequence");
        Check(brain.VocabularyCount == before && brain.Generate("describe a red bird") == "Red bird sings.",
            "Inventing hypotheses does not train unverified guesses as facts");

        var tasks = new[]
        {
            new TextExperiment("describe a red bird", "red bird rests."),
            new TextExperiment("what is copperfin?", "copperfin is a fictional fish.")
        };
        ExperimentResult result = TextExperimentRunner.Run(brain, tasks, attempts: 64);
        Check(result.InitiallyCorrect == 0 && result.ValidatedProposals == 1 && result.Corrections == 1 && result.FinalCorrect == 2,
            "Experiments independently validate a hypothesis and learn a teacher correction when search fails");
        string path = Path.Combine(scratch, "experiment.brain");
        Check(brain.SaveState(path), "Experiment learning saves to the same brain format");
        using var restored = new NeuralEngine();
        Check(restored.LoadState(path) && restored.Generate("describe a red bird") == "Red bird rests." &&
              restored.Generate("what is copperfin?") == "Copperfin is a fictional fish.",
            "Validated inventions and teacher corrections survive restart in one file");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
