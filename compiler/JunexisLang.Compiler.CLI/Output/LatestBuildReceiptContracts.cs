using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record LatestBuildReceipt(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("projectRoot")] string ProjectRoot,
    [property: JsonPropertyName("entryFile")] string EntryFile,
    [property: JsonPropertyName("outputRoot")] string OutputRoot,
    [property: JsonPropertyName("targets")] IReadOnlyList<string> Targets,
    [property: JsonPropertyName("bundleManifest")] string BundleManifest,
    [property: JsonPropertyName("artifactManifest")] string ArtifactManifest,
    [property: JsonPropertyName("debugManifest")] string DebugManifest,
    [property: JsonPropertyName("analysisSnapshot")] string AnalysisSnapshot)
{
    internal const string CurrentVersion = "1.0";
}

internal static class LatestBuildReceiptContract
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static (string Path, string Json) Create(
        string buildId,
        string mode,
        string projectRoot,
        string entryFile,
        string outputRoot,
        IReadOnlyList<string> targets,
        string bundleManifest,
        string artifactManifest,
        string debugManifest,
        string analysisSnapshot)
    {
        var receipt = new LatestBuildReceipt(
            LatestBuildReceipt.CurrentVersion,
            buildId,
            mode,
            Path.GetFullPath(projectRoot),
            Path.GetFullPath(entryFile),
            Path.GetFullPath(outputRoot),
            targets,
            Path.GetFullPath(bundleManifest),
            Path.GetFullPath(artifactManifest),
            Path.GetFullPath(debugManifest),
            Path.GetFullPath(analysisSnapshot));
        Validate(receipt);

        return (GetPath(receipt.Mode, receipt.ProjectRoot, receipt.EntryFile),
            JsonSerializer.Serialize(receipt, JsonOptions));
    }

    internal static LatestBuildReceipt Parse(string json)
    {
        LatestBuildReceipt? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<LatestBuildReceipt>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("latest-build receipt is not valid JSON", ex);
        }

        if (receipt is null)
            throw new InvalidDataException("latest-build receipt is empty");

        Validate(receipt);
        return receipt;
    }

    internal static async Task WriteAtomicAsync(string receiptPath, string json)
    {
        var directory = Path.GetDirectoryName(receiptPath)
            ?? throw new InvalidDataException("latest-build receipt path has no parent directory");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(receiptPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, receiptPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string GetPath(string mode, string projectRoot, string entryFile)
    {
        var stateRoot = Path.Combine(projectRoot, ".junexis", "state");
        if (mode == "project")
            return Path.Combine(stateRoot, "latest-build.json");

        var entryName = Path.GetFileNameWithoutExtension(entryFile);
        if (string.IsNullOrWhiteSpace(entryName))
            throw new InvalidDataException("file-mode entry must have a non-empty file name");
        return Path.Combine(stateRoot, entryName, "latest-build.json");
    }

    private static void Validate(LatestBuildReceipt receipt)
    {
        if (!string.Equals(
                receipt.SchemaVersion,
                LatestBuildReceipt.CurrentVersion,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"unsupported latest-build schema '{receipt.SchemaVersion}'; expected {LatestBuildReceipt.CurrentVersion}");
        if (string.IsNullOrWhiteSpace(receipt.BuildId) ||
            receipt.BuildId.Length != 64 ||
            receipt.BuildId.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("latest-build buildId must be a 64-character hexadecimal SHA-256");
        if (receipt.Mode is not ("project" or "file"))
            throw new InvalidDataException("latest-build mode must be 'project' or 'file'");
        if (receipt.Targets is null ||
            receipt.Targets.Count == 0 ||
            receipt.Targets.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("latest-build targets must contain at least one named target");

        RequireAbsoluteDirectory(receipt.ProjectRoot, "projectRoot");
        RequireAbsoluteFile(receipt.EntryFile, "entryFile");
        RequireAbsoluteDirectory(receipt.OutputRoot, "outputRoot");
        RequireChild(receipt.ProjectRoot, receipt.EntryFile, "entryFile");
        RequireChild(receipt.OutputRoot, receipt.BundleManifest, "bundleManifest");
        RequireChild(receipt.OutputRoot, receipt.ArtifactManifest, "artifactManifest");
        RequireChild(receipt.OutputRoot, receipt.DebugManifest, "debugManifest");
        RequireChild(receipt.OutputRoot, receipt.AnalysisSnapshot, "analysisSnapshot");
    }

    private static void RequireAbsoluteDirectory(string path, string property)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException($"latest-build {property} must be an absolute path");
    }

    private static void RequireAbsoluteFile(string path, string property)
    {
        RequireAbsoluteDirectory(path, property);
        if (Path.EndsInDirectorySeparator(path))
            throw new InvalidDataException($"latest-build {property} must identify a file");
    }

    private static void RequireChild(string root, string path, string property)
    {
        RequireAbsoluteFile(path, property);
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (relative == "." ||
            relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relative))
        {
            throw new InvalidDataException($"latest-build {property} must stay inside its owning root");
        }
    }
}
