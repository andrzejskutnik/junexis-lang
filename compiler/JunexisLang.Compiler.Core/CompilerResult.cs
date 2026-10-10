using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Core;

/// <summary>Jeden wygenerowany plik wyjściowy.</summary>
/// <param name="Platform">Platforma docelowa, np. "embedded", "server".</param>
/// <param name="FilePath">Ścieżka pliku na dysku (relatywna do out-dir).</param>
/// <param name="Content">Zawartość pliku jako string.</param>
public sealed record GeneratedFile(string Platform, string FilePath, string Content);

/// <summary>Wynik samodzielnego etapu parsowania, bez semantyki i codegen.</summary>
public sealed record ParseResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    ProgramNode? Ast)
{
    public static ParseResult Ok(ProgramNode ast) => new(true, [], ast);
    public static ParseResult Fail(IReadOnlyList<Diagnostic> diagnostics) => new(false, diagnostics, null);
}

/// <summary>Wynik operacji kompilatora (check lub compile).</summary>
public sealed record CompilerResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<GeneratedFile> GeneratedFiles,
    ProgramNode? Ast = null)
{
    public static CompilerResult Ok(IReadOnlyList<GeneratedFile> files, ProgramNode? ast = null, IReadOnlyList<Diagnostic>? diagnostics = null) =>
        new(true, diagnostics ?? [], files, ast);

    public static CompilerResult CheckOk(ProgramNode ast, IReadOnlyList<Diagnostic>? diagnostics = null) =>
        new(true, diagnostics ?? [], [], ast);

    public static CompilerResult Fail(IReadOnlyList<Diagnostic> diagnostics) =>
        new(false, diagnostics, []);
}
