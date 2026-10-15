using System;
using System.IO;
using System.Linq;

internal static class ConversationChecks
{
    public static void Run(EnglishCurriculum curriculum, string scratch)
    {
        using var brain = new NeuralEngine();
        new EnglishCurriculumTrainer(brain).Train(curriculum);
        int neurons = brain.NeuronCount;
        string eclipse = brain.Chat("whats a solar clipse");
        Check(eclipse.Contains("Moon") && eclipse.Contains("Sun") && !eclipse.Contains("don't know"),
            "The reported misspelled eclipse question recovers a learned topic");
        string path = Path.Combine(scratch, "conversation.brain");
        Check(brain.SaveState(path), "A conversation's current topic saves inside the brain");
        using var restored = new NeuralEngine();
        Check(restored.LoadState(path), "Compact conversational state reloads");
        string followup = restored.Chat("explain");
        Check(followup.Contains("eclipse") && followup.Length > eclipse.Length,
            "A one-word follow-up elaborates the preceding topic after restart");
        string essay = brain.Chat("write a eassy about food");
        Check(essay.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 200 && essay.Contains("nutrient") &&
            essay.Contains("\n\n") && !curriculum.Units.SelectMany(unit => unit.Dialogues).Any(lesson => lesson.Response == essay),
            "The reported essay request combines multiple learned passages instead of one canned essay");
        Check(brain.Chat("food").Length > 300, "A bare known topic produces a substantive explanation");
        Check(brain.NeuronCount == neurons, "Typos, follow-ups, and composition do not allocate new neurons");
        brain.TrainDialogue("copperfin", "Copperfin is a fictional creature in this test story. It has bright copper colored fins and lives in imaginary freshwater streams.");
        brain.TrainDialogue("copperfin habitat", "In this fictional setting, copperfin habitat includes shaded pools and clear streams. These details belong to the invented story rather than to real biological knowledge.");
        string invented = brain.Chat("write an article about copperfin");
        Check(invented.Contains("fins") && invented.Contains("shaded pools"), "The composition mechanism also works for a newly taught topic");

        byte[] bytes = File.ReadAllBytes(path);
        Check(BitConverter.ToUInt32(bytes, 0) == 0x31434e44, "Brain files use the compact lossless envelope");
        uint rawSize = BitConverter.ToUInt32(bytes, 4);
        Check(bytes.Length < rawSize * 0.8, $"Compact storage: {bytes.Length:N0} bytes versus {rawSize:N0} raw bytes");
        bytes[^1] ^= 0x20;
        string corrupt = Path.Combine(scratch, "corrupt.brain");
        File.WriteAllBytes(corrupt, bytes);
        using var invalid = new NeuralEngine();
        Check(!invalid.LoadState(corrupt), "Corrupt compact payloads fail validation instead of becoming a damaged graph");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
