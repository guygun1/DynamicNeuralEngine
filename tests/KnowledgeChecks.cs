using System;
using System.Globalization;
using System.IO;
using System.Linq;

internal static class KnowledgeChecks
{
    public static void Run(EnglishCurriculum curriculum, string scratch)
    {
        using var brain = new NeuralEngine();
        new EnglishCurriculumTrainer(brain).Train(curriculum);
        var cases = new (string input, double expected)[] {
            ("1+1", 2), ("what is 19 + 23?", 42), ("17-42", -25), ("12*13",156),
            ("calculate 144 / 12",12), ("2 + 3 * 4",14), ("(2+3)*4",20), ("2^3^2",512),
            ("-2^2",-4), ("(-2)^2",4), ("2^-2",0.25), ("5.5 + 2.25",7.75), (".5*.2",0.1),
            ("17 % 5",2), ("what is two hundred and six plus seventeen?",223),
            ("what is twenty-one times three?",63), ("negative four plus six",2),
            ("what is 20 percent of 150?",30), ("what is three squared?",9),
            ("what is two cubed?",8), ("please tell me two plus two",4), ("what's 3 divided by 4?",0.75),
            ("calculate 1e3 + 2",1002), ("one thousand two hundred plus thirty four",1234)
        };
        foreach (var test in cases) {
            string response = brain.Chat(test.input);
            string first = response.Split('\n')[0];
            int equals = first.LastIndexOf('=');
            bool valid = equals >= 0 && double.TryParse(first[(equals + 1)..].Trim().TrimEnd('.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double result) && Math.Abs(result - test.expected) < 1e-9;
            Check(valid, $"Arithmetic: {test.input} -> {first}");
        }
        static bool Uncertain(string text) => text.Contains("not sure", StringComparison.OrdinalIgnoreCase) || text.Contains("don't know", StringComparison.OrdinalIgnoreCase);
        Check(brain.Chat("1 / 0").Contains("undefined", StringComparison.OrdinalIgnoreCase), "Division by zero is reported as undefined");
        Check(Uncertain(brain.Chat("calculate 2 +")), "Malformed arithmetic uses learned uncertainty");
        Check(Uncertain(brain.Chat("calculate 2 + a zorbiflux")), "Unsupported expressions cannot become a nearby memorized answer");
        Check(Uncertain(brain.Chat("what is the plural of gravity?")),
            "Unsupported relationships between known words abstain");
        Check(Uncertain(brain.Chat("tell me about zorbiflux")),
            "Unfamiliar topics abstain instead of matching generic question words");
        Check(brain.Chat("please explain photosynthesis").Contains("carbon dioxide", StringComparison.OrdinalIgnoreCase),
            "A known topic is recognized through a polite paraphrase");

        string history = brain.Chat("what was world war two?");
        Check(history.Length > 512 && history.Contains("1939") && history.Contains("1945") && history.Contains("\n\n"),
            "History responses retain multiple paragraphs and exceed the former buffer size");
        string science = brain.Chat("what is photosynthesis?");
        Check(science.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 60 && science.EndsWith("energy."),
            "Science lessons deliver their complete extended explanation");
        Check(brain.Chat("what is democracy?").Contains("Civil liberties"), "General human knowledge includes substantive civic explanations");
        Check(brain.Chat("what does absorb mean?").Contains("take it in"), "English vocabulary lessons connect a word to its meaning and usage");
        Check(brain.Chat("the dog chased the cat. who did the chasing?").StartsWith("In that sentence, the dog") &&
            brain.Chat("the cat chased the dog. who did the chasing?").StartsWith("In that sentence, the cat"),
            "Taught English sentence roles distinguish the same nouns in different orders");
        Check(brain.Chat("mira puts a red book on the table. tell me where the book is").Contains("on the table"),
            "A question's focus is not drowned out by a shared story passage");
        Check(brain.Chat("what is photosynthesis?", 80).Length <= 80 && brain.Generate("what is photosynthesis?", 80).Length <= 80,
            "Explicit length limits apply to both chat and generation");
        Check(brain.Chat("what is photosynthesis?", 0) == "", "A zero length request emits no text");

        string path = Path.Combine(scratch, "knowledge.brain");
        Check(brain.SaveState(path), "Formatted knowledge and curriculum stamp save together");
        using var restored = new NeuralEngine();
        Check(restored.LoadState(path) && restored.Chat("what was world war two?") == history &&
            restored.CurriculumFingerprint == curriculum.Fingerprint, "Long responses and curriculum stamp survive restart in one brain file");
        Check(EnglishCurriculum.Load(Path.Combine(AppContext.BaseDirectory, "TeachingMaterials"), includeBootstrap: true).Fingerprint == curriculum.Fingerprint,
            "Bootstrap lessons do not trigger needless curriculum updates on the next run");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
