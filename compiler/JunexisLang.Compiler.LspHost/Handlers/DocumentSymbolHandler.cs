using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>Dostarcza strukturę dokumentu dla panelu Outline w VS Code
/// (textDocument/documentSymbol).</summary>
internal sealed class DocumentSymbolHandler(DocumentStore store) : DocumentSymbolHandlerBase
{
    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability _c, ClientCapabilities _caps) =>
        new() { DocumentSelector = TextDocumentSelector.ForLanguage("junexis") };

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken _)
    {
        var (_, ast, _) = store.Get(request.TextDocument.Uri.ToString());
        if (ast is null)
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);

        var symbols = new List<SymbolInformationOrDocumentSymbol>();

        foreach (var decl in ast.Declarations)
        {
            var (name, kind) = ClassifyDecl(decl);
            if (name is null) continue;

            // AST: 1-based → LSP: 0-based
            var line = Math.Max(0, decl.Line   - 1);
            var col  = Math.Max(0, decl.Column - 1);
            var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(line, col),
                new Position(line, col));

            var sym = new DocumentSymbol
            {
                Name           = name,
                Kind           = kind,
                Range          = range,
                SelectionRange = range,
                Children       = new Container<DocumentSymbol>(ChildSymbols(decl)),
            };

            symbols.Add(new SymbolInformationOrDocumentSymbol(sym));
        }

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    private static (string? Name, SymbolKind Kind) ClassifyDecl(AstNode n) => n switch
    {
        StructDeclarationNode         s  => (s.Name,  SymbolKind.Struct),
        EnumDeclarationNode           e  => (e.Name,  SymbolKind.Enum),
        FnDeclarationNode             f  => (f.Name,  SymbolKind.Function),
        TaskDeclarationNode           t  => (t.Name,  SymbolKind.Event),
        PipelineDeclarationNode       p  => (p.Name,  SymbolKind.Module),
        DeviceDeclarationNode         dv => (dv.Name, SymbolKind.Interface),
        SupervisorDeclarationNode     sv => (sv.Name, SymbolKind.Class),
        CircuitBreakerDeclarationNode cb => (cb.Name, SymbolKind.Class),
        ExternTaskNode                et => (et.Name, SymbolKind.Function),
        ExternTypeNode               etp => (etp.Name,SymbolKind.Class),
        ExternContractNode            ec => (ec.Name, SymbolKind.Interface),
        StorageDeclarationNode         st => (st.Name, SymbolKind.Module),
        _                                => (null,    SymbolKind.Null),
    };

    private static IEnumerable<DocumentSymbol> ChildSymbols(AstNode n) => n switch
    {
        StructDeclarationNode s => s.Fields.Select(field =>
            CreateChild(field.Name, SymbolKind.Field, field.Line, field.Column)),

        EnumDeclarationNode e => e.Variants.Select(variant =>
            CreateChild(variant.Name, SymbolKind.EnumMember, variant.Line, variant.Column)),

        FnDeclarationNode f => f.Params.Select(param =>
            CreateChild(param.Name, SymbolKind.Variable, param.Line, param.Column)),

        TaskDeclarationNode t => t.Params.Select(param =>
            CreateChild(param.Name, SymbolKind.Variable, param.Line, param.Column)),

        ExternContractNode ec => ec.Functions.Select(function =>
            CreateChild(function.Name, SymbolKind.Method, function.Line, function.Column)),

        StorageDeclarationNode storage => storage.Entities.Select(entity =>
                CreateChild(entity.Name, SymbolKind.Struct, entity.Line, entity.Column))
            .Concat(storage.Queries.Select(query =>
                CreateChild(query.Name, SymbolKind.Method, query.Line, query.Column)))
            .Concat(storage.Mutations.Select(mutation =>
                CreateChild(mutation.Name, SymbolKind.Method, mutation.Line, mutation.Column))),

        _ => []
    };

    private static DocumentSymbol CreateChild(string name, SymbolKind kind, int astLine, int astColumn)
    {
        var line = Math.Max(0, astLine - 1);
        var col = Math.Max(0, astColumn - 1);
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            new Position(line, col),
            new Position(line, col));

        return new DocumentSymbol
        {
            Name = name,
            Kind = kind,
            Range = range,
            SelectionRange = range,
        };
    }
}
