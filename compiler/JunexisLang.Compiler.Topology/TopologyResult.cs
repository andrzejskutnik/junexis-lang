namespace JunexisLang.Compiler.Topology;

using JunexisLang.Compiler.Core;

/// <summary>Wynik analizy topologicznej grafu pipeline.</summary>
/// <param name="SCCs">Silnie spójne składowe (Tarjan SCC). Każda lista = jeden SCC.</param>
/// <param name="HasCycles">True jeśli któryś SCC tworzy cykl.</param>
/// <param name="FanOut">Fan-out: liczba wyjść per węzeł (task name → count).</param>
/// <param name="FanIn">Fan-in: liczba wejść per węzeł (task name → count).</param>
/// <param name="Depth">Głębokość tasku w DAG-u kondensacji SCC; korzenie mają 0.</param>
/// <param name="MaxDepth">Maksymalna głębokość logicznej topologii.</param>
/// <param name="Diagnostics">Diagnostyki emitowane podczas analizy.</param>
public sealed record TopologyResult(
    IReadOnlyList<IReadOnlyList<string>> SCCs,
    bool HasCycles,
    IReadOnlyDictionary<string, int> FanOut,
    IReadOnlyDictionary<string, int> FanIn,
    IReadOnlyDictionary<string, int> Depth,
    int MaxDepth,
    IReadOnlyList<Diagnostic> Diagnostics);
