using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>
/// Klasyfikuje podstawowe elementy semantyczne Junexis dla kolorowania edytora.
/// Klasyfikacja pozostaje odporna na niepełny kod, ponieważ pracuje na tekście
/// otwartego dokumentu, a nie wymaga poprawnego AST.
/// </summary>
internal sealed class SemanticTokensHandler(DocumentStore store) : SemanticTokensHandlerBase
{
    private static readonly SemanticTokensLegend Legend = new()
    {
        TokenTypes = new Container<SemanticTokenType>(
            SemanticTokenType.Comment,
            SemanticTokenType.Keyword,
            SemanticTokenType.String,
            SemanticTokenType.Number,
            SemanticTokenType.Operator,
            SemanticTokenType.Type,
            SemanticTokenType.Struct,
            SemanticTokenType.Enum,
            SemanticTokenType.Function,
            SemanticTokenType.Event,
            SemanticTokenType.Namespace,
            SemanticTokenType.Interface,
            SemanticTokenType.Decorator,
            SemanticTokenType.Variable),
        TokenModifiers = new Container<SemanticTokenModifier>()
    };

    private static readonly HashSet<string> Keywords =
    [
        "task", "struct", "enum", "fn", "pipeline", "device", "protocol",
        "narrative", "deployment", "supervisor", "circuit_breaker", "quirks",
        "extern", "type", "import", "let", "const", "var", "state", "use",
        "return", "if", "else", "for", "in", "match", "every", "loop",
        "delay", "log", "as", "on", "send", "recv", "respond", "to", "from",
        "broadcast", "parallel", "and", "or", "not", "true", "false", "config",
        "native", "storage", "entity", "query", "filter", "project", "sort",
        "limit", "offset", "key", "asc", "desc", "mutation", "insert",
        "update", "delete", "single", "many", "set", "where"
    ];

    private static readonly HashSet<string> BuiltInTypes =
    [
        "Int", "Float", "Bool", "Text", "Time", "Symbol", "Duration", "Null",
        "Bytes", "Decimal", "Uuid", "Json", "Map", "List", "Option", "Result",
        "chan", "queue", "stream", "u8", "u16", "u32", "u64", "i8", "i16",
        "i32", "i64", "f32", "f64"
    ];

    private static readonly Dictionary<string, SemanticTokenType> DeclarationTypes = new()
    {
        ["task"] = SemanticTokenType.Event,
        ["struct"] = SemanticTokenType.Struct,
        ["enum"] = SemanticTokenType.Enum,
        ["fn"] = SemanticTokenType.Function,
        ["pipeline"] = SemanticTokenType.Namespace,
        ["device"] = SemanticTokenType.Interface,
        ["protocol"] = SemanticTokenType.Interface,
        ["storage"] = SemanticTokenType.Namespace,
        ["entity"] = SemanticTokenType.Struct,
        ["query"] = SemanticTokenType.Function,
        ["mutation"] = SemanticTokenType.Function,
        ["type"] = SemanticTokenType.Type,
        ["narrative"] = SemanticTokenType.Namespace,
        ["deployment"] = SemanticTokenType.Namespace,
        ["quirks"] = SemanticTokenType.Namespace,
        ["supervisor"] = SemanticTokenType.Type,
        ["circuit_breaker"] = SemanticTokenType.Type
    };

    private readonly ConcurrentDictionary<string, SemanticTokensDocument> _documents = new();

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability _, ClientCapabilities __) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("junexis"),
            Legend = Legend,
            Full = true,
            Range = true
        };

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams request, CancellationToken _) =>
        Task.FromResult(_documents.GetOrAdd(
            request.TextDocument.Uri.ToString(),
            _ => new SemanticTokensDocument(Legend)));

    protected override Task Tokenize(
        SemanticTokensBuilder builder,
        ITextDocumentIdentifierParams request,
        CancellationToken _)
    {
        var (source, _, _) = store.Get(request.TextDocument.Uri.ToString());
        if (source is null)
            return Task.CompletedTask;

        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var inBlockComment = false;

        for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
            TokenizeLine(builder, lineNumber, lines[lineNumber], ref inBlockComment);

        return Task.CompletedTask;
    }

    private static void TokenizeLine(
        SemanticTokensBuilder builder,
        int lineNumber,
        string line,
        ref bool inBlockComment)
    {
        string? declarationKeyword = null;
        var index = 0;

        while (index < line.Length)
        {
            if (inBlockComment)
            {
                var end = line.IndexOf("*/", index, StringComparison.Ordinal);
                var length = end < 0 ? line.Length - index : end + 2 - index;
                Push(builder, lineNumber, index, length, SemanticTokenType.Comment);
                index += length;
                inBlockComment = end < 0;
                continue;
            }

            if (char.IsWhiteSpace(line[index]))
            {
                index++;
                continue;
            }

            if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/')
            {
                Push(builder, lineNumber, index, line.Length - index, SemanticTokenType.Comment);
                break;
            }

            if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
            {
                var end = line.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var length = end < 0 ? line.Length - index : end + 2 - index;
                Push(builder, lineNumber, index, length, SemanticTokenType.Comment);
                index += length;
                inBlockComment = end < 0;
                continue;
            }

            if (line[index] == '"')
            {
                var end = index + 1;
                while (end < line.Length)
                {
                    if (line[end] == '"' && line[end - 1] != '\\')
                    {
                        end++;
                        break;
                    }
                    end++;
                }
                Push(builder, lineNumber, index, end - index, SemanticTokenType.String);
                index = end;
                continue;
            }

            if (char.IsDigit(line[index]))
            {
                var end = index + 1;
                while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.'))
                    end++;
                Push(builder, lineNumber, index, end - index, SemanticTokenType.Number);
                index = end;
                continue;
            }

            if (IsIdentifierStart(line[index]))
            {
                var end = index + 1;
                while (end < line.Length && IsIdentifierPart(line[end]))
                    end++;

                var word = line[index..end];
                var type = ClassifyIdentifier(word, declarationKeyword, index > 0 && line[index - 1] == '@');
                if (type is not null)
                    Push(builder, lineNumber, index, end - index, type.Value);

                declarationKeyword = DeclarationTypes.ContainsKey(word) ? word : null;
                index = end;
                continue;
            }

            if (IsOperator(line[index]))
                Push(builder, lineNumber, index, 1, line[index] == '@' ? SemanticTokenType.Decorator : SemanticTokenType.Operator);

            index++;
        }
    }

    private static SemanticTokenType? ClassifyIdentifier(string word, string? declarationKeyword, bool decorator)
    {
        if (declarationKeyword is not null)
            return DeclarationTypes[declarationKeyword];
        if (decorator)
            return SemanticTokenType.Decorator;
        if (Keywords.Contains(word))
            return SemanticTokenType.Keyword;
        if (BuiltInTypes.Contains(word) || char.IsUpper(word[0]))
            return SemanticTokenType.Type;
        return SemanticTokenType.Variable;
    }

    private static void Push(
        SemanticTokensBuilder builder,
        int line,
        int start,
        int length,
        SemanticTokenType type) =>
        builder.Push(line, start, length, type, Array.Empty<SemanticTokenModifier>());

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static bool IsOperator(char value) => "@+-*/%=!<>|&?:.".Contains(value, StringComparison.Ordinal);
}
