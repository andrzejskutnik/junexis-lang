namespace JunexisLang.Compiler.CodeGen;

using System.Text.Json;
using System.Text.Json.Serialization;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>Generates the bidirectional Junexis-to-runtime debug source map.</summary>
public static class DebugSourceMapGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Generates mappings for task functions emitted by bootstrap backends.</summary>
    /// <param name="document">Source IR document.</param>
    /// <param name="files">Generated backend files.</param>
    /// <returns>The versioned debug source-map artifact.</returns>
    public static GeneratedFile Generate(IRDocument document, IReadOnlyList<GeneratedFile> files)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(files);
        var mappings = new List<DebugSourceMapEntry>();
        for (var index = 0; index < document.Tasks.Count; index++)
        {
            var task = document.Tasks[index];
            var source = document.SourceMap.FirstOrDefault(entry => entry.Path == $"/Tasks[{index}]")?.Source;
            if (source is null)
                continue;
            AddMapping(mappings, files, task, source, "embedded", $"embedded/{task.Name}.cpp", $"task_{task.Name}(");
            AddMapping(mappings, files, task, source, "server", "server/main.go", $"func {task.Name}(");
            AddMapping(mappings, files, task, source, "report", "report/index.ts", $"export async function {task.Name}(");
        }
        AddStatementMappings(mappings, document, files);

        var contract = new DebugSourceMapContract(
            "1.0",
            "junexis.debug-source-map",
            Path.GetFullPath(document.SourceFile),
            mappings.OrderBy(entry => entry.Generated.File, StringComparer.Ordinal)
                .ThenBy(entry => entry.Generated.StartLine)
                .ThenBy(entry => entry.SymbolId, StringComparer.Ordinal)
                .ToArray());
        return new GeneratedFile("debug", "debug/junexis.source-map.json", JsonSerializer.Serialize(contract, JsonOptions));
    }

    private static void AddMapping(
        ICollection<DebugSourceMapEntry> mappings,
        IReadOnlyList<GeneratedFile> files,
        IRTask task,
        IRSourceSpan source,
        string target,
        string filePath,
        string declaration)
    {
        if (!task.Platforms.Any(platform => platform.Target.Equals(target, StringComparison.OrdinalIgnoreCase)))
            return;
        var file = files.FirstOrDefault(candidate =>
            candidate.FilePath.Replace('\\', '/').Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (file is null)
            return;
        var lines = file.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var lineIndex = Array.FindIndex(lines, line => line.Contains(declaration, StringComparison.Ordinal));
        if (lineIndex < 0)
            return;
        var endIndex = FindBlockEnd(lines, lineIndex);
        mappings.Add(new DebugSourceMapEntry(
            StableSymbolIds.Symbol("task", task.Name),
            "task",
            null,
            new DebugSourcePosition(Path.GetFullPath(source.File), source.Line, source.Column),
            new DebugGeneratedRange(target, filePath, lineIndex + 1, endIndex + 1, 1)));
    }

    private static void AddStatementMappings(
        ICollection<DebugSourceMapEntry> mappings,
        IRDocument document,
        IReadOnlyList<GeneratedFile> files)
    {
        const string marker = "// junexis:source ";
        var sources = document.SourceMap.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (var file in files.Where(file => file.Platform is "embedded" or "server" or "report"))
        {
            var lines = file.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var index = 0; index + 1 < lines.Length; index++)
            {
                var trimmed = lines[index].TrimStart();
                if (!trimmed.StartsWith(marker, StringComparison.Ordinal))
                    continue;
                var irPath = trimmed[marker.Length..].Trim();
                if (!sources.TryGetValue(irPath, out var sourceEntry))
                    continue;
                var taskIndex = ParseTaskIndex(irPath);
                if (taskIndex < 0 || taskIndex >= document.Tasks.Count)
                    continue;
                var generatedLine = index + 2;
                var generatedColumnIndex = Array.FindIndex(lines[index + 1].ToCharArray(), character => !char.IsWhiteSpace(character));
                var generatedColumn = generatedColumnIndex < 0 ? 1 : generatedColumnIndex + 1;
                mappings.Add(new DebugSourceMapEntry(
                    StableSymbolIds.Symbol("task", document.Tasks[taskIndex].Name),
                    "statement",
                    irPath,
                    new DebugSourcePosition(
                        Path.GetFullPath(sourceEntry.Source.File),
                        sourceEntry.Source.Line,
                        sourceEntry.Source.Column),
                    new DebugGeneratedRange(file.Platform, file.FilePath, generatedLine, generatedLine, generatedColumn)));
            }
        }
    }

    private static int ParseTaskIndex(string path)
    {
        const string prefix = "/Tasks[";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return -1;
        var close = path.IndexOf(']', prefix.Length);
        return close > prefix.Length && int.TryParse(path.AsSpan(prefix.Length, close - prefix.Length), out var index)
            ? index
            : -1;
    }

    private static int FindBlockEnd(IReadOnlyList<string> lines, int startIndex)
    {
        var depth = 0;
        var opened = false;
        char? quote = null;
        var escaped = false;
        for (var index = startIndex; index < lines.Count; index++)
        {
            for (var column = 0; column < lines[index].Length; column++)
            {
                var character = lines[index][column];
                if (quote is not null)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (character == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (character == quote)
                        quote = null;
                    continue;
                }
                if (character is '"' or '\'' or '`')
                {
                    quote = character;
                    continue;
                }
                if (character == '/' && column + 1 < lines[index].Length && lines[index][column + 1] == '/')
                    break;
                if (character == '{')
                {
                    depth++;
                    opened = true;
                }
                else if (character == '}' && opened && --depth == 0)
                {
                    return index;
                }
            }
            if (quote is not '`')
                quote = null;
        }
        return startIndex;
    }

    private sealed record DebugSourceMapContract(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("sourceFile")] string SourceFile,
        [property: JsonPropertyName("mappings")] IReadOnlyList<DebugSourceMapEntry> Mappings);

    private sealed record DebugSourceMapEntry(
        [property: JsonPropertyName("symbolId")] string SymbolId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("irPath")] string? IrPath,
        [property: JsonPropertyName("source")] DebugSourcePosition Source,
        [property: JsonPropertyName("generated")] DebugGeneratedRange Generated);

    private sealed record DebugSourcePosition(
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("line")] int Line,
        [property: JsonPropertyName("column")] int Column);

    private sealed record DebugGeneratedRange(
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("startLine")] int StartLine,
        [property: JsonPropertyName("endLine")] int EndLine,
        [property: JsonPropertyName("startColumn")] int StartColumn);
}
