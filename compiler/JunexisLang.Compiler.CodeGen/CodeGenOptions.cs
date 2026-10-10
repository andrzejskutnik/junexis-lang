namespace JunexisLang.Compiler.CodeGen;

/// <summary>Opcje generatora kodu.</summary>
/// <param name="OutDir">Katalog wyjściowy (ścieżka absolutna).</param>
/// <param name="Targets">Platformy docelowe (null = wszystkie).</param>
public sealed record CodeGenOptions(
    string OutDir,
    IReadOnlyList<string>? Targets = null);
