using System;

public class EnglishCurriculumTrainer
{
    private readonly NeuralEngine _brain;

    public EnglishCurriculumTrainer(NeuralEngine brain) => _brain = brain;

    public void Train(EnglishCurriculum curriculum, int epochs = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(epochs, 1);
        int vocabularyBefore = _brain.VocabularyCount;
        Console.WriteLine($"[Curriculum] {curriculum.DialogueCount} dialogue pairs, {curriculum.SentenceCount} sentences, {epochs} epoch(s).");
        for (int epoch = 1; epoch <= epochs; epoch++)
        {
            foreach (var unit in curriculum.Units)
            {
                foreach (var lesson in unit.Dialogues)
                    _brain.TrainDialogue(lesson.Prompt, lesson.Response);
                foreach (string sentence in unit.Sentences)
                    _brain.TrainText(sentence);
                Console.WriteLine($"  [{epoch}/{epochs}] {unit.Name}: {unit.Dialogues.Count} pairs, {unit.Sentences.Count} sentences");
            }
        }
        // Simulation currently decays activations; it does not replay or consolidate lessons.
        _brain.Simulate(0.0f, goodAction: false, badAction: false, iterations: 20);
        _brain.CurriculumFingerprint = curriculum.Fingerprint;
        Console.WriteLine($"[Stats] Vocabulary: {vocabularyBefore} -> {_brain.VocabularyCount}; neurons: {_brain.NeuronCount}");
    }

    // Retain the original public entry point, using the actual token-language API.
    public void TrainOnCurriculum(string[] corpus, int epochs)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentOutOfRangeException.ThrowIfLessThan(epochs, 1);
        for (int epoch = 0; epoch < epochs; epoch++)
            foreach (string sentence in corpus)
                _brain.TrainText(sentence);
    }
}
