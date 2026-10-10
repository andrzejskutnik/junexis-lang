using Antlr4.Runtime;
using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.Core.Platform;
using JunexisLang.Compiler.Core.Parsing;
using JunexisLang.Compiler.Core.Semantics;
using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.Core;

/// <summary>
/// Główny punkt wejścia do kompilatora JunexisLang.
/// Etap 1: ANTLR4 parse → błędy syntaktyczne jako <see cref="Diagnostic"/>.
/// Kolejne etapy (AST → IR → CodeGen) będą podłączane iteracyjnie.
/// </summary>
public sealed class JunexisCompiler
{
    /// <summary>
    /// Parsuje plik do AST bez uruchamiania semantyki, walidacji platform ani codegen.
    /// To jest publiczny kontrakt warstwy parsera.
    /// </summary>
    public async Task<ParseResult> ParseAsync(ParseOptions options)
    {
        if (!File.Exists(options.InputFile))
            return ParseResult.Fail(
            [
                new Diagnostic("E0000", DiagnosticLevel.Error, "General",
                    "File not found", options.InputFile)
            ]);

        var source = await File.ReadAllTextAsync(options.InputFile);
        return ParseSource(source, options.InputFile);
    }

    /// <summary>Parsuje tekst źródłowy do AST bez dalszych etapów kompilatora.</summary>
    public ParseResult ParseSource(string source, string virtualPath = "<memory>")
    {
        var inputStream = CharStreams.fromString(source);

        var lexer = new JunexisLangLexer(inputStream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new JunexisLangParser(tokens);
        var listener = new SyntaxErrorListener(virtualPath);

        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(listener);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(listener);

        var programCtx = parser.program();
        var errors = listener.Errors.ToList();
        if (errors.Count > 0)
            return ParseResult.Fail(errors);

        try
        {
            return ParseResult.Ok(AstBuilder.Build(programCtx, tokens, virtualPath));
        }
        catch (Exception ex)
        {
            return ParseResult.Fail(
            [
                new Diagnostic("P0002", DiagnosticLevel.Error, "Parser",
                    $"AST construction failed: {ex.Message}", virtualPath)
            ]);
        }
    }

    /// <summary>
    /// Parsuje i sprawdza plik <paramref name="options"/>.InputFile.
    /// Nie generuje kodu.
    /// </summary>
    public async Task<CompilerResult> CheckAsync(CheckOptions options)
    {
        if (!File.Exists(options.InputFile))
            return CompilerResult.Fail(
            [
                new Diagnostic("E0000", DiagnosticLevel.Error, "General",
                    "File not found", options.InputFile)
            ]);

        var source = await File.ReadAllTextAsync(options.InputFile);
        return CheckSource(source, options.InputFile);
    }

    /// <summary>
    /// Parsuje i sprawdza podany źródłowy tekst bez odczytu z dysku.
    /// Używane przez LSP — bufory edytora.
    /// </summary>
    public Task<CompilerResult> CheckSourceAsync(string source, string virtualPath = "<lsp>")
        => Task.FromResult(CheckSource(source, virtualPath));

    /// <summary>
    /// Runs platform and file-local semantic validation on an already parsed program.
    /// </summary>
    /// <param name="ast">The parsed Junexis program.</param>
    /// <returns>The compiler result without reparsing the source module.</returns>
    public CompilerResult CheckAst(ProgramNode ast)
        => CheckAst(ast, []);

    /// <summary>Checks an AST using a project-bound module symbol scope.</summary>
    /// <param name="ast">The parsed module.</param>
    /// <param name="visibleSymbols">Symbols visible after resolving imports.</param>
    /// <returns>The semantic and platform validation result.</returns>
    public CompilerResult CheckAst(ProgramNode ast, IEnumerable<ProjectVisibleSymbol> visibleSymbols)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(visibleSymbols);

        var platformDiagnostics = PlatformValidator.ValidateProgram(ast);
        var semanticDiagnostics = SemanticAnalyzer.Analyze(ast, visibleSymbols);
        var diagnostics = platformDiagnostics.Concat(semanticDiagnostics).ToList();
        var hasErrors = diagnostics.Any(diagnostic => diagnostic.Level == DiagnosticLevel.Error);
        return hasErrors
            ? CompilerResult.Fail(diagnostics)
            : CompilerResult.CheckOk(ast, diagnostics);
    }

    /// <summary>
    /// Parsuje, sprawdza i generuje kod dla pliku .jnx.
    /// CodeGen nie jest jeszcze zaimplementowany.
    /// </summary>
    public async Task<CompilerResult> CompileAsync(CompileOptions options)
    {
        if (!File.Exists(options.InputFile))
            return CompilerResult.Fail(
            [
                new Diagnostic("E0000", DiagnosticLevel.Error, "General",
                    "File not found", options.InputFile)
            ]);

        var source = await File.ReadAllTextAsync(options.InputFile);
        var parse = ParseSource(source, options.InputFile);

        if (!parse.Success)
            return CompilerResult.Fail(parse.Diagnostics);

        // Core nie może referencjonować IR ani CodeGen (cykl zależności).
        // Kompletny pipeline kompilacji jest realizowany w CLI:
        //   CheckAsync() → AstToIrTransformer.Transform() → CodeGeneratorFactory.Get() → zapis plików
        // Jeśli potrzebujesz wywołać kompilator programowo, użyj CompileHandler z projektu CLI.
        throw new NotSupportedException(
            "CompileAsync nie jest zaimplementowane w Core. " +
            "Użyj CLI pipeline: CheckAsync → AstToIrTransformer → CodeGeneratorFactory.");
    }

    // ────────────────────────────────────────────────────────────────────
    // Wewnętrzna metoda parsowania — ANTLR4 lexer + parser + ASTBuilder
    // ────────────────────────────────────────────────────────────────────

    private static CompilerResult CheckSource(string source, string file)
    {
        var parse = new JunexisCompiler().ParseSource(source, file);
        if (!parse.Success)
        {
            return CompilerResult.Fail(parse.Diagnostics);
        }

        var ast = parse.Ast!;

        return new JunexisCompiler().CheckAst(ast);
    }
}
