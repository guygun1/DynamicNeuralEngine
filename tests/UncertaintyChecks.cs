using System;
using System.IO;

internal static class UncertaintyChecks
{
    public static void Run(EnglishCurriculum curriculum, string scratch)
    {
        using var untrained = new NeuralEngine();
        Check(untrained.Generate("zorbiflux") == "", "An untaught brain has no built-in English fallback");

        using var taught = new NeuralEngine();
        taught.TrainDialogue("what is <unk>?", "i don't know.");
        taught.TrainDialogue("what is ice?", "ice is frozen water.");
        Check(taught.Generate("what is zorbiflux?") == "I don't know.", "Unseen words recall the taught uncertainty response");
        Check(taught.Generate("what is ice?") == "Ice is frozen water.", "Known answers still use their own taught context");

        using var differentLesson = new NeuralEngine();
        differentLesson.TrainDialogue("what is <unk>?", "please explain that word.");
        Check(differentLesson.Generate("what is zorbiflux?") == "Please explain that word.",
            "Changing only the teaching changes the unfamiliar-topic reply");

        taught.TrainDialogue("what is zorbiflux?", "zorbiflux is a made up example word.");
        Check(taught.Generate("what is zorbiflux?") == "Zorbiflux is a made up example word.",
            "Teaching a missing answer replaces uncertainty for that question");

        using var brain = new NeuralEngine();
        new EnglishCurriculumTrainer(brain).Train(curriculum);
        string before = brain.Generate("what is two plus two?");
        Check(before.EndsWith("= 4."), "Arithmetic computes a result instead of mixing global is connections");
        Check(brain.Generate("what is a zorbiflux?") == "I am not sure, but I managed to understand this.", "Bundled uncertainty lessons generalize to a held-out unfamiliar word");
        Check(brain.Generate("what is a quendlebox?") == "I am not sure, but I managed to understand this.", "A second unfamiliar word uses the learned response");
        Check(brain.Generate("what number am i thinking of?").Contains("don't know"), "Missing personal information is taught explicitly");
        brain.Chat("from now on you shall be known as don.");
        brain.Chat("my name is guy.");
        Check(brain.Generate("what is your name?") == "My name is don.", "Assistant identity is scoped to assistant-name questions");
        Check(brain.Generate("what is my name?") == "Your name is guy.", "User identity remains separate from assistant identity");
        Check(brain.Generate("what is two plus two?") == before, "Learning both names leaves arithmetic unchanged");
        Check(brain.Generate("what is two plus three?").EndsWith("= 5."), "Other arithmetic results also remain independent of names");
        Check(brain.Generate("please tell me two plus two") == before, "Paraphrased arithmetic remains independent of names");
        brain.Chat("your name is nova.");
        brain.Chat("my name is alex.");
        Check(brain.Generate("what is your name?") == "My name is nova." && brain.Generate("what is my name?") == "Your name is alex.",
            "Later identity corrections replace the appropriate context");
        Check(brain.Generate("what is two plus two?") == before, "Repeated renaming does not rewrite facts");
        new EnglishCurriculumTrainer(brain).Train(curriculum);
        Check(brain.Generate("what is your name?") == "My name is nova." && brain.Generate("what is my name?") == "Your name is alex.",
            "Re-teaching the general curriculum leaves learned identities intact");

        string path = Path.Combine(scratch, "uncertainty.brain");
        Check(brain.SaveState(path), "Context-specific episodes save in the unified brain file");
        using var restored = new NeuralEngine();
        Check(restored.LoadState(path), "The context-aware brain file reloads");
        Check(restored.Generate("what is a zorbiflux?") == "I am not sure, but I managed to understand this." &&
              restored.Generate("what is two plus two?") == before &&
              restored.Generate("what is your name?") == "My name is nova." &&
              restored.Generate("what is my name?") == "Your name is alex.",
            "Uncertainty, arithmetic, and separate identities survive restart");

        string legacyPath = Path.Combine(scratch, "legacy-v2.brain");
        LegacyBrainFixtures.Write(legacyPath, 2);
        using var upgraded = new NeuralEngine();
        Check(upgraded.LoadState(legacyPath) && upgraded.WordNeuronId("quartz") == 1,
            "Existing AGI2 token brains remain loadable");
        new EnglishCurriculumTrainer(upgraded).Train(curriculum);
        Check(upgraded.Generate("what is a zorbiflux?") == "I am not sure, but I managed to understand this.", "Loaded older brains learn uncertainty from the new lessons");
        foreach (int version in new[] { 3, 4 }) {
            string fixture = Path.Combine(scratch, $"legacy-v{version}.brain");
            LegacyBrainFixtures.Write(fixture, version);
            using var old = new NeuralEngine();
            Check(old.LoadState(fixture) && old.Generate("quartz") == "Quartz.", $"AGI{version} dialogue brains remain readable");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
