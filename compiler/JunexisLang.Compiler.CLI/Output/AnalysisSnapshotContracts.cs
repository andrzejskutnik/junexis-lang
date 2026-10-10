using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Topology;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record AnalysisSnapshot(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("sourceFile")] string SourceFile,
    [property: JsonPropertyName("symbols")] IReadOnlyList<AnalysisSymbol> Symbols,
    [property: JsonPropertyName("relations")] IReadOnlyList<AnalysisRelation> Relations,
    [property: JsonPropertyName("semanticRelations")] IReadOnlyList<AnalysisSemanticRelation> SemanticRelations,
    [property: JsonPropertyName("topology")] AnalysisTopology Topology)
{
    internal const string CurrentVersion = "1.1";
}

internal sealed record AnalysisSymbol(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("source")] AnalysisSourceLocation? Source,
    [property: JsonPropertyName("platforms")] IReadOnlyList<string> Platforms,
    [property: JsonPropertyName("dataType")] string? DataType,
    [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, string> Properties);

internal sealed record AnalysisRelation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("communicationKind")] string CommunicationKind,
    [property: JsonPropertyName("pipelineSymbolId")] string PipelineSymbolId,
    [property: JsonPropertyName("sourceSymbolId")] string SourceSymbolId,
    [property: JsonPropertyName("sourcePort")] string? SourcePort,
    [property: JsonPropertyName("targetSymbolId")] string TargetSymbolId,
    [property: JsonPropertyName("targetPort")] string? TargetPort,
    [property: JsonPropertyName("source")] AnalysisSourceLocation? Source);

internal sealed record AnalysisSemanticRelation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("sourceSymbolId")] string SourceSymbolId,
    [property: JsonPropertyName("targetSymbolId")] string TargetSymbolId,
    [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, string> Properties,
    [property: JsonPropertyName("source")] AnalysisSourceLocation? Source);

internal sealed record AnalysisSourceLocation(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("column")] int Column);

internal sealed record AnalysisTopology(
    [property: JsonPropertyName("hasCycles")] bool HasCycles,
    [property: JsonPropertyName("maxDepth")] int MaxDepth,
    [property: JsonPropertyName("stronglyConnectedComponents")] IReadOnlyList<IReadOnlyList<string>> StronglyConnectedComponents,
    [property: JsonPropertyName("fanIn")] IReadOnlyDictionary<string, int> FanIn,
    [property: JsonPropertyName("fanOut")] IReadOnlyDictionary<string, int> FanOut,
    [property: JsonPropertyName("depth")] IReadOnlyDictionary<string, int> Depth);

internal static class AnalysisSnapshotWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string CreateJson(IRDocument document, string buildId)
    {
        var graph = PipelineGraph.Build(document);
        var topology = TopologyAnalyzer.Analyze(graph);
        var symbols = CreateSymbols(document);
        var semanticRelations = CreateSemanticRelations(document);
        var duplicateIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var relations = graph.Edges.Select(edge =>
        {
            var semanticId = StableSymbolIds.Communication(
                edge.Pipeline, edge.From, edge.FromPort, edge.To, edge.ToPort);
            duplicateIds.TryGetValue(semanticId, out var duplicateIndex);
            duplicateIds[semanticId] = duplicateIndex + 1;
            var id = duplicateIndex == 0 ? semanticId : $"{semanticId}#{duplicateIndex + 1}";
            return new AnalysisRelation(
                id,
                "communication",
                DebugManifestGenerator.CommunicationKind(document, edge),
                StableSymbolIds.Symbol("pipeline", edge.Pipeline),
                StableSymbolIds.Symbol("task", edge.From),
                edge.FromPort,
                StableSymbolIds.Symbol("task", edge.To),
                edge.ToPort,
                Source(edge.Source));
        }).ToArray();

        var snapshot = new AnalysisSnapshot(
            AnalysisSnapshot.CurrentVersion,
            buildId,
            Path.GetFullPath(document.SourceFile),
            symbols,
            relations,
            semanticRelations,
            new AnalysisTopology(
                topology.HasCycles,
                topology.MaxDepth,
                topology.SCCs.Select(component =>
                    (IReadOnlyList<string>)component.Select(name => StableSymbolIds.Symbol("task", name)).ToArray()).ToArray(),
                WithTaskIds(topology.FanIn),
                WithTaskIds(topology.FanOut),
                WithTaskIds(topology.Depth)));
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    internal static string ComputePreflightBuildId(
        IRDocument document,
        IReadOnlyList<string> targets)
    {
        var canonical = new StringBuilder();
        canonical.Append(NormalizeSourceForIdentity(File.ReadAllText(document.SourceFile))).Append('\n');
        foreach (var target in targets.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            canonical.Append("target:").Append(target.ToLowerInvariant()).Append('\n');
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string NormalizeSourceForIdentity(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static IReadOnlyList<AnalysisSymbol> CreateSymbols(IRDocument document)
    {
        var symbols = new List<AnalysisSymbol>();
        Add(document.Tasks, "/Tasks", "task", task => task.Name, task => task.Platforms.Select(platform => platform.Target), task => TypeName(task.OutputType));
        Add(document.Pipelines, "/Pipelines", "pipeline", pipeline => pipeline.Name);
        Add(document.Devices, "/Devices", "device", device => device.Name, device => [device.Platform.Target]);
        Add(document.Narratives, "/Narratives", "narrative", narrative => narrative.Label,
            properties: narrative => BodyProperties(narrative.RawBody));
        Add(document.Structs, "/Structs", "struct", value => value.Name);
        Add(document.Enums, "/Enums", "enum", value => value.Name);
        Add(document.Functions, "/Functions", "function", value => value.Name, dataType: value => TypeName(value.ReturnType));
        Add(document.Supervisors, "/Supervisors", "supervisor", value => value.Name,
            properties: value => BodyProperties(value.RawBody));
        Add(document.CircuitBreakers, "/CircuitBreakers", "circuit-breaker", value => value.Name,
            properties: value => BodyProperties(value.RawBody));
        Add(document.ExternTasks, "/ExternTasks", "extern-task", value => value.Name, value => value.Platforms.Select(platform => platform.Target), value => TypeName(value.OutputType));
        Add(document.ExternTypes, "/ExternTypes", "extern-type", value => value.Name, value => value.Platforms.Select(platform => platform.Target));
        Add(document.ExternContracts, "/ExternContracts", "extern-contract", value => value.Name,
            value => value.Target is null ? [] : [value.Target],
            properties: value => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ownership"] = "developer",
                ["role"] = value.Role,
                ["target"] = value.Target ?? string.Empty,
                ["language"] = value.Language ?? string.Empty,
                ["userFile"] = value.UserFile ?? string.Empty
            });

        foreach (var platform in document.Tasks.SelectMany(task => task.Platforms)
                     .Concat(document.Devices.Select(device => device.Platform))
                     .Concat(document.ExternTasks.SelectMany(task => task.Platforms))
                     .Concat(document.ExternTypes.SelectMany(type => type.Platforms))
                     .Select(platform => platform.Target)
                     .Where(platform => !string.IsNullOrWhiteSpace(platform))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            symbols.Add(new AnalysisSymbol(
                StableSymbolIds.Symbol("platform", platform),
                "platform",
                platform,
                null,
                [platform],
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
        return symbols;

        void Add<T>(
            IReadOnlyList<T> values,
            string path,
            string kind,
            Func<T, string> name,
            Func<T, IEnumerable<string>>? platforms = null,
            Func<T, string?>? dataType = null,
            Func<T, IReadOnlyDictionary<string, string>>? properties = null)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                var symbolName = name(value);
                symbols.Add(new AnalysisSymbol(
                    StableSymbolIds.Symbol(kind, symbolName),
                    kind,
                    symbolName,
                    Source(document.SourceMap.FirstOrDefault(entry => entry.Path == $"{path}[{index}]")?.Source),
                    platforms?.Invoke(value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                    dataType?.Invoke(value),
                    properties?.Invoke(value) ?? new Dictionary<string, string>(StringComparer.Ordinal)));
            }
        }
    }

    private static IReadOnlyList<AnalysisSemanticRelation> CreateSemanticRelations(IRDocument document)
    {
        var relations = new List<AnalysisSemanticRelation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pipelineTasks = document.Pipelines.ToDictionary(
            pipeline => pipeline.Name,
            pipeline => PipelineTaskNames(pipeline).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var (pipelineName, taskNames) in pipelineTasks)
        {
            foreach (var taskName in taskNames)
                Add("belongs-to-pipeline", "task", taskName, "pipeline", pipelineName);
        }

        foreach (var task in document.Tasks)
        {
            foreach (var platform in task.Platforms)
                Add("runs-on", "task", task.Name, "platform", platform.Target);
        }

        foreach (var narrative in document.Narratives)
        {
            var knownTargets = document.Tasks.Select(task => task.Name)
                .Concat(document.Pipelines.Select(pipeline => pipeline.Name))
                .Concat(document.Devices.Select(device => device.Name))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var target in ExtractReferences(narrative.RawBody, "applies_to", knownTargets))
            {
                var targetKind = document.Pipelines.Any(pipeline => pipeline.Name == target)
                    ? "pipeline"
                    : document.Devices.Any(device => device.Name == target) ? "device" : "task";
                Add("applies-to", "narrative", narrative.Label, targetKind, target);

                if (targetKind == "task")
                    Add("participates-in-narrative", "task", target, "narrative", narrative.Label);
                else if (targetKind == "pipeline" && pipelineTasks.TryGetValue(target, out var taskNames))
                    foreach (var taskName in taskNames)
                        Add("participates-in-narrative", "task", taskName, "narrative", narrative.Label);
            }
        }

        var knownTasks = document.Tasks.Select(task => task.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var supervisor in document.Supervisors)
            foreach (var taskName in ExtractReferences(supervisor.RawBody, "children", knownTasks))
                Add("supervised-by", "task", taskName, "supervisor", supervisor.Name);

        foreach (var policy in document.CircuitBreakers)
            foreach (var taskName in ExtractReferences(policy.RawBody, "protects", knownTasks))
                Add("protected-by", "task", taskName, "circuit-breaker", policy.Name);

        return relations;

        void Add(string kind, string sourceKind, string sourceName, string targetKind, string targetName)
        {
            var sourceId = StableSymbolIds.Symbol(sourceKind, sourceName);
            var targetId = StableSymbolIds.Symbol(targetKind, targetName);
            var id = $"{kind}:{sourceId}->{targetId}";
            if (!ids.Add(id))
                return;
            relations.Add(new AnalysisSemanticRelation(
                id,
                kind,
                sourceId,
                targetId,
                new Dictionary<string, string>(StringComparer.Ordinal),
                null));
        }
    }

    private static IEnumerable<string> PipelineTaskNames(IRPipeline pipeline) =>
        pipeline.Connections.SelectMany(TaskNames);

    private static IEnumerable<string> TaskNames(IRPipelineConn connection) => connection switch
    {
        IRUseConn use => new[] { use.SourceTask, use.TargetTask }.OfType<string>(),
        IRParallelBlock parallel => parallel.Connections.SelectMany(TaskNames),
        _ => []
    };

    private static IReadOnlyList<string> ExtractReferences(
        string rawBody,
        string property,
        IReadOnlySet<string> knownNames)
    {
        if (string.IsNullOrWhiteSpace(rawBody) || knownNames.Count == 0)
            return [];

        var propertyMatch = Regex.Match(
            rawBody,
            $@"{Regex.Escape(property)}\s*:?\s*",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!propertyMatch.Success)
            return [];

        var openingIndex = propertyMatch.Index + propertyMatch.Length;
        if (openingIndex >= rawBody.Length || rawBody[openingIndex] is not ('[' or '{'))
            return [];
        var opening = rawBody[openingIndex];
        var closing = opening == '[' ? ']' : '}';
        var depth = 0;
        var closingIndex = -1;
        for (var index = openingIndex; index < rawBody.Length; index++)
        {
            if (rawBody[index] == opening)
                depth++;
            else if (rawBody[index] == closing && --depth == 0)
            {
                closingIndex = index;
                break;
            }
        }
        if (closingIndex < 0)
            return [];

        var body = rawBody[(openingIndex + 1)..closingIndex];
        return knownNames
            .Where(name => body.Contains(name, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> BodyProperties(string rawBody)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rawBody"] = rawBody ?? string.Empty
        };
        var compliance = Regex.Match(
            rawBody ?? string.Empty,
            "\\bcompliance\\s*:?\\s*\"(?<value>[^\"]+)\"",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (compliance.Success)
            properties["compliance"] = compliance.Groups["value"].Value;
        var trace = Regex.Match(
            rawBody ?? string.Empty,
            "\\btrace\\s*:?\\s*(?<value>full|causal|minimal|none|off)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (trace.Success)
            properties["tracePolicy"] = trace.Groups["value"].Value;
        return properties;
    }

    private static IReadOnlyDictionary<string, int> WithTaskIds(IReadOnlyDictionary<string, int> values) =>
        values.ToDictionary(pair => StableSymbolIds.Symbol("task", pair.Key), pair => pair.Value, StringComparer.Ordinal);

    private static AnalysisSourceLocation? Source(IRSourceSpan? source) =>
        source is null ? null : new AnalysisSourceLocation(Path.GetFullPath(source.File), source.Line, source.Column);

    private static string? TypeName(IRType? type) => type switch
    {
        null => null,
        IRNamedType named => named.Name,
        IRCommType comm => $"{comm.Kind.ToString().ToLowerInvariant()}<{TypeName(comm.Element)}>",
        IRMapType map => $"Map<{TypeName(map.Key)}, {TypeName(map.Value)}>",
        IRGenericType generic => $"{generic.Name}<{string.Join(", ", generic.TypeArgs.Select(TypeName))}>",
        IRArrayType array => $"List<{TypeName(array.Element)}>",
        IRUnionType union => string.Join(" | ", union.Variants.Select(TypeName)),
        _ => type.GetType().Name
    };
}
