using System;
using System.Collections.Generic;

public class EnglishCurriculumTrainer
{
    private NeuralEngine _brain;
    private HashSet<char> _seenCharacters = new HashSet<char>();

    public EnglishCurriculumTrainer(NeuralEngine brain)
    {
        _brain = brain;
    }

    /// <summary>
    /// Trains the AGI on a structured multi-sentence corpus using raw character streams.
    /// Triggers dynamic neurogenesis when novel characters or structural patterns are encountered.
    /// </summary>
    public void TrainOnCurriculum(string[] corpus, int epochs)
    {
        Console.WriteLine($"\n[Curriculum Trainer] Starting training across {corpus.Length} sentences for {epochs} epochs...");

        for (int epoch = 1; epoch <= epochs; epoch++)
        {
            Console.WriteLine($"\n--- Epoch {epoch} of {epochs} ---");

            foreach (string sentence in corpus)
            {
                foreach (char c in sentence)
                {
                    bool isNovel = !_seenCharacters.Contains(c);
                    if (isNovel)
                    {
                        _seenCharacters.Add(c);

                        // When a truly novel structural pattern/character appears,
                        // trigger physical brain expansion (Runtime Neurogenesis)
                        int newId = _brain.SpawnNeuron();
                        Console.WriteLine($"   [Neurogenesis] Spawned new neuron #{newId} due to novel character: '{c}'");
                    }

                    // Normalize ASCII character into an electrical signal (0.0 to 1.0)
                    float electricalSignal = (float)c / 255.0f;

                    // Step the C++ engine core
                    _brain.Step(signal: electricalSignal, goodAction: false, badAction: false, novelState: isNovel);
                }
            }

            // After each epoch, let the brain "dream" in RAM to consolidate sequence patterns via Hebbian plasticity
            Console.WriteLine("[Dreaming] Running offline simulation consolidation cycle...");
            _brain.Simulate(signal: 0.5f, goodAction: true, badAction: false, iterations: 200);
        }

        Console.WriteLine("\n[Curriculum Trainer] Training complete! Language core successfully adapted.");
    }
}