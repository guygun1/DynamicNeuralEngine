using System;
using System.IO;

internal static class ReasoningChecks
{
    public static void Run(string scratch)
    {
        using var brain = new NeuralEngine();
        brain.TrainDialogue("<unk>", "i don't know.");
        brain.TrainText("A glintling is a flutterer.");
        brain.TrainText("A flutterer is a creature.");
        brain.TrainText("A creature needs energy.");
        brain.TrainText("A flutterer has wings.");
        brain.TrainText("The brass box is in the green room.");
        brain.TrainText("The green room is in the tower.");
        int neurons = brain.NeuronCount;
        string deduction = brain.Chat("is a glintling a creature?");
        Check(deduction.StartsWith("Yes.") && deduction.Contains("glintling is a flutterer", StringComparison.OrdinalIgnoreCase) &&
            deduction.Contains("flutterer is a creature", StringComparison.OrdinalIgnoreCase),
            "A never-taught conclusion is derived from two independently taught premises");
        Check(brain.Chat("does a glintling need energy?").StartsWith("Yes."), "Properties are inherited across a multi-step type chain");
        Check(brain.Chat("does a glintling have wings?").StartsWith("Yes."), "Inherited possession is inferred rather than looked up as a question-answer pair");
        Check(brain.Chat("is the brass box in the tower?").StartsWith("Yes."), "Location containment is chained across intermediate places");
        Check(brain.Chat("is a flutterer a glintling?").Contains("don't know"), "Inference does not reverse implications without evidence");
        brain.TrainText("A creature is a flutterer.");
        Check(brain.Chat("does a glintling need water?").Contains("don't know"), "Cyclic facts terminate without inventing unsupported properties");
        Check(brain.NeuronCount == neurons, "Reasoning over existing concepts does not allocate derived-answer neurons");
        brain.Chat("A copperfin is a glintling.");
        Check(brain.Chat("is a copperfin a creature?").StartsWith("Yes."), "An ordinary controlled-English teaching statement enables a new three-link deduction");
        brain.TrainText("All otters are mammals.");
        brain.TrainText("A mammal is an animal.");
        Check(brain.Chat("is an otter an animal?").StartsWith("Yes."), "A simple universal statement supplies a reusable category relation");
        string path = Path.Combine(scratch, "concepts.brain");
        Check(brain.SaveState(path), "Typed facts save with the graph");
        using var restored = new NeuralEngine();
        Check(restored.LoadState(path) && restored.Chat("is a copperfin a creature?").StartsWith("Yes."),
            "Learned premises still produce untaught conclusions after restart");

        var report = LearningSandbox.Run(restored, 300);
        Check(report.AfterSuccesses >= 95 && report.AfterSuccesses > report.BeforeSuccesses && report.AddedNeurons < 64,
            "The toy agent improves its policy from environmental rewards using a bounded set of state/action neurons");
        Check(restored.Generate("is a sandbox rover a sandbox object?").StartsWith("Yes.") &&
            restored.Generate("does a sandbox rover have a sandbox battery?").StartsWith("Yes."),
            "Observed sandbox premises support new type and property deductions");
        Check(restored.SaveState(path), "Learned action values save in the same brain file");
        using var policy = new NeuralEngine();
        Check(policy.LoadState(path) && LearningSandbox.Evaluate(policy) == report.AfterSuccesses,
            "The learned control policy survives restart without a separate policy file");
        int policyNodes = policy.NeuronCount;
        LearningSandbox.Run(policy, 30);
        Check(policy.NeuronCount == policyNodes, "More experience in the same finite world reuses its state/action representations");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
