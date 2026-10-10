namespace JunexisLang.Compiler.Topology;

using System.Text.RegularExpressions;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>A protocol endpoint lowered from an opaque 0.2 protocol declaration.</summary>
public sealed record ProtocolBinding(
    string Declaration,
    string Protocol,
    string? Method,
    string? Path,
    string Task,
    IReadOnlyDictionary<string, string> ParameterSources);

/// <summary>A concrete task placement lowered from deployment or inferred from its platform.</summary>
public sealed record TaskPlacement(
    string Deployment,
    string Task,
    string Platform,
    int Instances,
    bool Explicit);

/// <summary>A transport selected for one logical pipeline edge.</summary>
public sealed record DistributedEdge(
    string Pipeline,
    string SourceTask,
    string TargetTask,
    string SourcePlatform,
    string TargetPlatform,
    IRCommKind? CommunicationKind,
    string Protocol,
    string Encoding,
    string WireVersion);

/// <summary>Distributed Topology IR used by M4d.</summary>
public sealed record DistributedTopologyPlan(
    IReadOnlyList<ProtocolBinding> ProtocolBindings,
    IReadOnlyList<TaskPlacement> Placements,
    IReadOnlyList<DistributedEdge> Edges);

/// <summary>Result of semantic lowering for protocol, deployment and platform boundaries.</summary>
public sealed record DistributedTopologyResult(
    bool Success,
    DistributedTopologyPlan Plan,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Lowers the executable 0.2 subset of opaque protocol/deployment bodies.
/// Unknown opaque fields are preserved in Structural IR but do not silently become executable.
/// </summary>
public static partial class DistributedTopologyLowerer
{
    [GeneratedRegex(@"(?im)\b(GET|POST|PUT|PATCH|DELETE)\s*\(\s*""([^""]+)""\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)")]
    private static partial Regex RestFunctionBindingRegex();

    [GeneratedRegex(@"(?im)\b(GET|POST|PUT|PATCH|DELETE)\s*(.+?)\s*->\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)")]
    private static partial Regex RestArrowBindingRegex();

    [GeneratedRegex(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline)]
    private static partial Regex CallLikeRegex();

    [GeneratedRegex(@"\btarget\s*(embedded|server|report)\s*\{", RegexOptions.IgnoreCase)]
    private static partial Regex TargetBlockRegex();

    [GeneratedRegex(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*@(embedded|server|report)(?:\([^)]*\))?\s*\{", RegexOptions.IgnoreCase)]
    private static partial Regex PlacementRegex();

    [GeneratedRegex(@"\b(?:repliki|replicas|instancje|instances)\s*:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex InstanceCountRegex();

    [GeneratedRegex(@"\b(path|query|body|header)\s*([A-Za-z_][A-Za-z0-9_]*)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ProtocolParameterRegex();

    /// <summary>Builds Distributed Topology IR and validates task references.</summary>
    public static DistributedTopologyResult Lower(IRDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = new List<Diagnostic>();
        var taskNames = document.Tasks.Select(task => task.Name).ToHashSet(StringComparer.Ordinal);
        var protocolBindings = LowerProtocols(document, taskNames, diagnostics);
        var placements = LowerPlacements(document, taskNames, diagnostics);
        var edges = LowerEdges(document);
        var plan = new DistributedTopologyPlan(protocolBindings, placements, edges);
        return new DistributedTopologyResult(
            !diagnostics.Any(diagnostic => diagnostic.Level == DiagnosticLevel.Error),
            plan,
            diagnostics);
    }

    private static List<ProtocolBinding> LowerProtocols(
        IRDocument document,
        IReadOnlySet<string> taskNames,
        List<Diagnostic> diagnostics)
    {
        var bindings = new List<ProtocolBinding>();
        for (var index = 0; index < document.Protocols.Count; index++)
        {
            var declaration = document.Protocols[index];
            var protocol = NormalizeProtocol(declaration.Name);
            var seen = new HashSet<(string? Method, string? Path, string Task)>();

            foreach (Match match in RestFunctionBindingRegex().Matches(declaration.RawBody))
                AddProtocolBinding(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value);
            foreach (Match match in RestArrowBindingRegex().Matches(declaration.RawBody))
                AddProtocolBinding(match.Groups[1].Value, match.Groups[2].Value.Trim().Trim('"'), match.Groups[3].Value, match.Groups[4].Value);

            if (protocol != "rest")
            {
                foreach (Match match in CallLikeRegex().Matches(declaration.RawBody))
                {
                    var task = match.Groups[1].Value;
                    if (taskNames.Contains(task))
                        AddProtocolBinding(null, null, task, string.Empty);
                }
            }

            void AddProtocolBinding(string? method, string? path, string task, string arguments)
            {
                if (!taskNames.Contains(task))
                {
                    diagnostics.Add(At(document, $"/Protocols[{index}]", "D6001", DiagnosticLevel.Error,
                        $"Protocol '{declaration.Name}' references unknown task '{task}'."));
                    return;
                }

                var key = (method?.ToUpperInvariant(), path, task);
                if (seen.Add(key))
                {
                    var parameterSources = ProtocolParameterRegex().Matches(arguments)
                        .ToDictionary(
                            match => match.Groups[2].Value,
                            match => match.Groups[1].Value.ToLowerInvariant(),
                            StringComparer.Ordinal);
                    bindings.Add(new ProtocolBinding(declaration.Name, protocol, key.Item1, path, task, parameterSources));
                }
            }
        }
        return bindings;
    }

    private static List<TaskPlacement> LowerPlacements(
        IRDocument document,
        IReadOnlySet<string> taskNames,
        List<Diagnostic> diagnostics)
    {
        var placements = new List<TaskPlacement>();
        var explicitlyPlaced = new HashSet<string>(StringComparer.Ordinal);

        for (var deploymentIndex = 0; deploymentIndex < document.Deployments.Count; deploymentIndex++)
        {
            var deployment = document.Deployments[deploymentIndex];
            var deploymentName = deployment.Label ?? "default";
            foreach (Match targetMatch in TargetBlockRegex().Matches(deployment.RawBody))
            {
                var platform = targetMatch.Groups[1].Value.ToLowerInvariant();
                var openBrace = deployment.RawBody.IndexOf('{', targetMatch.Index);
                var closeBrace = FindMatchingBrace(deployment.RawBody, openBrace);
                if (closeBrace < 0)
                    continue;
                var body = deployment.RawBody[(openBrace + 1)..closeBrace];
                foreach (Match placementMatch in PlacementRegex().Matches(body))
                {
                    var task = placementMatch.Groups[1].Value;
                    var annotatedPlatform = placementMatch.Groups[2].Value.ToLowerInvariant();
                    if (!taskNames.Contains(task))
                    {
                        diagnostics.Add(At(document, $"/Deployments[{deploymentIndex}]", "D6101", DiagnosticLevel.Error,
                            $"Deployment '{deploymentName}' references unknown task '{task}'."));
                        continue;
                    }
                    if (annotatedPlatform != platform)
                    {
                        diagnostics.Add(At(document, $"/Deployments[{deploymentIndex}]", "D6102", DiagnosticLevel.Error,
                            $"Deployment target '{platform}' contains placement '{task}' for platform '{annotatedPlatform}'."));
                        continue;
                    }

                    var placementOpen = body.IndexOf('{', placementMatch.Index);
                    var placementClose = FindMatchingBrace(body, placementOpen);
                    var placementBody = placementClose < 0 ? string.Empty : body[(placementOpen + 1)..placementClose];
                    var countMatch = InstanceCountRegex().Match(placementBody);
                    var instances = countMatch.Success ? int.Parse(countMatch.Groups[1].Value) : 1;
                    placements.Add(new TaskPlacement(deploymentName, task, platform, instances, Explicit: true));
                    explicitlyPlaced.Add(task);
                }
            }
        }

        var inferredDeployment = document.Deployments.FirstOrDefault()?.Label ?? "implicit";
        foreach (var task in document.Tasks.Where(task => !explicitlyPlaced.Contains(task.Name)))
        {
            foreach (var platform in task.Platforms.Select(platform => platform.Target).Distinct(StringComparer.Ordinal))
                placements.Add(new TaskPlacement(inferredDeployment, task.Name, platform, 1, Explicit: false));
        }
        return placements;
    }

    private static List<DistributedEdge> LowerEdges(IRDocument document)
    {
        var tasks = document.Tasks.ToDictionary(task => task.Name, StringComparer.Ordinal);
        var edges = new List<DistributedEdge>();
        foreach (var pipeline in document.Pipelines)
        foreach (var use in Flatten(pipeline.Connections).OfType<IRUseConn>())
        {
            if (use.TargetTask is null || !tasks.TryGetValue(use.SourceTask, out var source) || !tasks.TryGetValue(use.TargetTask, out var target))
                continue;
            var sourcePlatform = source.Platforms.FirstOrDefault()?.Target ?? "local";
            var targetPlatform = target.Platforms.FirstOrDefault()?.Target ?? "local";
            var communicationKind = ResolveCommunicationKind(source, target, use.TargetPort);
            var (protocol, encoding) = SelectBootstrapTransport(sourcePlatform, targetPlatform, communicationKind);
            edges.Add(new DistributedEdge(
                pipeline.Name, source.Name, target.Name, sourcePlatform, targetPlatform,
                communicationKind, protocol, encoding, WireRuntimeContractCatalog.Json.Version));
        }
        return edges;
    }

    private static IRCommKind? ResolveCommunicationKind(IRTask source, IRTask target, string? targetPort)
    {
        if (source.OutputType is IRCommType output)
            return output.Kind;
        var parameter = targetPort is null
            ? target.Params.FirstOrDefault()
            : target.Params.FirstOrDefault(candidate => candidate.Name == targetPort);
        return parameter?.Type is IRCommType input ? input.Kind : null;
    }

    private static (string Protocol, string Encoding) SelectBootstrapTransport(
        string source,
        string target,
        IRCommKind? kind) => (source, target, kind) switch
    {
        ("embedded", "server", _) => ("tcp", "json"),
        ("server", "report", IRCommKind.Chan) => ("websocket", "messagepack"),
        ("server", "report", _) => ("http", "json"),
        (var left, var right, _) when left == right => ("in-process", "native"),
        _ => ("http", "json")
    };

    private static string NormalizeProtocol(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("rest", StringComparison.Ordinal)) return "rest";
        if (normalized.Contains("grpc", StringComparison.Ordinal)) return "grpc";
        if (normalized is "ws" || normalized.Contains("websocket", StringComparison.Ordinal)) return "websocket";
        if (normalized.Contains("mqtt", StringComparison.Ordinal)) return "mqtt";
        if (normalized.Contains("coap", StringComparison.Ordinal)) return "coap";
        if (normalized.Contains("amqp", StringComparison.Ordinal)) return "amqp";
        if (normalized.Contains("kafka", StringComparison.Ordinal)) return "kafka";
        return normalized;
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        if (openBrace < 0) return -1;
        var depth = 0;
        var quoted = false;
        for (var index = openBrace; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"' && (index == 0 || text[index - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (character == '{') depth++;
            else if (character == '}' && --depth == 0) return index;
        }
        return -1;
    }

    private static IEnumerable<IRPipelineConn> Flatten(IReadOnlyList<IRPipelineConn> connections)
    {
        foreach (var connection in connections)
        {
            if (connection is IRParallelBlock parallel)
                foreach (var nested in Flatten(parallel.Connections)) yield return nested;
            else
                yield return connection;
        }
    }

    private static Diagnostic At(
        IRDocument document,
        string path,
        string code,
        DiagnosticLevel level,
        string message)
    {
        var source = document.SourceMap.FirstOrDefault(entry => entry.Path == path)?.Source;
        return new Diagnostic(code, level, "DistributedTopology", message,
            source?.File ?? document.SourceFile, source?.Line ?? 0, source?.Column ?? 0);
    }
}
