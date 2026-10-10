namespace JunexisLang.Compiler.Topology;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Analizuje logiczną topologię tasków: SCC/cykle, fan-in, fan-out i głębokość
/// grafu kondensacji. Nie wybiera transportu, protokołu ani placementu.
/// </summary>
public static class TopologyAnalyzer
{
    private const int FanThreshold = 4;

    /// <summary>Buduje graf z dokumentu IR i uruchamia analizę.</summary>
    public static TopologyResult Analyze(IRDocument document) => Analyze(PipelineGraph.Build(document));

    /// <summary>Analizuje gotowy logiczny graf pipeline.</summary>
    public static TopologyResult Analyze(PipelineGraph graph)
    {
        var sccs = TarjanScc(graph);
        var selfLoops = graph.Edges
            .Where(edge => edge.From == edge.To)
            .Select(edge => edge.From)
            .ToHashSet(StringComparer.Ordinal);
        var cyclicSccs = sccs
            .Where(scc => scc.Count > 1 || (scc.Count == 1 && selfLoops.Contains(scc[0])))
            .ToList();

        var fanOut = graph.Nodes.ToDictionary(
            node => node,
            node => graph.Edges
                .Where(edge => edge.From == node)
                .Select(edge => (edge.To, edge.ToPort))
                .Distinct()
                .Count(),
            StringComparer.Ordinal);
        var fanIn = graph.Nodes.ToDictionary(
            node => node,
            node => graph.Edges
                .Where(edge => edge.To == node)
                .Select(edge => (edge.From, edge.FromPort))
                .Distinct()
                .Count(),
            StringComparer.Ordinal);

        var (depth, maxDepth) = ComputeDepth(graph, sccs);
        var diagnostics = new List<Diagnostic>(graph.Diagnostics);

        foreach (var scc in cyclicSccs)
        {
            var cycle = FindCyclePath(graph, scc);
            var source = cycle.Count > 0 && graph.NodeSources.TryGetValue(cycle[0], out var origin)
                ? origin
                : null;
            diagnostics.Add(new Diagnostic(
                "T0001",
                DiagnosticLevel.Warning,
                "Topology",
                $"Logical pipeline cycle: {string.Join(" → ", cycle)}.",
                source?.File,
                source?.Line ?? 0,
                source?.Column ?? 0));
        }

        foreach (var (node, count) in fanOut.Where(item => item.Value >= FanThreshold))
            diagnostics.Add(NodeDiagnostic(
                "T0003", DiagnosticLevel.Info, graph, node,
                $"High logical fan-out {count} for task '{node}'."));

        foreach (var (node, count) in fanIn.Where(item => item.Value >= FanThreshold))
            diagnostics.Add(NodeDiagnostic(
                "T0004", DiagnosticLevel.Info, graph, node,
                $"High logical fan-in {count} for task '{node}'."));

        return new TopologyResult(sccs, cyclicSccs.Count > 0, fanOut, fanIn, depth, maxDepth, diagnostics);
    }

    private static Diagnostic NodeDiagnostic(
        string code,
        DiagnosticLevel level,
        PipelineGraph graph,
        string node,
        string message)
    {
        var source = graph.NodeSources.TryGetValue(node, out var origin) ? origin : null;
        return new Diagnostic(code, level, "Topology", message,
            source?.File, source?.Line ?? 0, source?.Column ?? 0);
    }

    private static (IReadOnlyDictionary<string, int> Depth, int MaxDepth) ComputeDepth(
        PipelineGraph graph,
        IReadOnlyList<List<string>> sccs)
    {
        var componentByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < sccs.Count; index++)
            foreach (var node in sccs[index])
                componentByNode[node] = index;

        var successors = Enumerable.Range(0, sccs.Count)
            .ToDictionary(index => index, _ => new HashSet<int>());
        var inDegree = Enumerable.Range(0, sccs.Count).ToDictionary(index => index, _ => 0);
        foreach (var edge in graph.Edges)
        {
            var from = componentByNode[edge.From];
            var to = componentByNode[edge.To];
            if (from != to && successors[from].Add(to))
                inDegree[to]++;
        }

        var queue = new Queue<int>(inDegree.Where(item => item.Value == 0).Select(item => item.Key));
        var componentDepth = Enumerable.Range(0, sccs.Count).ToDictionary(index => index, _ => 0);
        while (queue.Count > 0)
        {
            var component = queue.Dequeue();
            foreach (var successor in successors[component])
            {
                componentDepth[successor] = Math.Max(
                    componentDepth[successor],
                    componentDepth[component] + 1);
                if (--inDegree[successor] == 0)
                    queue.Enqueue(successor);
            }
        }

        var depth = graph.Nodes.ToDictionary(
            node => node,
            node => componentDepth[componentByNode[node]],
            StringComparer.Ordinal);
        return (depth, depth.Count == 0 ? 0 : depth.Values.Max());
    }

    private static IReadOnlyList<string> FindCyclePath(PipelineGraph graph, IReadOnlyList<string> scc)
    {
        var allowed = scc.ToHashSet(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        List<string>? found = null;

        foreach (var start in scc)
        {
            if (!visited.Contains(start) && Visit(start))
                return found!;
        }

        return scc.Count == 0 ? [] : [scc[0], scc[0]];

        bool Visit(string node)
        {
            visited.Add(node);
            active.Add(node);
            path.Add(node);

            foreach (var successor in graph.Successors(node).Where(allowed.Contains))
            {
                if (!visited.Contains(successor))
                {
                    if (Visit(successor))
                        return true;
                }
                else if (active.Contains(successor))
                {
                    var cycleStart = path.IndexOf(successor);
                    found = [.. path.Skip(cycleStart), successor];
                    return true;
                }
            }

            active.Remove(node);
            path.RemoveAt(path.Count - 1);
            return false;
        }
    }

    private static List<List<string>> TarjanScc(PipelineGraph graph)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowlink = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var sccs = new List<List<string>>();
        var counter = 0;

        foreach (var node in graph.Nodes)
            if (!index.ContainsKey(node))
                StrongConnect(node);

        return sccs;

        void StrongConnect(string node)
        {
            index[node] = lowlink[node] = counter++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var successor in graph.Successors(node))
            {
                if (!index.ContainsKey(successor))
                {
                    StrongConnect(successor);
                    lowlink[node] = Math.Min(lowlink[node], lowlink[successor]);
                }
                else if (onStack.Contains(successor))
                {
                    lowlink[node] = Math.Min(lowlink[node], index[successor]);
                }
            }

            if (lowlink[node] != index[node])
                return;

            var scc = new List<string>();
            string current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                scc.Add(current);
            } while (current != node);
            sccs.Add(scc);
        }
    }
}
