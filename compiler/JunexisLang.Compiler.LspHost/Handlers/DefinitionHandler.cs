using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using JunexisLang.Compiler.LspHost.Analysis;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>Nawiguje do miejsca deklaracji symbolu (F12 / Go to Definition).</summary>
internal sealed class DefinitionHandler(DocumentStore store) : DefinitionHandlerBase
{
    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability _c, ClientCapabilities _caps) =>
        new() { DocumentSelector = TextDocumentSelector.ForLanguage("junexis") };

    public override Task<LocationOrLocationLinks?> Handle(
        DefinitionParams request, CancellationToken _)
    {
        var (source, ast, _) = store.Get(request.TextDocument.Uri.ToString());
        if (source is null || ast is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var word = WordAtPosition.Get(
            source, request.Position.Line, request.Position.Character);
        if (word is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var local = SymbolIndex.Build(ast).Find(word.Value.Word);
        var storageQuery = local is null
            ? store.FindStorageQuerySymbol(request.TextDocument.Uri.ToString(), word.Value.Word)
            : null;
        var candidates = local is not null
            ? [local]
            : storageQuery is not null
                ? [storageQuery.Definition]
            : store.ParsedSnapshot()
                .Where(document => document.Uri != request.TextDocument.Uri.ToString())
                .Select(document => SymbolIndex.Build(document.Ast).Find(word.Value.Word))
                .Where(node => node is not null)
                .Cast<JunexisLang.Compiler.Core.Ast.AstNode>()
                .ToArray();
        if (candidates.Length != 1)
            return Task.FromResult<LocationOrLocationLinks?>(null);
        var decl = candidates[0];

        // AST: 1-based → LSP: 0-based
        var pos = new Position(
            Math.Max(0, decl.Line   - 1),
            Math.Max(0, decl.Column - 1));

        var loc = new Location
        {
            Uri   = DocumentUri.From(new Uri(Path.GetFullPath(decl.File)).AbsoluteUri),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(pos, pos)
        };

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(new LocationOrLocationLink(loc)));
    }
}
