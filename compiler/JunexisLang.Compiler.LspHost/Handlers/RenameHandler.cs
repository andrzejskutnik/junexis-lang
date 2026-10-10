using JunexisLang.Compiler.LspHost.Analysis;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>Tworzy atomową zmianę nazwy symbolu we wszystkich otwartych dokumentach Junexis.</summary>
internal sealed class RenameHandler(DocumentStore store) : RenameHandlerBase
{
    private static readonly HashSet<string> ReservedWords =
    [
        "and", "as", "at", "await", "behavior", "bool", "break", "catch", "channel",
        "const", "continue", "do", "else", "emit", "enum", "event", "false", "float",
        "for", "foreach", "from", "if", "import", "in", "int", "interface", "is", "let",
        "match", "module", "not", "null", "on", "or", "parallel", "policy", "return",
        "service", "state", "string", "struct", "task", "throw", "to", "true", "try",
        "type", "until", "use", "var", "when", "while", "with",
    ];

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability _capability,
        ClientCapabilities _clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("junexis"),
            PrepareProvider = false,
        };

    public override Task<WorkspaceEdit?> Handle(
        RenameParams request,
        CancellationToken _)
    {
        var (source, _, _) = store.Get(request.TextDocument.Uri.ToString());
        var word = source is null
            ? null
            : WordAtPosition.Get(source, request.Position.Line, request.Position.Character);
        if (word is null
            || ReservedWords.Contains(word.Value.Word)
            || !IsIdentifier(request.NewName)
            || ReservedWords.Contains(request.NewName))
            return Task.FromResult<WorkspaceEdit?>(null);

        var changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>();
        foreach (var document in store.Snapshot())
        {
            var edits = FindEdits(document.Source, word.Value.Word, request.NewName);
            if (edits.Count > 0)
                changes[DocumentUri.From(document.Uri)] = edits;
        }

        return Task.FromResult<WorkspaceEdit?>(changes.Count == 0
            ? null
            : new WorkspaceEdit { Changes = changes });
    }

    private static List<TextEdit> FindEdits(string source, string oldName, string newName)
    {
        var edits = new List<TextEdit>();
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var start = 0;
            while (start < line.Length)
            {
                var index = line.IndexOf(oldName, start, StringComparison.Ordinal);
                if (index < 0)
                    break;
                var end = index + oldName.Length;
                if (IsBoundary(line, index - 1) && IsBoundary(line, end))
                {
                    edits.Add(new TextEdit
                    {
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                            new Position(lineIndex, index),
                            new Position(lineIndex, end)),
                        NewText = newName,
                    });
                }
                start = Math.Max(end, index + 1);
            }
        }
        return edits;
    }

    private static bool IsIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsBoundary(string line, int index) =>
        index < 0 || index >= line.Length || !(char.IsLetterOrDigit(line[index]) || line[index] == '_');
}
