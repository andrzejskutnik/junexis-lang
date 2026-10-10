namespace JunexisLang.Compiler.CodeGen;

using JunexisLang.Compiler.IR;

/// <summary>
/// Fabryka generatorów kodu — zwraca odpowiednie generatory na podstawie listy platform.
/// </summary>
public static class CodeGeneratorFactory
{
    private static readonly IReadOnlyList<ICodeGenerator> All =
    [
        new CppCodeGenerator(),
        new GoCodeGenerator(),
        new TypeScriptCodeGenerator(),
        new ProtoSchemaGenerator(),
    ];

    /// <summary>Canonical target names accepted by the code generation pipeline.</summary>
    public static IReadOnlySet<string> SupportedPlatforms { get; } =
        All.Select(generator => generator.Platform).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Canonical user-selectable execution targets.</summary>
    public static IReadOnlySet<string> ExecutionPlatforms { get; } =
        new HashSet<string>(["embedded", "server", "report"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns execution targets that have at least one task in the analyzed document.
    /// Auxiliary generators such as <c>proto</c> are intentionally excluded.
    /// </summary>
    /// <param name="document">Structural IR document inspected for platform annotations.</param>
    /// <returns>Sorted, distinct execution target names.</returns>
    public static IReadOnlyList<string> GetApplicableExecutionPlatforms(IRDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Tasks.SelectMany(task => task.Platforms)
            .Select(platform => platform.Target)
            .Select(target => target.ToLowerInvariant())
            .Where(ExecutionPlatforms.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Zwraca generatory dla podanych platform.
    /// Jeśli <paramref name="targets"/> jest null lub puste — zwraca wszystkie.
    /// </summary>
    public static IReadOnlyList<ICodeGenerator> Get(IReadOnlyList<string>? targets)
    {
        if (targets is not { Count: > 0 })
            return All;

        var set = targets.Select(t => t.ToLowerInvariant()).ToHashSet();
        // "proto" can be explicitly requested; also always include when no target filter
        return [.. All.Where(g => set.Contains(g.Platform))];
    }
}
