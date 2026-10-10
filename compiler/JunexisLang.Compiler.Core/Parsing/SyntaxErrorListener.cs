using Antlr4.Runtime;

namespace JunexisLang.Compiler.Core.Parsing;

/// <summary>
/// Zbiera błędy syntaktyczne z leksera i parsera ANTLR4 i zamienia je
/// na <see cref="Diagnostic"/> z poprawnym plikiem / linią / kolumną.
/// </summary>
internal sealed class SyntaxErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
    private readonly string _file;
    private readonly List<Diagnostic> _errors = [];

    internal IReadOnlyList<Diagnostic> Errors => _errors;

    internal SyntaxErrorListener(string file) => _file = file;

    // ── Parser errors (IToken) ──────────────────────────────────────
    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
        => Add(line, charPositionInLine, msg);

    // ── Lexer errors (int token type) ──────────────────────────────
    void IAntlrErrorListener<int>.SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
        => Add(line, charPositionInLine, msg);

    private void Add(int line, int col, string msg)
        => _errors.Add(new Diagnostic(
            Code: "P0001",
            Level: DiagnosticLevel.Error,
            Category: "Syntax",
            Message: msg,
            File: _file,
            Line: line,
            Column: col + 1));   // ANTLR4 jest 0-based, Diagnostic 1-based
}
