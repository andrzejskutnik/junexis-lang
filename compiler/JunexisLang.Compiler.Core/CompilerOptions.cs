namespace JunexisLang.Compiler.Core;

/// <summary>Opcje dla operacji kompilacji (parse + semantic + codegen).</summary>
/// <param name="InputFile">Ścieżka do pliku .jnx.</param>
/// <param name="OutDir">Katalog wyjściowy dla wygenerowanych plików.</param>
/// <param name="Targets">Lista platform docelowych; null = wszystkie.</param>
public sealed record CompileOptions(
    string InputFile,
    string OutDir = "out",
    IReadOnlyList<string>? Targets = null);

/// <summary>Opcje dla operacji sprawdzenia (parse + semantic, bez codegen).</summary>
/// <param name="InputFile">Ścieżka do pliku .jnx.</param>
public sealed record CheckOptions(string InputFile);

/// <summary>Opcje dla samego parsowania (lexer + parser + AST, bez walidacji semantycznej).</summary>
/// <param name="InputFile">Ścieżka do pliku .jnx.</param>
public sealed record ParseOptions(string InputFile);
