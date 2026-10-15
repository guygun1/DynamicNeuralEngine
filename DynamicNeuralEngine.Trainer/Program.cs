using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== [C# AGI Trainer] Unified Brain Persistence Pipeline ===\n");

        string brainFilePath = "agi_mind.brain";

        // ==========================================================
        // PHASE 1: Train & Save Whole Brain State to a Single File
        // ==========================================================
        if (!File.Exists(brainFilePath))
        {
            Console.WriteLine("--- PHASE 1: Training Fresh Brain & Saving State ---");
            using (var trainerBrain = new NeuralEngine(10))
            {
                string[] curriculum = {
                    "hello? hello there friend. ",
                    "how are you? i am doing well thanks. ",
                    "what is your name? my name is agi core. "
                };

                foreach (var dialogue in curriculum)
                {
                    Console.WriteLine($"Teaching: \"{dialogue}\"");
                    foreach (char c in dialogue)
                    {
                        trainerBrain.StepChar(c, novelState: false);
                    }
                }

                Console.WriteLine("\n[Dreaming] Consolidating pathways...");
                trainerBrain.Simulate(signal: 0.5f, goodAction: true, badAction: false, iterations: 400);

                // Save entire unified brain state to one single file
                if (trainerBrain.SaveState(brainFilePath))
                {
                    Console.WriteLine($"[Success] Saved complete brain state to '{brainFilePath}'!\n");
                }
                else
                {
                    Console.WriteLine("[Error] Failed to save brain state.");
                    return;
                }
            }
        }
        else
        {
            Console.WriteLine($"[Found Existing Brain] Loading state from '{brainFilePath}' directly...\n");
        }

        // ==========================================================
        // PHASE 2: Load Unified Brain & Start Chat Terminal
        // ==========================================================
        Console.WriteLine("--- PHASE 2: Loading State & Interactive Chat ---");
        using (var chatBrain = new NeuralEngine(10))
        {
            if (chatBrain.LoadState(brainFilePath))
            {
                Console.WriteLine($"[Success] Successfully loaded '{brainFilePath}' into active memory!\n");
            }
            else
            {
                Console.WriteLine("[Error] Failed to load brain file.");
                return;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine(" AGI Chat Terminal Active. Type your prompt below.");
            Console.WriteLine(" (e.g., 'hello?', 'how are you?')");
            Console.WriteLine(" Type 'exit' to quit.");
            Console.WriteLine("==================================================\n");

            while (true)
            {
                Console.Write("User > ");
                string input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "exit")
                {
                    break;
                }

                string response = chatBrain.Generate(input, maxChars: 50);

                Console.WriteLine($"AGI  > {response}");
                Console.WriteLine();
            }
        }
    }
}