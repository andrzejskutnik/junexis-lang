using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>Udostępnia bezpieczne refaktoryzacje prowadzące kod w stronę składni narrative.</summary>
internal sealed class CodeActionHandler(DocumentStore store) : CodeActionHandlerBase
{
    public override Task<CodeAction> Handle(CodeAction request, CancellationToken _) =>
        Task.FromResult(request);

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability _capability,
        ClientCapabilities _clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("junexis"),
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.RefactorRewrite),
        };

    public override Task<CommandOrCodeActionContainer?> Handle(
        CodeActionParams request,
        CancellationToken _)
    {
        var uri = request.TextDocument.Uri;
        var (source, _, _) = store.Get(uri.ToString());
        if (source is null)
            return Task.FromResult<CommandOrCodeActionContainer?>(null);

        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var firstLine = Math.Clamp(request.Range.Start.Line, 0, Math.Max(0, lines.Length - 1));
        var lastLine = Math.Clamp(request.Range.End.Line, firstLine, Math.Max(firstLine, lines.Length - 1));
        var actions = new List<CommandOrCodeAction>();
        AddNarrativeAction(actions, uri, lines, firstLine, lastLine, "&&", "and");
        AddNarrativeAction(actions, uri, lines, firstLine, lastLine, "||", "or");

        return Task.FromResult<CommandOrCodeActionContainer?>(
            actions.Count == 0 ? null : new CommandOrCodeActionContainer(actions));
    }

    private static void AddNarrativeAction(
        List<CommandOrCodeAction> actions,
        DocumentUri uri,
        IReadOnlyList<string> lines,
        int firstLine,
        int lastLine,
        string symbolicOperator,
        string narrativeOperator)
    {
        var edits = new List<TextEdit>();
        for (var lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
        {
            foreach (var column in FindOperators(lines[lineIndex], symbolicOperator))
            {
                edits.Add(new TextEdit
                {
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                        new Position(lineIndex, column),
                        new Position(lineIndex, column + symbolicOperator.Length)),
                    NewText = narrativeOperator,
                });
            }
        }
        if (edits.Count == 0)
            return;

        actions.Add(new CodeAction
        {
            Title = $"Use narrative '{narrativeOperator}' operator",
            Kind = CodeActionKind.RefactorRewrite,
            IsPreferred = true,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [uri] = edits,
                },
            },
        });
    }

    private static IEnumerable<int> FindOperators(string line, string value)
    {
        var inString = false;
        var escaped = false;
        for (var index = 0; index <= line.Length - value.Length; index++)
        {
            var character = line[index];
            if (!inString && character == '/' && index + 1 < line.Length && line[index + 1] == '/')
                yield break;
            if (character == '"' && !escaped)
                inString = !inString;
            if (!inString && line.AsSpan(index).StartsWith(value, StringComparison.Ordinal))
            {
                yield return index;
                index += value.Length - 1;
                escaped = false;
                continue;
            }
            escaped = inString && character == '\\' && !escaped;
            if (character != '\\')
                escaped = false;
        }
    }
}
