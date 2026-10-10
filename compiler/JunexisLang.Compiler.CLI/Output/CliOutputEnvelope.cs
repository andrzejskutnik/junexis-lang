using System.Text.Json.Serialization;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Output;

/// <summary>
/// Root envelope JSON output CLI zgodny ze schema v1.0.
/// Każde wywołanie CLI z flagą <c>--json</c> emituje dokładnie jeden obiekt tego typu na stdout.
/// </summary>
/// <param name="SchemaVersion">Wersja schema: zawsze <c>"1.0"</c> dla bieżącego CLI.</param>
/// <param name="Command">Komenda, na przykład <c>"check"</c> lub <c>"build"</c>.</param>
/// <param name="Success">
/// <c>true</c> gdy brak diagnostyk na poziomie Error.
/// Może być <c>true</c> przy obecnych Warning/Info.
/// </param>
/// <param name="DurationMs">Czas wykonania w milisekundach.</param>
/// <param name="InputFile">Ścieżka pliku wejściowego przekazana do CLI.</param>
/// <param name="Diagnostics">Lista diagnostyk (może być pusta przy sukcesie).</param>
internal sealed record CliOutputEnvelope(
    [property: JsonPropertyName("schemaVersion")] string                     SchemaVersion,
    [property: JsonPropertyName("command")]       string                     Command,
    [property: JsonPropertyName("success")]       bool                       Success,
    [property: JsonPropertyName("durationMs")]    long                       DurationMs,
    [property: JsonPropertyName("inputFile")]     string                     InputFile,
    [property: JsonPropertyName("diagnostics")]   IReadOnlyList<CliDiagnosticDto> Diagnostics)
{
    /// <summary>Wersja schema emitowana przez ten build CLI.</summary>
    public const string CurrentVersion = "1.0";
}

/// <summary>
/// DTO pojedynczej diagnostyki — camelCase property names zgodne ze schema v1.0.
/// Mapuje wewnętrzny <see cref="Diagnostic"/> z Core na zewnętrzny kontrakt JSON.
/// </summary>
/// <param name="Code">Kod diagnostyczny (np. <c>"E0001"</c>). Pusty string dla wewnętrznych.</param>
/// <param name="Level">Poziom: <c>"error"</c>, <c>"warning"</c> lub <c>"info"</c>.</param>
/// <param name="Category">Kategoria logiczna (np. <c>"Communication"</c>, <c>"Type"</c>).</param>
/// <param name="Message">Gotowy komunikat użytkownika.</param>
/// <param name="File">Ścieżka pliku źródłowego lub <c>null</c> dla diagnostyk globalnych.</param>
/// <param name="Line">Numer wiersza 1-based (<c>0</c> = nieznany).</param>
/// <param name="Column">Numer kolumny 1-based (<c>0</c> = nieznana).</param>
internal sealed record CliDiagnosticDto(
    [property: JsonPropertyName("code")]     string  Code,
    [property: JsonPropertyName("level")]    string  Level,
    [property: JsonPropertyName("category")] string  Category,
    [property: JsonPropertyName("message")]  string  Message,
    [property: JsonPropertyName("file")]     string? File,
    [property: JsonPropertyName("line")]     int     Line,
    [property: JsonPropertyName("column")]   int     Column)
{
    /// <summary>Tworzy DTO z wewnętrznego <see cref="Diagnostic"/> Core.</summary>
    /// <param name="d">Diagnostyka z kompilatora.</param>
    /// <returns>DTO gotowe do serializacji JSON.</returns>
    internal static CliDiagnosticDto From(Diagnostic d) => new(
        Code:     d.Code,
        Level:    d.Level switch
        {
            DiagnosticLevel.Error   => "error",
            DiagnosticLevel.Warning => "warning",
            DiagnosticLevel.Info    => "info",
            _                       => "info"
        },
        Category: d.Category,
        Message:  d.Message,
        File:     d.File,
        Line:     d.Line,
        Column:   d.Column);
}
