using JunexisLang.Compiler.LspHost.Analysis;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>Wyszukuje użycia symbolu we wszystkich otwartych dokumentach Junexis.</summary>
internal sealed class ReferencesHandler(DocumentStore store) : ReferencesHandlerBase
{
    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability _capability,
        ClientCapabilities _clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForLanguage("junexis") };

    public override Task<LocationContainer?> Handle(
        ReferenceParams request,
        CancellationToken _)
    {
        var (source, _, _) = store.Get(request.TextDocument.Uri.ToString());
        if (source is null)
            return Task.FromResult<LocationContainer?>(null);

        var word = WordAtPosition.Get(source, request.Position.Line, request.Position.Character);
        if (word is null)
            return Task.FromResult<LocationContainer?>(null);

        var locations = new List<Location>();
        foreach (var document in store.Snapshot())
        {
            var lines = document.Source.ReplaceLineEndings("\n").Split('\n');
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var start = 0;
                while (start < line.Length)
                {
                    var index = line.IndexOf(word.Value.Word, start, StringComparison.Ordinal);
                    if (index < 0)
                        break;
                    var end = index + word.Value.Word.Length;
                    if (IsBoundary(line, index - 1) && IsBoundary(line, end))
                    {
                        locations.Add(new Location
                        {
                            Uri = DocumentUri.From(document.Uri),
                            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                                new Position(lineIndex, index),
                                new Position(lineIndex, end)),
                        });
                    }
                    start = Math.Max(end, index + 1);
                }
            }
        }

        return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
    }

    private static bool IsBoundary(string line, int index) =>
        index < 0 || index >= line.Length || !(char.IsLetterOrDigit(line[index]) || line[index] == '_');
}
