using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record DeploymentLifecycleReceipt(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("startedAtUtc")] string StartedAtUtc,
    [property: JsonPropertyName("endedAtUtc")] string EndedAtUtc,
    [property: JsonPropertyName("exitCode")] int? ExitCode,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("durationSeconds")] int DurationSeconds);

internal sealed record DeploymentTargetReceipt(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("details")] string Details);

internal sealed record DeploymentReceipt(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("packageReceiptPath")] string PackageReceiptPath,
    [property: JsonPropertyName("preflightReportPath")] string PreflightReportPath,
    [property: JsonPropertyName("workDirectory")] string WorkDirectory,
    [property: JsonPropertyName("targets")] IReadOnlyList<DeploymentTargetReceipt> Targets,
    [property: JsonPropertyName("lifecycle")] DeploymentLifecycleReceipt Lifecycle)
{
    internal const string CurrentVersion = "junexis.deployment-receipt/0.1";
}

internal static class DeploymentReceiptContract
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string DefaultPath(string preflightReportPath)
    {
        var directory = Path.GetDirectoryName(preflightReportPath)
            ?? throw new InvalidDataException("deployment preflight path has no parent directory");
        return Path.Combine(directory, "deployment-receipt.json");
    }

    internal static async Task WriteAtomicAsync(
        string path,
        DeploymentReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("deployment receipt path has no parent directory");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(receipt, JsonOptions);
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}