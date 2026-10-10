using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record DeploymentPreflightReport(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("technicalStatus")] string TechnicalStatus,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("targets")] IReadOnlyList<string> Targets,
    [property: JsonPropertyName("checks")] IReadOnlyList<DeploymentPreflightCheck> Checks,
    [property: JsonPropertyName("mentor")] DeploymentMentorOpinion Mentor)
{
    [JsonInclude, JsonPropertyName("authenticity")]
    internal SystemBundleAuthenticity Authenticity { get; init; } = null!;

    internal const string CurrentVersion = "junexis.deployment-preflight/0.2";
}

internal sealed record DeploymentPreflightCheck(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("message")] string Message);

internal sealed record DeploymentMentorOpinion(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("buildId")] string? BuildId,
    [property: JsonPropertyName("nonBlocking")] bool NonBlocking,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("findings")] IReadOnlyList<DeploymentMentorFinding> Findings);

internal sealed record DeploymentMentorFinding(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("relatedSymbolId")] string? RelatedSymbolId,
    [property: JsonPropertyName("recommendedAction")] string? RecommendedAction);

internal static class DeploymentPreflightContract
{
    private const int MaximumMentorOutputCharacters = 16 * 1024 * 1024;
    private const int MaximumReportBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan MentorTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<DeploymentPreflightReport> EvaluateAsync(
        string bundlePath,
        string? receiptPath,
        string[]? requestedTargets,
        string? mentorExecutable,
        string authenticityPolicy = "permissive",
        string? signaturePath = null,
        string[]? trustedSignerFingerprints = null,
        CancellationToken cancellationToken = default)
    {
        var bundle = await SystemBundleContract.VerifyAsync(
            bundlePath,
            cancellationToken).ConfigureAwait(false);
        var targets = ResolveTargets(bundle, requestedTargets);
        var checks = new List<DeploymentPreflightCheck>
        {
            Pass("DP0001", null, "System Bundle integrity and inventory verified offline.")
        };
        var authenticity = await SystemBundleAuthenticityContract.InspectAsync(
            bundle.Path,
            signaturePath,
            authenticityPolicy,
            trustedSignerFingerprints,
            cancellationToken).ConfigureAwait(false);
        checks.Add(authenticity.PolicySatisfied
            ? Pass(
                "DP0005",
                null,
                $"System Bundle authenticity '{authenticity.Status}' satisfies policy '{authenticity.Policy}'.")
            : Block(
                "DP1006",
                null,
                $"System Bundle authenticity '{authenticity.Status}' does not satisfy policy '{authenticity.Policy}'."));

        var effectiveReceiptPath = string.IsNullOrWhiteSpace(receiptPath)
            ? PackageReceiptContract.DefaultPath(bundle.Path)
            : Path.GetFullPath(receiptPath);
        PackageReceipt? receipt = null;
        try
        {
            receipt = await PackageReceiptContract.ReadAsync(
                effectiveReceiptPath,
                cancellationToken).ConfigureAwait(false);
            PackageReceiptContract.ValidateForBundle(receipt, bundle);
            checks.Add(Pass(
                "DP0002",
                null,
                "Package receipt matches Bundle ID, Build ID, targets, and passed native gates."));
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            receipt = null;
            checks.Add(Block(
                "DP1001",
                null,
                $"No matching M7.2 package evidence: {ex.Message}"));
        }

        var artifactPaths = bundle.Descriptor.Artifacts
            .Select(artifact => artifact.Path)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!bundle.Descriptor.Targets.Contains(target, StringComparer.Ordinal))
            {
                checks.Add(Block(
                    "DP1002",
                    target,
                    $"Target '{target}' is not present in this System Bundle."));
                continue;
            }
            if (receipt is not null
                && !receipt.Gates.Any(gate =>
                    gate.Target == target
                    && gate.Kind == "native-build"
                    && gate.Status == "passed"))
            {
                checks.Add(Block(
                    "DP1003",
                    target,
                    $"Target '{target}' has no passed native gate in the package receipt."));
            }
            AddArtifactChecks(target, artifactPaths, checks);
        }

        checks.Add(artifactPaths.Contains("artifacts/deployment/deployment-plan.json")
            ? Pass("DP0004", null, "Bundle contains an explicit deployment plan.")
            : Info(
                "DP2001",
                null,
                "Bundle 0.1 has no explicit deployment plan; target-local defaults apply."));

        var mentor = await ReviewWithMentorAsync(
            bundle,
            mentorExecutable,
            cancellationToken).ConfigureAwait(false);
        var technicalStatus = checks.Any(check => check.Status == "blocked")
            ? "blocked"
            : "ready";
        return new DeploymentPreflightReport(
            DeploymentPreflightReport.CurrentVersion,
            technicalStatus,
            bundle.BundleId,
            bundle.Descriptor.BuildId,
            targets,
            checks,
            mentor)
        {
            Authenticity = authenticity
        };
    }

    internal static string Serialize(DeploymentPreflightReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    internal static async Task<DeploymentPreflightReport> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists)
            throw new FileNotFoundException(
                "Deployment preflight report was not found.",
                file.FullName);
        if (file.Length > MaximumReportBytes)
            throw new InvalidDataException(
                "Deployment preflight report exceeds its 4 MiB safety limit.");
        DeploymentPreflightReport? report;
        try
        {
            report = JsonSerializer.Deserialize<DeploymentPreflightReport>(
                await File.ReadAllTextAsync(
                    file.FullName,
                    cancellationToken).ConfigureAwait(false),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Deployment preflight report contains invalid JSON.",
                ex);
        }
        if (report is null)
            throw new InvalidDataException("Deployment preflight report is empty.");
        Validate(report);
        return report;
    }

    internal static async Task WriteAtomicAsync(
        string path,
        DeploymentPreflightReport report,
        CancellationToken cancellationToken = default)
    {
        var json = Serialize(report);
        if (Encoding.UTF8.GetByteCount(json) > MaximumReportBytes)
            throw new InvalidDataException(
                "Deployment preflight report exceeds its 4 MiB safety limit.");
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException(
                "Deployment preflight report path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string[] ResolveTargets(
        SystemBundleInspection bundle,
        string[]? requestedTargets)
    {
        var targets = requestedTargets is { Length: > 0 }
            ? requestedTargets
            : bundle.Descriptor.Targets.ToArray();
        var normalized = targets
            .Select(target => target.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();
        var unsupported = normalized.Where(
            target => target is not ("embedded" or "server" or "report")).ToArray();
        if (normalized.Length == 0 || unsupported.Length > 0)
            throw new ArgumentException(
                $"Unsupported deployment target(s): {string.Join(", ", unsupported)}");
        return normalized;
    }

    private static void Validate(DeploymentPreflightReport report)
    {
        if (report.SchemaVersion != DeploymentPreflightReport.CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported deployment preflight schema '{report.SchemaVersion}'.");
        if (report.TechnicalStatus is not ("ready" or "blocked"))
            throw new InvalidDataException(
                "Deployment preflight technicalStatus must be ready or blocked.");
        ValidateHash(report.BundleId, "bundleId");
        ValidateHash(report.BuildId, "buildId");
        if (report.Targets is null
            || report.Targets.Count == 0
            || report.Targets.Count > 32
            || report.Targets.Any(target =>
                target is not ("embedded" or "server" or "report"))
            || report.Targets.Distinct(StringComparer.Ordinal).Count()
                != report.Targets.Count
            || !report.Targets.SequenceEqual(
                report.Targets.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
            throw new InvalidDataException(
                "Deployment preflight targets must be known, unique, and sorted.");
        if (report.Checks is null
            || report.Checks.Count == 0
            || report.Checks.Count > 256
            || report.Checks.Any(check =>
                check.Status is not ("passed" or "blocked" or "info")
                || string.IsNullOrWhiteSpace(check.Code)
                || string.IsNullOrWhiteSpace(check.Message)))
            throw new InvalidDataException(
                "Deployment preflight checks are missing or invalid.");
        if ((report.TechnicalStatus == "ready")
            != report.Checks.All(check => check.Status != "blocked"))
            throw new InvalidDataException(
                "Deployment preflight status is inconsistent with its checks.");
        if (report.Mentor is null || !report.Mentor.NonBlocking)
            throw new InvalidDataException(
                "Deployment preflight Mentor opinion must remain explicitly non-blocking.");
        if (report.Authenticity is null
            || report.Authenticity.Status is not ("unsigned" or "verified" or "trusted" or "invalid")
            || report.Authenticity.Policy is not ("permissive" or "signed" or "trusted")
            || string.IsNullOrWhiteSpace(report.Authenticity.Summary)
            || report.Authenticity.PolicySatisfied !=
                (report.Authenticity.Status != "invalid" && report.Authenticity.Policy switch
                {
                    "permissive" => true,
                    "signed" => report.Authenticity.Status is "verified" or "trusted",
                    "trusted" => report.Authenticity.Status == "trusted",
                    _ => false
                }))
            throw new InvalidDataException(
                "Deployment preflight authenticity result is invalid or inconsistent.");
    }

    private static void ValidateHash(string? value, string property)
    {
        if (value is null
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException(
                $"Deployment preflight {property} must be a 64-character SHA-256.");
    }

    private static void AddArtifactChecks(
        string target,
        IReadOnlySet<string> artifacts,
        ICollection<DeploymentPreflightCheck> checks)
    {
        var required = target switch
        {
            "server" =>
                artifacts.Any(path => path.StartsWith("artifacts/server/runtime/junexis-server", StringComparison.Ordinal))
                    ? new[] { artifacts.First(path => path.StartsWith("artifacts/server/runtime/junexis-server", StringComparison.Ordinal)) }
                    : new[] { "artifacts/server/main.go", "artifacts/server/go.mod" },
            "report" =>
                artifacts.Contains("artifacts/report/runtime/dist/browser.js")
                    ? new[]
                    {
                        "artifacts/report/runtime/index.html",
                        "artifacts/report/runtime/report.css",
                        "artifacts/report/runtime/serve.mjs",
                        "artifacts/report/runtime/junexis-report-manifest.json",
                        "artifacts/report/runtime/dist/browser.js"
                    }
                    : new[]
                    {
                        "artifacts/report/package.json",
                        "artifacts/report/tsconfig.json",
                        "artifacts/report/deploy.ts"
                    },
            "embedded" =>
                new[] { "artifacts/embedded/platformio.ini" },
            _ => []
        };
        var missing = required.Where(path => !artifacts.Contains(path)).ToArray();
        if (target == "report"
            && !artifacts.Contains("artifacts/report/runtime/dist/browser.js")
            && !artifacts.Any(path =>
                path.StartsWith("artifacts/report/", StringComparison.Ordinal)
                && path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)))
            missing = [.. missing, "artifacts/report/<typescript-source>"];
        if (target == "embedded"
            && !artifacts.Any(path =>
                path.StartsWith("artifacts/embedded/", StringComparison.Ordinal)
                && path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)))
            missing = [.. missing, "artifacts/embedded/<cpp-source>"];

        if (missing.Length > 0)
        {
            checks.Add(Block(
                "DP1004",
                target,
                $"Required deployment artifacts are missing: {string.Join(", ", missing)}."));
            return;
        }
        checks.Add(Pass(
            "DP0003",
            target,
            $"Required '{target}' deployment artifacts are present."));
        if (target == "embedded")
        {
            checks.Add(Block(
                "DP1005",
                target,
                "Embedded deployment executor is not available before M7.7; no device may be flashed."));
        }
    }

    private static async Task<DeploymentMentorOpinion> ReviewWithMentorAsync(
        SystemBundleInspection bundle,
        string? requestedExecutable,
        CancellationToken cancellationToken)
    {
        var executable = ResolveMentorExecutable(requestedExecutable);
        if (executable is null)
            return Mentor(
                string.IsNullOrWhiteSpace(requestedExecutable)
                    ? "not-configured"
                    : "unavailable",
                null,
                string.IsNullOrWhiteSpace(requestedExecutable)
                    ? "Mentor is not configured; technical deployment checks remain authoritative."
                    : "The requested Mentor CLI was not found; technical deployment checks remain authoritative.");

        var analysisArtifact = bundle.Descriptor.Artifacts.SingleOrDefault(
            artifact => artifact.Path == "artifacts/analysis/junexis.analysis.json");
        if (analysisArtifact is null)
            return Mentor(
                "unavailable",
                null,
                "Mentor could not review this bundle because its Analysis Snapshot is missing.");

        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"junexis-deployment-preflight-{Guid.NewGuid():N}.analysis.json");
        try
        {
            var snapshot = await ReadVerifiedEntryAsync(
                bundle.Path,
                analysisArtifact,
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(
                temporaryPath,
                snapshot,
                cancellationToken).ConfigureAwait(false);
            var process = await RunMentorAsync(
                executable,
                temporaryPath,
                cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                return Mentor(
                    "unavailable",
                    null,
                    $"Mentor exited with code {process.ExitCode}; its opinion was not applied.");
            return ParseMentorOpinion(process.StandardOutput, bundle.Descriptor.BuildId);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException
                or TimeoutException
                or ArgumentException)
        {
            return Mentor(
                "unavailable",
                null,
                $"Mentor opinion is unavailable: {ex.Message}");
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            return Mentor(
                "unavailable",
                null,
                $"Mentor could not be started: {ex.Message}");
        }
        finally
        {
            TryDeleteTemporarySnapshot(temporaryPath);
        }
    }

    private static DeploymentMentorOpinion ParseMentorOpinion(
        string json,
        string expectedBuildId)
    {
        if (json.Length > MaximumMentorOutputCharacters)
            throw new InvalidDataException("Mentor output exceeds the 16 MiB safety limit.");
        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        var schema = root.GetProperty("schemaVersion").GetString();
        var success = root.GetProperty("success").GetBoolean();
        var buildId = root.GetProperty("buildId").GetString();
        if (schema is null
            || !schema.StartsWith("1.", StringComparison.Ordinal)
            || !success
            || string.IsNullOrWhiteSpace(buildId))
            throw new InvalidDataException("Mentor returned an unsupported analysis envelope.");
        if (!string.Equals(buildId, expectedBuildId, StringComparison.Ordinal))
            return Mentor(
                "stale",
                buildId,
                "Mentor Build ID differs from the bundle; the opinion was ignored.");

        var findings = root.GetProperty("findings").EnumerateArray()
            .Take(512)
            .Select(item => new DeploymentMentorFinding(
                RequiredString(item, "code", 32),
                RequiredString(item, "severity", 32),
                RequiredString(item, "message", 4096),
                OptionalString(item, "relatedSymbolId", 512),
                OptionalString(item, "recommendedAction", 4096)))
            .ToArray();
        var importantCount = findings.Count(item =>
            item.Severity is "error" or "warning");
        return new DeploymentMentorOpinion(
            "reviewed",
            buildId,
            true,
            importantCount == 0
                ? $"Mentor reviewed {findings.Length} finding(s); no Error or Warning opinion."
                : $"Mentor reviewed {findings.Length} finding(s); {importantCount} Error/Warning opinion(s), non-blocking.",
            findings);
    }

    private static string RequiredString(
        JsonElement element,
        string property,
        int maximumLength)
    {
        var value = element.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new InvalidDataException(
                $"Mentor finding property '{property}' is invalid.");
        return value;
    }

    private static string? OptionalString(
        JsonElement element,
        string property,
        int maximumLength)
    {
        if (!element.TryGetProperty(property, out var propertyElement)
            || propertyElement.ValueKind == JsonValueKind.Null)
            return null;
        var value = propertyElement.GetString();
        if (value is not null && value.Length > maximumLength)
            throw new InvalidDataException(
                $"Mentor finding property '{property}' is too long.");
        return value;
    }

    private static string? ResolveMentorExecutable(string? requested)
    {
        var candidate = string.IsNullOrWhiteSpace(requested)
            ? Environment.GetEnvironmentVariable("JUNEXIS_MENTOR_CLI")
            : requested;
        if (!string.IsNullOrWhiteSpace(candidate))
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        var names = OperatingSystem.IsWindows()
            ? new[] { "junexis-mentor.exe", "junexis-mentor.cmd", "junexis-mentor.bat" }
            : new[] { "junexis-mentor" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static async Task<(int ExitCode, string StandardOutput)> RunMentorAsync(
        string executable,
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        var isDll = Path.GetExtension(executable).Equals(
            ".dll",
            StringComparison.OrdinalIgnoreCase);
        var requiresShell = OperatingSystem.IsWindows()
            && (Path.GetExtension(executable).Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(executable).Equals(".bat", StringComparison.OrdinalIgnoreCase));
        var startInfo = new ProcessStartInfo(
            isDll
                ? "dotnet"
                : requiresShell
                    ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
                    : executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (isDll)
            startInfo.ArgumentList.Add(executable);
        else if (requiresShell)
            startInfo.ArgumentList.Add("/d");
        if (requiresShell)
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(executable);
        }
        startInfo.ArgumentList.Add("analyze");
        startInfo.ArgumentList.Add("--snapshot");
        startInfo.ArgumentList.Add(snapshotPath);
        startInfo.ArgumentList.Add("--json");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Mentor CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MentorTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Mentor did not finish within 15 seconds.");
        }
        var standardOutput = await stdout.ConfigureAwait(false);
        _ = await stderr.ConfigureAwait(false);
        if (standardOutput.Length > MaximumMentorOutputCharacters)
            throw new InvalidDataException("Mentor output exceeds the 16 MiB safety limit.");
        return (process.ExitCode, standardOutput);
    }

    private static async Task<byte[]> ReadVerifiedEntryAsync(
        string bundlePath,
        SystemBundleArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            bundlePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry(artifact.Path)
            ?? throw new InvalidDataException(
                $"System Bundle is missing '{artifact.Path}'.");
        if (entry.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("Analysis Snapshot exceeds the 64 MiB entry limit.");
        await using var stream = entry.Open();
        using var buffer = new MemoryStream((int)entry.Length);
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var content = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!hash.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Analysis Snapshot changed after System Bundle verification.");
        return content;
    }

    private static DeploymentMentorOpinion Mentor(
        string status,
        string? buildId,
        string summary) =>
        new(status, buildId, true, summary, []);

    private static void TryDeleteTemporarySnapshot(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The Mentor opinion remains fail-open; a temporary-file cleanup problem
            // must not become a technical deployment decision.
        }
    }

    private static DeploymentPreflightCheck Pass(
        string code,
        string? target,
        string message) =>
        new(code, "passed", target, message);

    private static DeploymentPreflightCheck Block(
        string code,
        string? target,
        string message) =>
        new(code, "blocked", target, message);

    private static DeploymentPreflightCheck Info(
        string code,
        string? target,
        string message) =>
        new(code, "info", target, message);
}
