using System.IO;
using System.Text;

internal static class LegacyBrainFixtures
{
    // Independent minimal files in the published old schemas, not produced by
    // today's serializer. This catches accidental backward-format breakage.
    public static void Write(string path, int version)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((uint)(0x41474930 + version));
        writer.Write(2u);
        Text(writer, "."); Text(writer, "quartz");
        writer.Write(-1); writer.Write(0.2f); writer.Write(0.0f); writer.Write(4);
        writer.Write(0u); // Prompt starter table.
        writer.Write(2u);
        for (int id = 0; id < 2; id++) {
            writer.Write(id); writer.Write(0.1f); writer.Write(0.0f); writer.Write(0u);
        }
        if (version >= 3) {
            writer.Write(1u); // One remembered dialogue.
            writer.Write(1u); writer.Write(1);
            writer.Write(2u); writer.Write(1); writer.Write(0);
            if (version >= 4) Text(writer, "Quartz.");
        }
        if (version >= 4) Text(writer, "legacy-stamp");
    }

    private static void Text(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((uint)bytes.Length); writer.Write(bytes);
    }
}
