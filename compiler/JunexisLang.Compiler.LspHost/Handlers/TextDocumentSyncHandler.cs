using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using JunexisLang.Compiler.Core;
using CompilerDiagnostic = JunexisLang.Compiler.Core.Diagnostic;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>
/// Obsługuje didOpen / didChange / didClose i natychmiast publikuje diagnostyki z kompilatora.
/// </summary>
internal sealed class TextDocumentSyncHandler(
    ILanguageServerFacade server,
    DocumentStore store)
    : TextDocumentSyncHandlerBase
{
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new(uri, "junexis");

    // ── Powiadomienia ─────────────────────────────────────────────────────

    public override async Task<Unit> Handle(DidOpenTextDocumentParams r, CancellationToken _)
    {
        await store.UpdateAsync(r.TextDocument.Uri.ToString(), r.TextDocument.Text);
        Publish(r.TextDocument.Uri.ToString());
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams r, CancellationToken _)
    {
        var text = r.ContentChanges.Last().Text;
        await store.UpdateAsync(r.TextDocument.Uri.ToString(), text);
        Publish(r.TextDocument.Uri.ToString());
        return Unit.Value;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams r, CancellationToken _) =>
        Task.FromResult(Unit.Value);

    public override Task<Unit> Handle(DidCloseTextDocumentParams r, CancellationToken _)
    {
        store.Remove(r.TextDocument.Uri.ToString());
        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri         = r.TextDocument.Uri,
            Diagnostics = new Container<LspDiagnostic>()
        });
        return Task.FromResult(Unit.Value);
    }

    // ── Rejestracja ───────────────────────────────────────────────────────

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability _c,
        ClientCapabilities _caps) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("junexis"),
            Change           = TextDocumentSyncKind.Full,
            Save             = new SaveOptions { IncludeText = false }
        };

    // ── Publikacja diagnostyk ─────────────────────────────────────────────

    private void Publish(string uri)
    {
        var (_, _, diagnostics) = store.Get(uri);

        var lsp = diagnostics.Select(d => new LspDiagnostic
        {
            Range    = ToRange(d.Line, d.Column),
            Severity = d.Level switch
            {
                DiagnosticLevel.Error   => DiagnosticSeverity.Error,
                DiagnosticLevel.Warning => DiagnosticSeverity.Warning,
                _                       => DiagnosticSeverity.Information
            },
            Code    = new DiagnosticCode(d.Code ?? ""),
            Source  = "junexis",
            Message = d.Message
        });

        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri         = DocumentUri.From(uri),
            Diagnostics = new Container<LspDiagnostic>(lsp)
        });
    }

    // ── Pomocnicze ────────────────────────────────────────────────────────

    private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range ToRange(int line, int col)
    {
        var pos = new Position(Math.Max(0, line - 1), Math.Max(0, col - 1));
        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            pos, pos with { Character = pos.Character + 1 });
    }
}
