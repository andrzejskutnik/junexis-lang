namespace JunexisLang.Compiler.CodeGen;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>Interfejs generatora kodu dla jednej platformy docelowej.</summary>
public interface ICodeGenerator
{
    /// <summary>Platforma: "embedded", "server" lub "report".</summary>
    string Platform { get; }

    /// <summary>
    /// Generuje pliki na podstawie IR.
    /// Zwraca pustą listę jeśli żaden task nie należy do tej platformy.
    /// </summary>
    IReadOnlyList<GeneratedFile> Generate(IRDocument ir, CodeGenOptions options);
}
