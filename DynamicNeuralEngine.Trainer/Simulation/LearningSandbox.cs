using System;

public sealed record SimulationReport(int Episodes, int BeforeSuccesses, int RandomSuccesses, int AfterSuccesses, int AddedNeurons);

// A deliberately small, observable environment. The agent is not told which
// action wins: it changes its state-action values from actual transition rewards.
public static class LearningSandbox
{
    private static readonly string[] Actions = { "move left", "move right" };
    private const int Goal = 4;
    private const int StepLimit = 16;
    private static string State(int position) => $"sandbox corridor position {position}";

    public static SimulationReport Run(NeuralEngine brain, int episodes = 300, int seed = 1729)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(episodes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(episodes, 10000);
        int nodesBefore = brain.NeuronCount;
        int before = Evaluate(brain);
        int randomScore = Evaluate(brain, random: new Random(seed + 1));
        // These are observed premises of the toy world, not answers to its later
        // transitive/inherited-property questions.
        foreach (string observation in new[] {
            "A sandbox rover is a sandbox machine.",
            "A sandbox machine is a sandbox object.",
            "A sandbox machine has a sandbox battery.",
            "The sandbox goal is in the sandbox final room.",
            "The sandbox final room is in the sandbox building."
        }) brain.TrainText(observation);

        var random = new Random(seed);
        int transitions = 0;
        for (int episode = 0; episode < episodes; episode++)
        {
            int position = episode % Goal;
            for (int step = 0; step < StepLimit && position != Goal; step++)
            {
                int action;
                float left = brain.ActionValue(State(position), Actions[0]);
                float right = brain.ActionValue(State(position), Actions[1]);
                if (random.NextDouble() < 0.25 || left == right) action = random.Next(2);
                else action = right > left ? 1 : 0;
                int next = Math.Clamp(position + (action == 0 ? -1 : 1), 0, Goal);
                float reward = next == Goal ? 1.0f : next == position ? -0.1f : -0.02f;
                float future = next == Goal ? 0 : Math.Max(brain.ActionValue(State(next), Actions[0]), brain.ActionValue(State(next), Actions[1]));
                brain.LearnActionValue(State(position), Actions[action], reward + 0.9f * future);
                transitions++;
                position = next;
            }
        }
        int after = Evaluate(brain);
        Console.WriteLine($"[Sandbox] {episodes} episodes; {transitions} observed transitions. The task is to reach corridor position {Goal} within {StepLimit} moves.");
        Console.WriteLine($"[Sandbox] Greedy success before: {before}/100; random-action baseline: {randomScore}/100; learned greedy success: {after}/100.");
        Console.WriteLine($"[Sandbox] Added representations: {brain.NeuronCount - nodesBefore}. State/action values are stored as neural connections in this brain.");
        Console.WriteLine("[Sandbox deduction] " + brain.Generate("is a sandbox rover a sandbox object?"));
        Console.WriteLine("[Sandbox deduction] " + brain.Generate("does a sandbox rover have a sandbox battery?"));
        Console.WriteLine("[Sandbox] These results concern this toy world; they do not establish general intelligence or autonomous real-world competence.");
        return new SimulationReport(episodes, before, randomScore, after, brain.NeuronCount - nodesBefore);
    }

    public static int Evaluate(NeuralEngine brain, Random? random = null)
    {
        int successes = 0;
        for (int trial = 0; trial < 100; trial++)
        {
            int position = trial % Goal;
            for (int step = 0; step < StepLimit && position != Goal; step++)
            {
                int action = random?.Next(2) ?? (brain.ActionValue(State(position), Actions[1]) > brain.ActionValue(State(position), Actions[0]) ? 1 : 0);
                position = Math.Clamp(position + (action == 0 ? -1 : 1), 0, Goal);
            }
            if (position == Goal) successes++;
        }
        return successes;
    }
}
