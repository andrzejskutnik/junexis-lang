using System.Text.Json;
using System.Text.Json.Serialization;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Output;
/// <summary>
/// Provides methods to format and emit compiler diagnostics in both text and JSON formats for the JunexisLang CLI.
/// This class is used by various CLI command handlers to report errors, warnings, and informational messages.
/// </summary>
internal static class DiagnosticFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Emits diagnostics in text format to stdout/stderr.
    /// Info-level diagnostics are omitted in text format (visible only in JSON).
    /// </summary>
    internal static void Write(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (var d in diagnostics.Where(d => d.Level != DiagnosticLevel.Info))
        {
            var location = d.File is not null ? $"{d.File}:{d.Line}:{d.Column}: " : "";
            var prefix = d.Level switch
            {
                DiagnosticLevel.Error   => "error",
                DiagnosticLevel.Warning => "warning",
                DiagnosticLevel.Info    => "info",
                _                       => "note"
            };
            var code = string.IsNullOrWhiteSpace(d.Code) ? "" : $"[{d.Code}] ";
            var target = d.Level == DiagnosticLevel.Error ? Console.Error : Console.Out;
            target.WriteLine($"{location}{prefix}: {code}{d.Message}");
        }
    }

    /// <summary>
    /// Emits a JSON envelope v1.0 to stdout compliant with CLI_SCHEMA_STANDARD v1.0.
    /// Exactly one object per invocation — never a raw array.
    /// </summary>
    /// <param name="command">The name of the command, e.g., <c>"check"</c> or <c>"build"</c>.</param>
    /// <param name="success"><c>true</c> if there are no Error diagnostics.</param>
    /// <param name="durationMs">The execution time in milliseconds.</param>
    /// <param name="inputFile">The path to the input file.</param>
    /// <param name="diagnostics">The list of compiler diagnostics.</param>
    internal static void WriteJson(
        string command,
        bool success,
        long durationMs,
        string inputFile,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        Console.WriteLine(BuildJson(command, success, durationMs, inputFile, diagnostics));
    }

    /// <summary>
    /// Builds a JSON envelope as a string — used by tests without redirecting the console.
    /// </summary>
    internal static string BuildJson(
        string command,
        bool success,
        long durationMs,
        string inputFile,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        var dtos = diagnostics.Select(CliDiagnosticDto.From).ToList();
        var envelope = new CliOutputEnvelope(
            SchemaVersion: CliOutputEnvelope.CurrentVersion,
            Command:       command,
            Success:       success,
            DurationMs:    durationMs,
            InputFile:     inputFile,
            Diagnostics:   dtos);

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }
}
