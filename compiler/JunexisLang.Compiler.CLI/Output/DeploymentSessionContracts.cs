using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record DeploymentSessionOwner(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("processId")] int ProcessId,
    [property: JsonPropertyName("instanceId")] string InstanceId,
    [property: JsonPropertyName("leaseExpiresAtUtc")] string LeaseExpiresAtUtc);

internal sealed record DeploymentSessionTarget(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("details")] string Details,
    [property: JsonPropertyName("processId")] int? ProcessId,
    [property: JsonPropertyName("runtimeUrl")] string? RuntimeUrl,
    [property: JsonPropertyName("healthUrl")] string? HealthUrl,
    [property: JsonPropertyName("processStartedAtUtc")] string? ProcessStartedAtUtc = null,
    [property: JsonPropertyName("processExecutablePath")] string? ProcessExecutablePath = null);

internal sealed record DeploymentSession(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("packageReceiptPath")] string PackageReceiptPath,
    [property: JsonPropertyName("preflightReportPath")] string PreflightReportPath,
    [property: JsonPropertyName("workDirectory")] string WorkDirectory,
    [property: JsonPropertyName("requestedDurationSeconds")] int RequestedDurationSeconds,
    [property: JsonPropertyName("startedAtUtc")] string StartedAtUtc,
    [property: JsonPropertyName("lastHeartbeatUtc")] string LastHeartbeatUtc,
    [property: JsonPropertyName("lastUpdatedAtUtc")] string LastUpdatedAtUtc,
    [property: JsonPropertyName("owner")] DeploymentSessionOwner Owner,
    [property: JsonPropertyName("targets")] IReadOnlyList<DeploymentSessionTarget> Targets)
{
    internal const string CurrentVersion = "junexis.deployment-session/0.2";
}

internal static class DeploymentSessionContract
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal static string DefaultPath(string preflightReportPath)
    {
        var directory = Path.GetDirectoryName(preflightReportPath)
            ?? throw new InvalidDataException("deployment preflight path has no parent directory");
        return Path.Combine(directory, "deployment-session.json");
    }

    internal static async Task<DeploymentSession> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var session = await JsonSerializer.DeserializeAsync<DeploymentSession>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Deployment session is empty.");
        if (!string.Equals(session.SchemaVersion, DeploymentSession.CurrentVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported deployment session schema '{session.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(session.SessionId))
            throw new InvalidDataException("Deployment session is missing sessionId.");
        if (string.IsNullOrWhiteSpace(session.State))
            throw new InvalidDataException("Deployment session is missing state.");
        if (session.Targets.Count == 0)
            throw new InvalidDataException("Deployment session has no targets.");
        if (string.IsNullOrWhiteSpace(session.BuildId) || string.IsNullOrWhiteSpace(session.BundleId))
            throw new InvalidDataException("Deployment session is missing build or bundle identity.");
        if (!Path.IsPathFullyQualified(session.PackageReceiptPath)
            || !Path.IsPathFullyQualified(session.PreflightReportPath)
            || !Path.IsPathFullyQualified(session.WorkDirectory))
        {
            throw new InvalidDataException("Deployment session paths must be absolute.");
        }
        foreach (var target in session.Targets)
        {
            if (target.Target is not ("server" or "report"))
                throw new InvalidDataException($"Deployment session contains unsupported target '{target.Target}'.");
            if (target.ProcessId is <= 0)
                throw new InvalidDataException("Deployment session contains an invalid target processId.");
            if (target.ProcessId is not null
                && (string.IsNullOrWhiteSpace(target.ProcessStartedAtUtc)
                    || string.IsNullOrWhiteSpace(target.ProcessExecutablePath)
                    || !Path.IsPathFullyQualified(target.ProcessExecutablePath)))
            {
                throw new InvalidDataException("Deployment session target process identity is incomplete.");
            }
            if (!string.IsNullOrWhiteSpace(target.HealthUrl) && !IsAllowedHealthUrl(target.HealthUrl))
                throw new InvalidDataException("Deployment session healthUrl must use loopback HTTP and /health/ready.");
        }
        return session;
    }

    private static bool IsAllowedHealthUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.IsLoopback
        && string.Equals(uri.AbsolutePath, "/health/ready", StringComparison.Ordinal);

    internal static async Task WriteAtomicAsync(
        string path,
        DeploymentSession session,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("deployment session path has no parent directory");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(session, JsonOptions);
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
