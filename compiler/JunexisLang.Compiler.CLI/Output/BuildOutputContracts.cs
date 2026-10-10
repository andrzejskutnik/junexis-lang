using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record CliBuildOutputEnvelope(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("inputFile")] string InputFile,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<CliDiagnosticDto> Diagnostics,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("projectRoot")] string ProjectRoot,
    [property: JsonPropertyName("entryFile")] string EntryFile,
    [property: JsonPropertyName("outputRoot")] string? OutputRoot,
    [property: JsonPropertyName("buildId")] string? BuildId,
    [property: JsonPropertyName("targets")] IReadOnlyList<string> Targets,
    [property: JsonPropertyName("requestedTargets")] IReadOnlyList<string> RequestedTargets,
    [property: JsonPropertyName("applicableTargets")] IReadOnlyList<string> ApplicableTargets,
    [property: JsonPropertyName("builtTargets")] IReadOnlyList<string> BuiltTargets,
    [property: JsonPropertyName("targetSelectionSource")] string TargetSelectionSource,
    [property: JsonPropertyName("bundleManifest")] string? BundleManifest,
    [property: JsonPropertyName("artifactManifest")] string? ArtifactManifest,
    [property: JsonPropertyName("debugManifest")] string? DebugManifest,
    [property: JsonPropertyName("analysisSnapshot")] string? AnalysisSnapshot,
    [property: JsonPropertyName("latestBuildReceipt")] string? LatestBuildReceipt)
{
    internal const string CurrentVersion = "2.0";
}

internal sealed record ArtifactInventory(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("artifacts")] IReadOnlyList<ArtifactInventoryEntry> Artifacts)
{
    internal const string CurrentVersion = "1.0";
}

internal sealed record ArtifactInventoryEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("ownership")] string Ownership,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record AnalysisBundleManifest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("projectRoot")] string ProjectRoot,
    [property: JsonPropertyName("entryFile")] string EntryFile,
    [property: JsonPropertyName("targets")] IReadOnlyList<string> Targets,
    [property: JsonPropertyName("requestedTargets")] IReadOnlyList<string> RequestedTargets,
    [property: JsonPropertyName("applicableTargets")] IReadOnlyList<string> ApplicableTargets,
    [property: JsonPropertyName("builtTargets")] IReadOnlyList<string> BuiltTargets,
    [property: JsonPropertyName("targetSelectionSource")] string TargetSelectionSource,
    [property: JsonPropertyName("artifactManifest")] string ArtifactManifest,
    [property: JsonPropertyName("debugManifest")] string DebugManifest,
    [property: JsonPropertyName("analysisSnapshot")] string AnalysisSnapshot)
{
    internal const string CurrentVersion = "1.0";
}

internal sealed record BuildOutputArtifacts(
    string BuildId,
    string InventoryPath,
    string BundlePath,
    string DebugManifestPath,
    string AnalysisSnapshotPath,
    string LatestBuildReceiptPath,
    string InventoryJson,
    string BundleJson,
    string AnalysisSnapshotJson,
    string LatestBuildReceiptJson);

internal static class BuildOutputContractWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static BuildOutputArtifacts Create(
        string outputRoot,
        string projectRoot,
        string entryFile,
        string mode,
        IReadOnlyList<string> requestedTargets,
        IReadOnlyList<string> applicableTargets,
        IReadOnlyList<string> builtTargets,
        string targetSelectionSource,
        IReadOnlyList<GeneratedFile> files,
        IReadOnlySet<string> userOwnedPaths,
        IRDocument document,
        string buildId)
    {
        var normalizedFiles = files
            .OrderBy(file => Normalize(file.FilePath), StringComparer.Ordinal)
            .ToArray();
        const string analysisRelativePath = "analysis/junexis.analysis.json";
        var analysisJson = AnalysisSnapshotWriter.CreateJson(document, buildId);
        var entries = normalizedFiles.Select(file => new ArtifactInventoryEntry(
            Normalize(file.FilePath),
            file.Platform,
            Classify(file.FilePath),
            userOwnedPaths.Contains(Normalize(file.FilePath)) ? "user-overlay" : "generated",
            Hash(file.Content))).Append(new ArtifactInventoryEntry(
                analysisRelativePath,
                "analysis",
                "analysis-snapshot",
                "generated",
                Hash(analysisJson))).ToArray();

        var inventoryPath = Path.Combine(outputRoot, "junexis.artifacts.json");
        var bundlePath = Path.Combine(outputRoot, "junexis.bundle.json");
        const string debugRelativePath = "debug/junexis.debug.toml";
        var debugManifestPath = Path.Combine(
            outputRoot, debugRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var analysisSnapshotPath = Path.Combine(
            outputRoot, analysisRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var inventory = new ArtifactInventory(ArtifactInventory.CurrentVersion, buildId, entries);
        var bundle = new AnalysisBundleManifest(
            AnalysisBundleManifest.CurrentVersion,
            buildId,
            mode,
            Path.GetFullPath(projectRoot),
            Path.GetFullPath(entryFile),
            builtTargets,
            requestedTargets,
            applicableTargets,
            builtTargets,
            targetSelectionSource,
            "junexis.artifacts.json",
            debugRelativePath,
            analysisRelativePath);
        var receipt = LatestBuildReceiptContract.Create(
            buildId,
            mode,
            projectRoot,
            entryFile,
            outputRoot,
            builtTargets,
            bundlePath,
            inventoryPath,
            debugManifestPath,
            analysisSnapshotPath);

        return new BuildOutputArtifacts(
            buildId,
            inventoryPath,
            bundlePath,
            debugManifestPath,
            analysisSnapshotPath,
            receipt.Path,
            JsonSerializer.Serialize(inventory, JsonOptions),
            JsonSerializer.Serialize(bundle, JsonOptions),
            analysisJson,
            receipt.Json);
    }

    internal static string BuildJson(
        bool success,
        long durationMs,
        string inputFile,
        IReadOnlyList<Diagnostic> diagnostics,
        string mode,
        string projectRoot,
        string entryFile,
        string? outputRoot,
        IReadOnlyList<string> targets,
        BuildOutputArtifacts? artifacts,
        IReadOnlyList<string> requestedTargets,
        IReadOnlyList<string> applicableTargets,
        IReadOnlyList<string> builtTargets,
        string targetSelectionSource,
        string command = "build",
        string? buildId = null,
        string? analysisSnapshot = null)
    {
        var envelope = new CliBuildOutputEnvelope(
            CliBuildOutputEnvelope.CurrentVersion,
            command,
            success,
            durationMs,
            inputFile,
            diagnostics.Select(CliDiagnosticDto.From).ToArray(),
            mode,
            Path.GetFullPath(projectRoot),
            Path.GetFullPath(entryFile),
            outputRoot is null ? null : Path.GetFullPath(outputRoot),
            artifacts?.BuildId ?? buildId,
            targets,
            requestedTargets,
            applicableTargets,
            builtTargets,
            targetSelectionSource,
            artifacts?.BundlePath,
            artifacts?.InventoryPath,
            artifacts?.DebugManifestPath,
            artifacts?.AnalysisSnapshotPath ?? analysisSnapshot,
            success ? artifacts?.LatestBuildReceiptPath : null);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private static string Hash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string Classify(string path)
    {
        var normalized = Normalize(path);
        if (normalized.Equals("debug/junexis.debug.toml", StringComparison.OrdinalIgnoreCase))
            return "debug-manifest";
        if (normalized.Equals("debug/junexis.source-map.json", StringComparison.OrdinalIgnoreCase))
            return "debug-source-map";
        return Path.GetExtension(normalized).ToLowerInvariant() switch
        {
            ".cpp" or ".c" or ".h" or ".hpp" or ".go" or ".ts" => "source",
            ".json" or ".toml" or ".ini" or ".mod" => "configuration",
            ".proto" => "schema",
            _ => "artifact"
        };
    }
}
