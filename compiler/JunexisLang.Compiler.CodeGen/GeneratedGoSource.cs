namespace JunexisLang.Compiler.CodeGen;

internal static class GeneratedGoSource
{
    internal static string Render(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var leadingSpaces = 0;
            while (leadingSpaces < line.Length && line[leadingSpaces] == ' ')
                leadingSpaces++;

            var tabs = leadingSpaces / 4;
            if (tabs > 0)
                lines[index] = new string('\t', tabs) + line[(tabs * 4)..];
        }

        return string.Join("\n", lines) + "\n";
    }
}
