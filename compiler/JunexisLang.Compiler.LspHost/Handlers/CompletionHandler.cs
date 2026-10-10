using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.LspHost.Analysis;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>
/// Zwraca listę podpowiedzi: słowa kluczowe + typy wbudowane + nazwy symboli z pliku.
/// </summary>
internal sealed class CompletionHandler(DocumentStore store) : CompletionHandlerBase
{
    private static readonly string[] Keywords =
    [
        // Deklaracje
        "task", "struct", "enum", "fn", "pipeline", "device",
        "narrative", "deployment", "supervisor", "circuit_breaker", "quirks",
        "extern", "type", "protocol", "import", "storage", "entity", "query",
        "filter", "project", "sort", "limit", "offset", "key", "asc", "desc",
        "mutation", "insert", "update", "delete", "single", "many", "set", "where",
        // Zmienne i stan
        "let", "const", "var", "state", "use",
        // Przepływ sterowania
        "return", "if", "else", "for", "in", "match", "every", "loop", "delay", "log", "as",
        // Zdarzenia cyklu życia
        "on",
        // Komunikacja
        "send", "recv", "respond", "to", "from", "broadcast", "parallel",
        // Wartości logiczne i operatory alternatywne
        "and", "or", "not", "true", "false",
        // Konfiguracja
        "config", "native"
    ];

    private static readonly string[] BuiltInTypes =
    [
        // Typy skalarne
        "Int", "Float", "Bool", "Text", "Time", "Symbol", "Duration", "Null",
        "Bytes", "Decimal", "Uuid", "Json",
        // Typy kolekcji
        "Map", "List", "Option", "Result",
        // Typy komunikacyjne (lowercase — zgodnie z lexerem)
        "chan", "queue", "stream",
        // Typy numeryczne niskopoziomowe
        "u8", "u16", "u32", "u64", "i8", "i16", "i32", "i64", "f32", "f64"
    ];

    private static readonly string[] Platforms = ["embedded", "server", "report"];

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability _c, ClientCapabilities _caps) =>
        new()
        {
            DocumentSelector  = TextDocumentSelector.ForLanguage("junexis"),
            TriggerCharacters = new Container<string>(".", ":", "<", "@")
        };

    public override Task<CompletionItem> Handle(CompletionItem item, CancellationToken _) =>
        Task.FromResult(item);

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken _)
    {
        var (source, ast, _) = store.Get(request.TextDocument.Uri.ToString());
        var items = new List<CompletionItem>(256);

        // ── Context-aware field completion on '.' trigger ──────────────────
        if (ast is not null && source is not null &&
            request.Context?.TriggerCharacter == ".")
        {
            var wordBefore = GetWordBeforeDot(source,
                (int)request.Position.Line,
                (int)request.Position.Character);

            if (wordBefore is not null)
            {
                var fieldItems = GetFieldCompletions(ast, wordBefore);
                if (fieldItems.Count > 0)
                    return Task.FromResult(new CompletionList(fieldItems));
            }
        }

        // ── Fallback: keywords + built-in types + symbols ──────────────────
        items.AddRange(Keywords.Select(kw =>
            new CompletionItem { Label = kw, Kind = CompletionItemKind.Keyword }));

        items.AddRange(BuiltInTypes.Select(t =>
            new CompletionItem { Label = t, Kind = CompletionItemKind.Class, Detail = "built-in" }));

        items.AddRange(Platforms.Select(platform =>
            new CompletionItem
            {
                Label = platform,
                Kind = CompletionItemKind.EnumMember,
                Detail = $"@{platform} platform"
            }));

        if (ast is not null)
        {
            var index = SymbolIndex.Build(ast);
            items.AddRange(index.AllNames().Select(name =>
            {
                var node = index.Find(name)!;
                return new CompletionItem
                {
                    Label  = name,
                    Kind   = Kind(node),
                    Detail = Detail(node)
                };
            }));
        }

        var existing = items.Select(item => item.Label).ToHashSet(StringComparer.Ordinal);
        items.AddRange(store.StorageQuerySymbols(request.TextDocument.Uri.ToString())
            .Where(symbol => existing.Add(symbol.Name))
            .Select(symbol => new CompletionItem
            {
                Label = symbol.Name,
                Kind = symbol.Kind == StorageQueryIdeSymbolKind.Function
                    ? CompletionItemKind.Function
                    : CompletionItemKind.Struct,
                Detail = symbol.Detail
            }));

        return Task.FromResult(new CompletionList(items));
    }

    // ── Field completion helpers ───────────────────────────────────────────

    /// <summary>
    /// Wyciąga identyfikator bezpośrednio przed pozycją kursora (zakłada, że kursor
    /// stoi tuż za właśnie wpisaną kropką).
    /// </summary>
    private static string? GetWordBeforeDot(string source, int line, int character)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        if (line >= lines.Length) return null;
        var ln = lines[line];

        // character = pozycja po '.', więc '.' jest na character-1
        var dotPos = character - 1;
        if (dotPos <= 0 || dotPos > ln.Length || (dotPos < ln.Length && ln[dotPos] != '.'))
            return null;

        var end   = dotPos;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(ln[start - 1]) || ln[start - 1] == '_'))
            start--;

        return start < end ? ln[start..end] : null;
    }

    /// <summary>
    /// Zwraca pola struktury lub bloku config dla zmiennej <paramref name="varName"/>.
    /// Dla <c>config</c>: pola <c>ConfigBlockNode</c> z pierwszego taska.
    /// Dla innych zmiennych: pola struktury odpowiadającej typowi param/state.
    /// </summary>
    private static List<CompletionItem> GetFieldCompletions(ProgramNode ast, string varName)
    {
        var result = new List<CompletionItem>();

        if (string.Equals(varName, "config", StringComparison.Ordinal))
        {
            foreach (var decl in ast.Declarations)
            {
                if (decl is not TaskDeclarationNode task) continue;
                foreach (var bodyNode in task.Body)
                {
                    if (bodyNode is not ConfigBlockNode cfg) continue;
                    foreach (var f in cfg.Fields)
                        result.Add(new CompletionItem
                        {
                            Label  = f.Name,
                            Kind   = CompletionItemKind.Field,
                            Detail = $"config: {FT(f.Type)}"
                        });
                    return result;
                }
            }
            return result;
        }

        // Szukaj nazwy w parametrach i deklaracjach state
        string? typeName = null;
        foreach (var decl in ast.Declarations)
        {
            if (decl is not TaskDeclarationNode task) continue;

            foreach (var p in task.Params)
            {
                if (p.Name == varName && p.Type is NamedTypeNode namedP)
                {
                    typeName = namedP.Name;
                    break;
                }
            }
            if (typeName is not null) break;

            foreach (var bodyNode in task.Body)
            {
                if (bodyNode is StateDeclarationNode state &&
                    state.Name == varName &&
                    state.Type is NamedTypeNode namedS)
                {
                    typeName = namedS.Name;
                    break;
                }
            }
            if (typeName is not null) break;
        }

        if (typeName is null) return result;

        // Znajdź definicję struktury i zwróć pola
        foreach (var decl in ast.Declarations)
        {
            if (decl is not StructDeclarationNode s || s.Name != typeName) continue;
            foreach (var f in s.Fields)
                result.Add(new CompletionItem
                {
                    Label  = f.Name,
                    Kind   = CompletionItemKind.Field,
                    Detail = FT(f.Type)
                });
            break;
        }

        return result;
    }

    private static string FT(TypeRefNode t) => t switch
    {
        NamedTypeNode n   => n.Name,
        CommTypeNode c    => $"{c.Kind.ToString().ToLowerInvariant()}<{FT(c.Element)}>",
        ArrayTypeNode a   => $"[{FT(a.Element)}]",
        MapTypeNode m     => $"Map<{FT(m.Key)}, {FT(m.Value)}>",
        GenericTypeNode g => $"{g.Name}<{string.Join(", ", g.TypeArgs.Select(FT))}>",
        UnionTypeNode u   => string.Join(" | ", u.Variants.Select(FT)),
        _                 => "?"
    };

    private static CompletionItemKind Kind(AstNode n) => n switch
    {
        TaskDeclarationNode     => CompletionItemKind.Function,
        StructDeclarationNode   => CompletionItemKind.Class,
        EnumDeclarationNode     => CompletionItemKind.Enum,
        FnDeclarationNode       => CompletionItemKind.Function,
        PipelineDeclarationNode => CompletionItemKind.Module,
        DeviceDeclarationNode   => CompletionItemKind.Interface,
        _                       => CompletionItemKind.Variable
    };

    private static string Detail(AstNode n) => n switch
    {
        TaskDeclarationNode     => "task",
        StructDeclarationNode   => "struct",
        EnumDeclarationNode     => "enum",
        FnDeclarationNode       => "fn",
        PipelineDeclarationNode => "pipeline",
        DeviceDeclarationNode   => "device",
        _                       => "symbol"
    };
}
