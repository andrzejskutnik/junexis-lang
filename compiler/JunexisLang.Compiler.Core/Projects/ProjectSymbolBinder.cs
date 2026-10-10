using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Core.Projects;

/// <summary>Describes one declaration visible in a module after applying its imports.</summary>
/// <param name="VisibleName">The unqualified or namespace-qualified name visible to the consumer.</param>
/// <param name="Declaration">The original declaration in its defining module.</param>
/// <param name="DefiningModule">The module that owns the declaration.</param>
/// <param name="IsLocal">Whether the declaration belongs to the consuming module.</param>
public sealed record ProjectVisibleSymbol(
    string VisibleName,
    AstNode Declaration,
    ModuleIdentity DefiningModule,
    bool IsLocal);

/// <summary>Contains module symbol scopes and import binding diagnostics.</summary>
/// <param name="Success">Whether all imports could be bound without collisions.</param>
/// <param name="Diagnostics">Missing-symbol and collision diagnostics.</param>
/// <param name="Scopes">Visible symbols keyed by consuming module identity.</param>
public sealed record ProjectSymbolBindingResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, ProjectVisibleSymbol>> Scopes);

/// <summary>Builds deterministic per-module symbol scopes from a resolved import graph.</summary>
public static class ProjectSymbolBinder
{
    /// <summary>Binds local declarations and glob, alias, or selective imports.</summary>
    /// <param name="graph">A successfully resolved acyclic module graph.</param>
    /// <returns>Bound scopes and stable diagnostics.</returns>
    public static ProjectSymbolBindingResult Bind(ProjectModuleGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var diagnostics = new List<Diagnostic>();
        var exports = graph.Modules.ToDictionary(
            document => document.Module.Identity.Value,
            document => GetExports(document).ToDictionary(symbol => symbol.VisibleName, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var scopes = new Dictionary<string, IReadOnlyDictionary<string, ProjectVisibleSymbol>>(StringComparer.Ordinal);

        foreach (var document in graph.Modules)
        {
            var local = exports[document.Module.Identity.Value];
            var scope = new Dictionary<string, ProjectVisibleSymbol>(local, StringComparer.Ordinal);
            foreach (var edge in graph.Edges.Where(edge => edge.Source == document))
            {
                var target = exports[edge.Target.Module.Identity.Value];
                if (edge.Import.Alias is { } moduleAlias)
                {
                    foreach (var symbol in target.Values)
                        Add(scope, symbol with { VisibleName = $"{moduleAlias}::{symbol.VisibleName}", IsLocal = false }, edge.Import, diagnostics);
                    if (target.Values.Any(symbol =>
                            symbol.Declaration is StorageDeclarationNode { Queries.Count: > 0 }))
                    {
                        diagnostics.Add(new Diagnostic(
                            "SQC006",
                            DiagnosticLevel.Warning,
                            "StorageQuery",
                            $"Alias modułu '{moduleAlias}' nie udostępnia kwalifikowanych wywołań named query; użyj selective import modelu storage.",
                            edge.Import.File,
                            edge.Import.Line,
                            edge.Import.Column));
                    }
                }
                else if (edge.Import.Symbols.Count > 0)
                {
                    foreach (var selected in edge.Import.Symbols)
                    {
                        if (!target.TryGetValue(selected.Name, out var symbol))
                        {
                            diagnostics.Add(new Diagnostic("E0007", DiagnosticLevel.Error, "Import",
                                $"Imported symbol not found: '{selected.Name}' in module '{edge.Target.Module.Identity}'.",
                                selected.File, selected.Line, selected.Column));
                            continue;
                        }

                        Add(scope, symbol with { VisibleName = selected.Alias ?? selected.Name, IsLocal = false }, selected, diagnostics);
                    }
                }
                else
                {
                    foreach (var symbol in target.Values)
                        Add(scope, symbol with { IsLocal = false }, edge.Import, diagnostics);
                }
            }

            scopes.Add(document.Module.Identity.Value, scope);
        }

        return new ProjectSymbolBindingResult(
            diagnostics.All(diagnostic => diagnostic.Level != DiagnosticLevel.Error), diagnostics, scopes);
    }

    private static IEnumerable<ProjectVisibleSymbol> GetExports(ProjectModuleDocument document)
    {
        foreach (var declaration in document.Program.Declarations)
        {
            var name = declaration switch
            {
                StructDeclarationNode value => value.Name,
                EnumDeclarationNode value => value.Name,
                FnDeclarationNode value => value.Name,
                TaskDeclarationNode value => value.Name,
                ExternTaskNode value => value.Name,
                ExternTypeNode value => value.Name,
                ExternContractNode value => value.Name,
                StorageDeclarationNode value => value.Name,
                _ => null
            };
            if (name is not null)
                yield return new ProjectVisibleSymbol(name, declaration, document.Module.Identity, true);
        }
    }

    private static void Add(
        IDictionary<string, ProjectVisibleSymbol> scope,
        ProjectVisibleSymbol symbol,
        AstNode import,
        ICollection<Diagnostic> diagnostics)
    {
        if (scope.TryAdd(symbol.VisibleName, symbol))
            return;

        diagnostics.Add(new Diagnostic("E0002", DiagnosticLevel.Error, "Import",
            $"Import name collision: '{symbol.VisibleName}' already in scope.",
            import.File, import.Line, import.Column));
    }
}
