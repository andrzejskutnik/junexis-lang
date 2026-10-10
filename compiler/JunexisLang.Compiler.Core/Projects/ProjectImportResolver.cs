using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Core.Projects;

/// <summary>
/// Holds the parsed syntax tree of one project module.
/// </summary>
/// <param name="Module">The physical and logical module descriptor.</param>
/// <param name="Program">The parsed module program.</param>
public sealed record ProjectModuleDocument(ProjectModule Module, ProgramNode Program);

/// <summary>
/// Represents one resolved import while preserving its alias and selective symbol syntax.
/// </summary>
/// <param name="Source">The importing module.</param>
/// <param name="Target">The imported module.</param>
/// <param name="Import">The original import declaration.</param>
public sealed record ProjectImportEdge(
    ProjectModuleDocument Source,
    ProjectModuleDocument Target,
    ImportNode Import);

/// <summary>
/// Represents a fully resolved deterministic project module graph.
/// </summary>
/// <param name="Modules">Parsed modules ordered by canonical identity.</param>
/// <param name="Edges">Resolved imports ordered by source, target, and source position.</param>
/// <param name="BuildOrder">Dependency-first module order.</param>
public sealed record ProjectModuleGraph(
    IReadOnlyList<ProjectModuleDocument> Modules,
    IReadOnlyList<ProjectImportEdge> Edges,
    IReadOnlyList<ProjectModuleDocument> BuildOrder);

/// <summary>
/// Contains project import resolution diagnostics and the partial or complete graph.
/// </summary>
/// <param name="Success">Whether parsing and import resolution completed without errors.</param>
/// <param name="Diagnostics">Stable parser and module diagnostics.</param>
/// <param name="Graph">The resolved graph, including partial edges when diagnostics exist.</param>
public sealed record ProjectResolutionResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    ProjectModuleGraph Graph);

/// <summary>
/// Parses a project module catalog once and resolves imports into a deterministic graph.
/// </summary>
public static class ProjectImportResolver
{
    /// <summary>
    /// Parses every catalog module exactly once and resolves its import declarations.
    /// </summary>
    /// <param name="catalog">The validated module catalog.</param>
    /// <returns>The graph and diagnostics for missing modules, case mismatches, syntax failures, and cycles.</returns>
    public static async Task<ProjectResolutionResult> ResolveAsync(ProjectModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var compiler = new JunexisCompiler();
        var diagnostics = new List<Diagnostic>();
        var documents = new List<ProjectModuleDocument>();
        foreach (var module in catalog.Modules)
        {
            var parse = await compiler.ParseAsync(new ParseOptions(module.SourceFile.FullName));
            diagnostics.AddRange(parse.Diagnostics);
            if (parse.Ast is not null)
                documents.Add(new ProjectModuleDocument(module, parse.Ast));
        }

        var byIdentity = documents.ToDictionary(
            document => document.Module.Identity.Value,
            StringComparer.Ordinal);
        var edges = new List<ProjectImportEdge>();
        foreach (var source in documents)
        {
            foreach (var import in source.Program.Imports)
            {
                var requested = new ModuleIdentity(import.ModulePath);
                if (byIdentity.TryGetValue(requested.Value, out var target))
                {
                    edges.Add(new ProjectImportEdge(source, target, import));
                    continue;
                }

                // The target exists but could not produce an AST. Its parser diagnostic is
                // already authoritative; do not misclassify it as a missing or case-mismatched module.
                if (catalog.TryResolve(requested, out _))
                    continue;

                if (catalog.TryResolvePortable(requested, out var portable))
                {
                    diagnostics.Add(new Diagnostic(
                        "E0006",
                        DiagnosticLevel.Error,
                        "Import",
                        $"Import module case mismatch: '{requested}' should be '{portable!.Identity}'.",
                        import.File,
                        import.Line,
                        import.Column));
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        "E0004",
                        DiagnosticLevel.Error,
                        "Import",
                        $"Imported module not found: '{requested}'.",
                        import.File,
                        import.Line,
                        import.Column));
                }
            }
        }

        var orderedEdges = edges
            .OrderBy(edge => edge.Source.Module.Identity.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target.Module.Identity.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.Import.Line)
            .ThenBy(edge => edge.Import.Column)
            .ToArray();
        var buildOrder = BuildDependencyOrder(documents, orderedEdges, diagnostics);
        var graph = new ProjectModuleGraph(documents, orderedEdges, buildOrder);
        return new ProjectResolutionResult(
            diagnostics.All(diagnostic => diagnostic.Level != DiagnosticLevel.Error),
            diagnostics,
            graph);
    }

    private static IReadOnlyList<ProjectModuleDocument> BuildDependencyOrder(
        IReadOnlyList<ProjectModuleDocument> documents,
        IReadOnlyList<ProjectImportEdge> edges,
        ICollection<Diagnostic> diagnostics)
    {
        var outgoing = edges
            .GroupBy(edge => edge.Source.Module.Identity.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var states = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        var stack = new List<ProjectModuleDocument>();
        var order = new List<ProjectModuleDocument>();
        var reportedCycles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in documents)
            Visit(document);

        return order;

        void Visit(ProjectModuleDocument document)
        {
            var identity = document.Module.Identity.Value;
            if (states.TryGetValue(identity, out var state) && state is VisitState.Visiting or VisitState.Visited)
                return;

            states[identity] = VisitState.Visiting;
            stack.Add(document);
            if (outgoing.TryGetValue(identity, out var dependencies))
            {
                foreach (var edge in dependencies)
                {
                    var targetIdentity = edge.Target.Module.Identity.Value;
                    if (states.GetValueOrDefault(targetIdentity) == VisitState.Visiting)
                    {
                        var cycleStart = stack.FindIndex(item =>
                            item.Module.Identity.Value.Equals(targetIdentity, StringComparison.Ordinal));
                        var cycle = stack.Skip(cycleStart)
                            .Select(item => item.Module.Identity.Value)
                            .Append(targetIdentity)
                            .ToArray();
                        var signature = string.Join(" -> ", cycle);
                        if (reportedCycles.Add(signature))
                        {
                            diagnostics.Add(new Diagnostic(
                                "E0005",
                                DiagnosticLevel.Error,
                                "Import",
                                $"Module import cycle: {signature}.",
                                edge.Import.File,
                                edge.Import.Line,
                                edge.Import.Column));
                        }
                    }
                    else
                    {
                        Visit(edge.Target);
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            states[identity] = VisitState.Visited;
            order.Add(document);
        }
    }

    private enum VisitState
    {
        Unvisited,
        Visiting,
        Visited
    }
}
