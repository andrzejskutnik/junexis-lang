namespace JunexisLang.Compiler.CodeGen;

/// <summary>Creates stable identifiers shared by compiler analysis and debugger contracts.</summary>
public static class StableSymbolIds
{
    /// <summary>Creates an identifier for a named Junexis symbol.</summary>
    public static string Symbol(string kind, string name) => $"{kind}:{name}";

    /// <summary>Creates a semantic identifier for a pipeline communication.</summary>
    public static string Communication(
        string pipeline,
        string sourceTask,
        string? sourcePort,
        string targetTask,
        string? targetPort)
    {
        var source = sourcePort is null ? sourceTask : $"{sourceTask}.{sourcePort}";
        var target = targetPort is null ? targetTask : $"{targetTask}.{targetPort}";
        return $"communication:{pipeline}:{source}->{target}";
    }
}
