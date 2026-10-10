namespace JunexisLang.Compiler.Topology;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>Jedna logiczna, port-aware krawędź pomiędzy taskami.</summary>
/// <param name="From">Nazwa tasku źródłowego.</param>
/// <param name="FromPort">Opcjonalny port źródłowy.</param>
/// <param name="To">Nazwa tasku docelowego.</param>
/// <param name="ToPort">Opcjonalny port docelowy.</param>
/// <param name="Pipeline">Nazwa deklaracji pipeline.</param>
/// <param name="Source">Lokalizacja połączenia w źródle.</param>
public sealed record PipelineEdge(
    string From,
    string? FromPort,
    string To,
    string? ToPort,
    string Pipeline,
    IRSourceSpan? Source);

/// <summary>
/// Logiczny graf pipeline. Węzły pochodzą wyłącznie z zadeklarowanych tasków;
/// nieznane endpointy stają się diagnostykami zamiast pozornymi węzłami.
/// </summary>
public sealed class PipelineGraph
{
    /// <summary>Wszystkie zadeklarowane taski.</summary>
    public IReadOnlySet<string> Nodes { get; }

    /// <summary>Logiczne krawędzie wraz z portami, pipeline i pochodzeniem.</summary>
    public IReadOnlyList<PipelineEdge> Edges { get; }

    /// <summary>Lokalizacja deklaracji każdego tasku, jeśli jest dostępna.</summary>
    public IReadOnlyDictionary<string, IRSourceSpan> NodeSources { get; }

    /// <summary>Diagnostyki powstałe podczas budowania grafu.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    private PipelineGraph(
        IReadOnlySet<string> nodes,
        IReadOnlyList<PipelineEdge> edges,
        IReadOnlyDictionary<string, IRSourceSpan> nodeSources,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        Nodes = nodes;
        Edges = edges;
        NodeSources = nodeSources;
        Diagnostics = diagnostics;
    }

    /// <summary>Zwraca unikalnych następców tasku.</summary>
    public IReadOnlyList<string> Successors(string node) =>
        [.. Edges.Where(edge => edge.From == node).Select(edge => edge.To).Distinct(StringComparer.Ordinal)];

    /// <summary>Buduje logiczny graf na podstawie Structural IR.</summary>
    public static PipelineGraph Build(IRDocument document)
    {
        var nodes = new HashSet<string>(document.Tasks.Select(task => task.Name), StringComparer.Ordinal);
        var edges = new List<PipelineEdge>();
        var diagnostics = new List<Diagnostic>();
        var nodeSources = new Dictionary<string, IRSourceSpan>(StringComparer.Ordinal);

        for (var taskIndex = 0; taskIndex < document.Tasks.Count; taskIndex++)
        {
            var source = FindSource(document, $"/Tasks[{taskIndex}]");
            if (source is not null)
                nodeSources[document.Tasks[taskIndex].Name] = source;
        }

        for (var pipelineIndex = 0; pipelineIndex < document.Pipelines.Count; pipelineIndex++)
        {
            var pipeline = document.Pipelines[pipelineIndex];
            CollectEdges(
                document,
                pipeline.Name,
                pipeline.Connections,
                $"/Pipelines[{pipelineIndex}]/Connections",
                nodes,
                edges,
                diagnostics);
        }

        return new PipelineGraph(nodes, edges, nodeSources, diagnostics);
    }

    private static void CollectEdges(
        IRDocument document,
        string pipelineName,
        IReadOnlyList<IRPipelineConn> connections,
        string path,
        IReadOnlySet<string> nodes,
        List<PipelineEdge> edges,
        List<Diagnostic> diagnostics)
    {
        for (var index = 0; index < connections.Count; index++)
        {
            var connection = connections[index];
            var connectionPath = $"{path}[{index}]";
            switch (connection)
            {
                case IRUseConn use:
                    var source = FindSource(document, connectionPath);
                    var sourceKnown = nodes.Contains(use.SourceTask);
                    if (!sourceKnown)
                        diagnostics.Add(UnknownTask(use.SourceTask, pipelineName, "source", source));

                    if (use.TargetTask is null)
                        break;

                    var targetKnown = nodes.Contains(use.TargetTask);
                    if (!targetKnown)
                        diagnostics.Add(UnknownTask(use.TargetTask, pipelineName, "target", source));

                    if (sourceKnown && targetKnown)
                    {
                        edges.Add(new PipelineEdge(
                            use.SourceTask,
                            use.SourcePort,
                            use.TargetTask,
                            use.TargetPort,
                            pipelineName,
                            source));
                    }
                    break;

                case IRParallelBlock parallel:
                    CollectEdges(
                        document,
                        pipelineName,
                        parallel.Connections,
                        $"{connectionPath}/Connections",
                        nodes,
                        edges,
                        diagnostics);
                    break;

                default:
                    diagnostics.Add(new Diagnostic(
                        "T0005",
                        DiagnosticLevel.Error,
                        "Topology",
                        $"Unsupported pipeline connection '{connection.GetType().Name}'.",
                        document.SourceFile));
                    break;
            }
        }
    }

    private static Diagnostic UnknownTask(string task, string pipeline, string endpoint, IRSourceSpan? source) =>
        new(
            "T0002",
            DiagnosticLevel.Error,
            "Topology",
            $"Pipeline '{pipeline}' references unknown {endpoint} task '{task}'.",
            source?.File,
            source?.Line ?? 0,
            source?.Column ?? 0);

    private static IRSourceSpan? FindSource(IRDocument document, string path) =>
        document.SourceMap.FirstOrDefault(entry => entry.Path == path)?.Source;
}
