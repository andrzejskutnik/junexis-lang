namespace JunexisLang.Compiler.CodeGen;

using JunexisLang.Compiler.IR;

/// <summary>
/// Wspólna polityka doboru pól stanu dla generatorów codegen.
/// </summary>
internal static class StateAccessPlanner
{
    public static IReadOnlyList<IRStateDecl> GetReadPreferredCompatibleStates(
        IRTask task,
        IRType targetType,
        Func<IRType, string> mapType)
        => task.States
            .Where(state => mapType(state.Type) == mapType(targetType))
            .OrderBy(state => GetReadPriority(state.Name))
            .ThenBy(state => state.Name, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<IRStateDecl> GetWritePreferredCompatibleStates(
        IRTask task,
        IRType targetType,
        Func<IRType, string> mapType)
        => task.States
            .Where(state => mapType(state.Type) == mapType(targetType))
            .OrderBy(state => GetWritePriority(state.Name))
            .ThenBy(state => state.Name, StringComparer.Ordinal)
            .ToArray();

    private static int GetReadPriority(string stateName)
    {
        var normalized = stateName.ToLowerInvariant();
        return normalized switch
        {
            "latest" or "latestvalue" => 0,
            "current" or "currentvalue" or "value" => 1,
            "last" or "lastvalue" => 2,
            _ when normalized.StartsWith("latest", StringComparison.Ordinal) => 3,
            _ when normalized.StartsWith("current", StringComparison.Ordinal) => 4,
            _ when normalized.StartsWith("last", StringComparison.Ordinal) => 5,
            _ => 10
        };
    }

    private static int GetWritePriority(string stateName)
    {
        var normalized = stateName.ToLowerInvariant();
        return normalized switch
        {
            "current" or "currentvalue" or "value" => 0,
            "latest" or "latestvalue" => 1,
            "last" or "lastvalue" => 2,
            _ when normalized.StartsWith("current", StringComparison.Ordinal) => 3,
            _ when normalized.StartsWith("latest", StringComparison.Ordinal) => 4,
            _ when normalized.StartsWith("last", StringComparison.Ordinal) => 5,
            _ => 10
        };
    }
}