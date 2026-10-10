using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Core.Projects;

/// <summary>Composes resolved modules into the executable AST consumed by IR and code generation.</summary>
public static class ProjectProgramComposer
{
    /// <summary>Builds a dependency-first program while reserving orchestration declarations for the entry module.</summary>
    /// <param name="graph">The resolved project graph.</param>
    /// <param name="binding">The bound project symbol scopes.</param>
    /// <param name="entryFile">The physical entry module path.</param>
    /// <returns>A single executable program containing reachable library declarations and entry orchestration.</returns>
    public static ProgramNode Compose(
        ProjectModuleGraph graph,
        ProjectSymbolBindingResult binding,
        string entryFile)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryFile);

        var entry = graph.Modules.Single(document => document.Module.SourceFile.FullName.Equals(
            Path.GetFullPath(entryFile), StringComparison.OrdinalIgnoreCase));
        var reachable = CollectReachable(entry, graph.Edges);
        var declarations = new List<AstNode>();
        foreach (var document in graph.BuildOrder.Where(document => reachable.Contains(document.Module.Identity.Value)))
        {
            var moduleDeclarations = document == entry
                ? document.Program.Declarations
                : document.Program.Declarations.Where(IsLibraryDeclaration);
            var scope = binding.Scopes[document.Module.Identity.Value].Values;
            declarations.AddRange(moduleDeclarations.Select(declaration =>
                StorageImportAliasRewriter.Rewrite(declaration, scope)));
        }

        var existingNames = declarations.Select(GetName).Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var symbol in binding.Scopes[entry.Module.Identity.Value].Values
                     .Where(symbol => !symbol.IsLocal && !symbol.VisibleName.Contains("::", StringComparison.Ordinal)))
        {
            if (symbol.Declaration is StorageDeclarationNode)
                continue;
            var originalName = GetName(symbol.Declaration);
            if (originalName is null || originalName == symbol.VisibleName || existingNames.Contains(symbol.VisibleName))
                continue;

            declarations.Add(Rename(symbol.Declaration, symbol.VisibleName));
            existingNames.Add(symbol.VisibleName);
        }

        return new ProgramNode(entry.Program.File, entry.Program.Imports, declarations);
    }

    private static HashSet<string> CollectReachable(
        ProjectModuleDocument entry,
        IReadOnlyList<ProjectImportEdge> edges)
    {
        var outgoing = edges.GroupBy(edge => edge.Source.Module.Identity.Value)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Target));
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        Visit(entry);
        return reachable;

        void Visit(ProjectModuleDocument document)
        {
            if (!reachable.Add(document.Module.Identity.Value) || !outgoing.TryGetValue(document.Module.Identity.Value, out var targets))
                return;
            foreach (var target in targets)
                Visit(target);
        }
    }

    private static bool IsLibraryDeclaration(AstNode declaration) => declaration is
        StructDeclarationNode or EnumDeclarationNode or FnDeclarationNode or TaskDeclarationNode or
        ExternTaskNode or ExternTypeNode or ExternContractNode or StorageDeclarationNode;

    private static string? GetName(AstNode declaration) => declaration switch
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

    private static AstNode Rename(AstNode declaration, string name) => declaration switch
    {
        StructDeclarationNode value => value with { Name = name },
        EnumDeclarationNode value => value with { Name = name },
        FnDeclarationNode value => value with { Name = name },
        TaskDeclarationNode value => value with { Name = name },
        ExternTaskNode value => value with { Name = name },
        ExternTypeNode value => value with { Name = name },
        ExternContractNode value => value with { Name = name },
        StorageDeclarationNode value => value with { Name = name },
        _ => throw new InvalidOperationException($"Declaration cannot be imported as an alias: {declaration.GetType().Name}")
    };
}
