using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.LspHost;

internal enum StorageQueryIdeSymbolKind
{
    Function,
    RowType
}

internal sealed record StorageQueryIdeSymbol(
    string Name,
    StorageQueryIdeSymbolKind Kind,
    string Detail,
    string Markdown,
    StorageQueryNode Definition);

/// <summary>Buduje widok pochodnych symboli named query dla jednego zakresu modułu.</summary>
internal static class StorageQueryIdeIndex
{
    internal static IReadOnlyDictionary<string, StorageQueryIdeSymbol> FromProgram(ProgramNode program)
        => Build(program.Declarations.OfType<StorageDeclarationNode>()
            .Select(storage => (VisibleName: storage.Name, Storage: storage)));

    internal static IReadOnlyDictionary<string, StorageQueryIdeSymbol> FromScope(
        IEnumerable<ProjectVisibleSymbol> symbols)
        => Build(symbols
            .Where(symbol => symbol.Declaration is StorageDeclarationNode &&
                             !symbol.VisibleName.Contains("::", StringComparison.Ordinal))
            .Select(symbol => (symbol.VisibleName, (StorageDeclarationNode)symbol.Declaration)));

    private static IReadOnlyDictionary<string, StorageQueryIdeSymbol> Build(
        IEnumerable<(string VisibleName, StorageDeclarationNode Storage)> models)
    {
        var result = new Dictionary<string, StorageQueryIdeSymbol>(StringComparer.Ordinal);
        foreach (var (visibleName, storage) in models)
        {
            foreach (var query in storage.Queries)
            {
                var callable = StorageQueryNames.Callable(visibleName, query.Name);
                var row = StorageQueryNames.Row(visibleName, query.Name);
                var parameters = string.Join(", ", query.Params.Select(parameter =>
                    $"{parameter.Name}: {FormatType(parameter.Type)}"));
                var aliasNote = visibleName == storage.Name
                    ? ""
                    : $"\n\nLokalny alias modelu: `{visibleName}` → `{storage.Name}`.";
                result.TryAdd(callable, new StorageQueryIdeSymbol(
                    callable,
                    StorageQueryIdeSymbolKind.Function,
                    $"named query {storage.Name}.{query.Name}",
                    $$"""
                    ```junexis
                    {{callable}}({{parameters}}) -> List<{{row}}>
                    ```

                    Typowane named query tylko do odczytu. Efekt I/O jest dozwolony w tasku `@server`.

                    Kanoniczny model Runtime: `{{storage.Name}}`.{{aliasNote}}
                    """,
                    query));

                var fields = ResultFields(storage, query);
                var fieldLines = string.Join("\n  ", fields.Select(field =>
                    $"{field.Name}: {FormatType(field.Type)}"));
                result.TryAdd(row, new StorageQueryIdeSymbol(
                    row,
                    StorageQueryIdeSymbolKind.RowType,
                    $"wynik {storage.Name}.{query.Name}",
                    $$"""
                    ```junexis
                    struct {{row}} {
                      {{fieldLines}}
                    }
                    ```

                    Syntetyczny typ wyniku named query `{{storage.Name}}.{{query.Name}}`.{{aliasNote}}
                    """,
                    query));
            }
        }
        return result;
    }

    private static IReadOnlyList<(string Name, TypeRefNode Type)> ResultFields(
        StorageDeclarationNode storage,
        StorageQueryNode query)
    {
        var entity = storage.Entities.FirstOrDefault(item => item.Name == query.SourceEntity);
        IReadOnlyList<(string Name, TypeRefNode Type)> fields = entity?.Fields
            .Select(field => (field.Name, field.Type))
            .ToList() ?? [];
        foreach (var project in query.Steps.OfType<StorageProjectStepNode>())
        {
            var projected = new List<(string Name, TypeRefNode Type)>();
            foreach (var item in project.Projections)
            {
                var source = fields.FirstOrDefault(field => field.Name == item.Field);
                if (source.Type is not null)
                    projected.Add((item.Alias ?? item.Field, source.Type));
            }
            fields = projected;
        }
        return fields;
    }

    private static string FormatType(TypeRefNode type) => type switch
    {
        NamedTypeNode named => named.Name,
        CommTypeNode communication =>
            $"{communication.Kind.ToString().ToLowerInvariant()}<{FormatType(communication.Element)}>",
        MapTypeNode map => $"Map<{FormatType(map.Key)}, {FormatType(map.Value)}>",
        GenericTypeNode generic =>
            $"{generic.Name}<{string.Join(", ", generic.TypeArgs.Select(FormatType))}>",
        ArrayTypeNode array => $"[{FormatType(array.Element)}]",
        UnionTypeNode union => string.Join(" | ", union.Variants.Select(FormatType)),
        _ => "?"
    };
}
