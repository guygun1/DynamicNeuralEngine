using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public sealed record DialogueLesson(string Prompt, string Response);
public sealed record TeachingUnit(string Name, IReadOnlyList<DialogueLesson> Dialogues, IReadOnlyList<string> Sentences);

public sealed class EnglishCurriculum
{
    public IReadOnlyList<TeachingUnit> Units { get; }
    public int DialogueCount => Units.Sum(unit => unit.Dialogues.Count);
    public int SentenceCount => Units.Sum(unit => unit.Sentences.Count);
    public string Fingerprint { get; }

    private EnglishCurriculum(IReadOnlyList<TeachingUnit> units)
    {
        Units = units;
        var content = new StringBuilder("text-import-schema:2\n");
        foreach (var unit in units.Where(unit => !unit.Name.EndsWith(".bootstrap.txt", StringComparison.OrdinalIgnoreCase)))
        {
            content.AppendLine(unit.Name);
            foreach (var lesson in unit.Dialogues) content.AppendLine(lesson.Prompt).AppendLine(lesson.Response);
            foreach (string sentence in unit.Sentences) content.AppendLine(sentence);
        }
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }

    public static EnglishCurriculum Load(string directory, bool includeBootstrap = false)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Teaching material directory not found: {directory}");

        var units = new List<TeachingUnit>();
        var prompts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Validate the entire pack before modifying a brain. File order is reproducible.
        foreach (string path in Directory.GetFiles(directory, "*.txt").OrderBy(path => path, StringComparer.Ordinal))
        {
            bool bootstrapFile = path.EndsWith(".bootstrap.txt", StringComparison.OrdinalIgnoreCase);
            if (bootstrapFile && !includeBootstrap) continue;
            bool dialogueFile = bootstrapFile || path.EndsWith(".dialogue.txt", StringComparison.OrdinalIgnoreCase);
            bool textFile = path.EndsWith(".sentences.txt", StringComparison.OrdinalIgnoreCase);
            textFile |= path.EndsWith(".facts.txt", StringComparison.OrdinalIgnoreCase);
            if (!dialogueFile && !textFile) continue;

            var dialogues = new List<DialogueLesson>();
            var sentences = new List<string>();
            int lineNumber = 0;
            foreach (string raw in File.ReadLines(path))
            {
                lineNumber++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (dialogueFile)
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidDataException($"{path}:{lineNumber}: expected prompt | response.");
                    foreach (string alias in parts[0].Split('~'))
                    {
                        string prompt = alias.Trim();
                        if (prompt.Length == 0 || !prompts.Add(prompt))
                            throw new InvalidDataException($"{path}:{lineNumber}: empty or duplicate prompt '{prompt}'.");
                        dialogues.Add(new DialogueLesson(prompt, parts[1].Trim().Replace("\\n", "\n")));
                    }
                }
                else
                {
                    sentences.Add(line);
                }
            }
            if (dialogues.Count + sentences.Count == 0)
                throw new InvalidDataException($"Teaching file is empty: {path}");
            units.Add(new TeachingUnit(Path.GetFileName(path), dialogues, sentences));
        }
        if (units.Count == 0)
            throw new InvalidDataException($"No .dialogue.txt or .sentences.txt teaching files in {directory}.");
        return new EnglishCurriculum(units);
    }
}
