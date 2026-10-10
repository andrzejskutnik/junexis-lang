namespace JunexisLang.Compiler.Core;

public enum DiagnosticLevel { Error, Warning, Info }

/// <summary>Jeden wpis diagnostyczny emitowany przez kompilator.</summary>
/// <param name="Code">Kod z diagnostics.csv, np. E0001. Pusty string = wewnętrzny.</param>
/// <param name="Level">Poziom: Error / Warning / Info.</param>
/// <param name="Category">Kategoria logiczna, np. Communication.</param>
/// <param name="Message">Gotowy komunikat (z podstawionymi placeholderami).</param>
/// <param name="File">Ścieżka pliku źródłowego (null = globalny).</param>
/// <param name="Line">Numer wiersza 1-based (0 = nieznany).</param>
/// <param name="Column">Numer kolumny 1-based (0 = nieznana).</param>
public sealed record Diagnostic(
    string Code,
    DiagnosticLevel Level,
    string Category,
    string Message,
    string? File = null,
    int Line = 0,
    int Column = 0);
