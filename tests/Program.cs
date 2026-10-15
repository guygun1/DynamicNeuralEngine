using System;
using System.IO;
using System.Linq;

internal static class IntegrationChecks
{
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"[PASS] {description}");
    }

    private static int Main(string[] args)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "opencode", "dne-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            if (args.Contains("--web-only")) { WebLearningChecks.Run(scratch).GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--ui-only")) { WebUiChecks.Run(scratch).GetAwaiter().GetResult(); return 0; }
            var curriculum = EnglishCurriculum.Load(Path.Combine(AppContext.BaseDirectory, "TeachingMaterials"));
            Require(curriculum.DialogueCount >= 250 && curriculum.SentenceCount >= 90, "Expanded English pack is available in build output");

            using (var legacyEntryPoint = new NeuralEngine())
            {
                int initialVocabulary = legacyEntryPoint.VocabularyCount;
                new EnglishCurriculumTrainer(legacyEntryPoint).TrainOnCurriculum(new[] { "quartz lantern shimmers." }, 1);
                Require(legacyEntryPoint.VocabularyCount >= initialVocabulary + 3, "Original curriculum API teaches words instead of numeric spaces");
            }

            using var brain = new NeuralEngine();
            int before = brain.VocabularyCount;
            new EnglishCurriculumTrainer(brain).Train(curriculum);
            Require(brain.VocabularyCount > before + 600, "Training grows the native vocabulary and graph");
            string[] prompts = { "hello", "what is a noun?", "what is two plus two?", "what is ice?" };
            string[] replies = prompts.Select(prompt => brain.Generate(prompt, 180)).ToArray();
            Require(replies.All(reply => !string.IsNullOrWhiteSpace(reply)), "Native generation returns responses after training");
            string path = Path.Combine(scratch, "roundtrip.brain");
            Require(brain.SaveState(path), "Trained state saves to a single brain file");
            using var restored = new NeuralEngine();
            Require(restored.LoadState(path), "Trained state reloads through the native ABI");
            Require(restored.NeuronCount == brain.NeuronCount && restored.VocabularyCount == brain.VocabularyCount,
                "Vocabulary and neuron counts survive a save/load round trip");
            Require(prompts.Select(prompt => restored.Generate(prompt, 180)).SequenceEqual(replies),
                "Learned response behavior survives a save/load round trip");

            int loadedVocabulary = restored.VocabularyCount;
            restored.TrainDialogue("where is the zephyrstone?", "the zephyrstone rests near the coppergate.");
            Require(restored.VocabularyCount > loadedVocabulary, "An existing brain accepts additional teaching");
            Require(restored.SaveState(path), "Additional teaching is saved");
            using var extended = new NeuralEngine();
            Require(extended.LoadState(path) && extended.VocabularyCount == restored.VocabularyCount,
                "Additional teaching survives restart");

            string invalid = Path.Combine(scratch, "invalid");
            Directory.CreateDirectory(invalid);
            File.WriteAllText(Path.Combine(invalid, "bad.dialogue.txt"), "question without an answer separator");
            bool rejected = false;
            try { EnglishCurriculum.Load(invalid); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Malformed teaching materials are rejected before training");
            UncertaintyChecks.Run(curriculum, scratch);
            ExperimentChecks.Run(scratch);
            KnowledgeChecks.Run(curriculum, scratch);
            WordNeuronChecks.Run(scratch);
            HostingChecks.Run(scratch).GetAwaiter().GetResult();
            ConversationChecks.Run(curriculum, scratch);
            ReasoningChecks.Run(scratch);
            SkillChecks.Run().GetAwaiter().GetResult();
            CompositionalChecks.Run(curriculum);
            AgentChecks.Run(scratch).GetAwaiter().GetResult();
            WebLearningChecks.Run(scratch).GetAwaiter().GetResult();
            WebUiChecks.Run(scratch).GetAwaiter().GetResult();
            Console.WriteLine("All integration checks passed. These checks validate the pipeline, not linguistic fluency.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
