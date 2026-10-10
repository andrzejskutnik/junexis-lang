using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.IR;

/// <summary>Kontrolowany wynik transformacji AST do Structural IR.</summary>
public sealed record IRTransformResult(
    bool Success,
    IRDocument? Document,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>Tworzy poprawny wynik transformacji.</summary>
    public static IRTransformResult Ok(IRDocument document) => new(true, document, []);

    /// <summary>Tworzy niepoprawny wynik bez częściowego dokumentu IR.</summary>
    public static IRTransformResult Fail(Diagnostic diagnostic) => new(false, null, [diagnostic]);
}

internal sealed class IRTransformException : Exception
{
    internal IRTransformException(string message, Core.Ast.AstNode node)
        : base(message) => Node = node;

    internal Core.Ast.AstNode Node { get; }
}
