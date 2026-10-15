using System;
using System.IO;

internal static class WordNeuronChecks
{
    public static void Run(string scratch)
    {
        using var brain = new NeuralEngine();
        int vocabulary = brain.VocabularyCount;
        int neurons = brain.NeuronCount;
        brain.StepToken("", true);
        brain.StepToken("   @@@ ### --- ", true);
        brain.TrainText("   @@@ ### --- ");
        Check(brain.NeuronCount == neurons && brain.VocabularyCount == vocabulary, "Empty and noise-only training creates no neurons");
        brain.SpawnNeuron(); // A deliberate modality/core neuron, not a language token.
        brain.SpawnNeuron();
        brain.TrainText("quartz lantern shimmers.");
        int quartz = brain.WordNeuronId("quartz");
        int lantern = brain.WordNeuronId("lantern");
        Check(quartz > neurons && lantern > quartz && brain.WordNeuronId("QUARTZ") == quartz,
            "English words retain their real neuron IDs even after explicit non-language neurogenesis");
        Check(brain.WordConnectionWeight("quartz", "lantern") > 0, "English word sequences build usable neural connections");
        neurons = brain.NeuronCount;
        vocabulary = brain.VocabularyCount;
        for (int i = 0; i < 20; i++) { brain.TrainText("quartz lantern shimmers."); brain.StepToken("QUARTZ", true); }
        Check(brain.NeuronCount == neurons && brain.VocabularyCount == vocabulary,
            "Repeated words and novelty flags reuse neurons instead of creating duplicates");
        brain.Generate("what is an unlearned gizmoberry?");
        brain.Propose("unlearned gizmoberry", 1);
        Check(brain.NeuronCount == neurons && brain.WordNeuronId("gizmoberry") < 0,
            "Queries and unverified hypotheses do not allocate word neurons");
        string path = Path.Combine(scratch, "word-neurons.brain");
        Check(brain.SaveState(path), "Word-neuron mapping saves");
        using var restored = new NeuralEngine();
        Check(restored.LoadState(path) && restored.WordNeuronId("quartz") == quartz &&
            restored.WordConnectionWeight("quartz", "lantern") > 0 && restored.VocabularyCount == vocabulary,
            "Word meanings' neuron identities and connections survive a single-file round trip");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
