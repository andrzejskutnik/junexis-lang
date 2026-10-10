using System.IO.Compression;
using System.Security.Cryptography;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record LocalDeploymentPlan(
    string BundlePath,
    string BundleId,
    string BuildId,
    string PackageReceiptPath,
    IReadOnlyList<string> Targets,
    string WorkDirectory,
    bool OwnsWorkDirectory,
    int ExtractedArtifactCount);

internal static class LocalDeploymentContract
{
    private const long MaximumArtifactBytes = 64L * 1024 * 1024;

    internal static async Task<LocalDeploymentPlan> PrepareAsync(
        string bundlePath,
        string? receiptPath,
        string preflightPath,
        string[]? requestedTargets,
        string? workDirectory,
        CancellationToken cancellationToken = default)
    {
        var bundle = await SystemBundleContract.VerifyAsync(
            bundlePath,
            cancellationToken).ConfigureAwait(false);
        var effectiveReceiptPath = string.IsNullOrWhiteSpace(receiptPath)
            ? PackageReceiptContract.DefaultPath(bundle.Path)
            : Path.GetFullPath(receiptPath);
        var receipt = await PackageReceiptContract.ReadAsync(
            effectiveReceiptPath,
            cancellationToken).ConfigureAwait(false);
        PackageReceiptContract.ValidateForBundle(receipt, bundle);

        var preflight = await DeploymentPreflightContract.ReadAsync(
            preflightPath,
            cancellationToken).ConfigureAwait(false);
        ValidatePreflight(preflight, bundle);
        var targets = ResolveTargets(preflight, requestedTargets);
        ValidateTargets(targets, bundle, receipt);

        var (root, ownsRoot) = CreateWorkDirectory(workDirectory, bundle.BundleId);
        try
        {
            var count = await ExtractTargetsAsync(
                bundle,
                targets,
                root,
                cancellationToken).ConfigureAwait(false);
            return new LocalDeploymentPlan(
                bundle.Path,
                bundle.BundleId,
                bundle.Descriptor.BuildId,
                effectiveReceiptPath,
                targets,
                root,
                ownsRoot,
                count);
        }
        catch
        {
            if (ownsRoot)
                TryDeleteOwnedDirectory(root);
            throw;
        }
    }

    internal static void Cleanup(LocalDeploymentPlan plan)
    {
        if (plan.OwnsWorkDirectory)
            TryDeleteOwnedDirectory(plan.WorkDirectory);
    }

    private static void ValidatePreflight(
        DeploymentPreflightReport report,
        SystemBundleInspection bundle)
    {
        if (report.TechnicalStatus != "ready")
            throw new InvalidDataException(
                "Deployment requires a technically ready preflight report.");
        if (report.BundleId != bundle.BundleId
            || report.BuildId != bundle.Descriptor.BuildId)
            throw new InvalidDataException(
                "Deployment preflight report does not belong to the selected System Bundle.");
    }

    private static string[] ResolveTargets(
        DeploymentPreflightReport report,
        string[]? requestedTargets)
    {
        var targets = requestedTargets is { Length: > 0 }
            ? requestedTargets
            : report.Targets.ToArray();
        var normalized = targets
            .Select(target => target.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0
            || normalized.Any(target => target is not ("server" or "report")))
            throw new NotSupportedException(
                "M7.4 local deployment supports only server and report; embedded remains blocked until M7.7.");
        if (normalized.Any(target => !report.Targets.Contains(target, StringComparer.Ordinal)))
            throw new InvalidDataException(
                "Every local deployment target must be covered by the selected preflight report.");
        return normalized;
    }

    private static void ValidateTargets(
        IReadOnlyList<string> targets,
        SystemBundleInspection bundle,
        PackageReceipt receipt)
    {
        foreach (var target in targets)
        {
            if (!bundle.Descriptor.Targets.Contains(target, StringComparer.Ordinal))
                throw new InvalidDataException(
                    $"Target '{target}' is not present in the System Bundle.");
            if (!receipt.Gates.Any(gate =>
                    gate.Target == target
                    && gate.Kind == "native-build"
                    && gate.Status == "passed"))
                throw new InvalidDataException(
                    $"Target '{target}' has no passed native package gate.");
        }
    }

    private static (string Path, bool Owned) CreateWorkDirectory(
        string? requestedPath,
        string bundleId)
    {
        var owned = string.IsNullOrWhiteSpace(requestedPath);
        var root = owned
            ? Path.Combine(
                Path.GetTempPath(),
                $"junexis-local-deploy-{bundleId[..12]}-{Guid.NewGuid():N}")
            : Path.GetFullPath(requestedPath!);
        if (Directory.Exists(root)
            && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException(
                "Local deployment work directory must be new or empty.");
        Directory.CreateDirectory(root);
        return (root, owned);
    }

    private static async Task<int> ExtractTargetsAsync(
        SystemBundleInspection bundle,
        IReadOnlyList<string> targets,
        string root,
        CancellationToken cancellationToken)
    {
        var selected = bundle.Descriptor.Artifacts
            .Where(artifact => targets.Contains(artifact.Target, StringComparer.Ordinal))
            .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidDataException(
                "No artifacts exist for the selected local deployment targets.");

        await using var input = new FileStream(
            bundle.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var artifact in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = $"artifacts/{artifact.Target}/";
            if (!artifact.Path.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Artifact '{artifact.Path}' is outside its declared target.");
            var relative = artifact.Path[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relative))
                throw new InvalidDataException(
                    $"Artifact '{artifact.Path}' has no target-relative path.");
            var targetRoot = Path.GetFullPath(Path.Combine(root, artifact.Target));
            var destination = Path.GetFullPath(Path.Combine(targetRoot, relative));
            if (!destination.StartsWith(
                    targetRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Artifact '{artifact.Path}' escapes the deployment workspace.");
            var entry = archive.GetEntry(artifact.Path)
                ?? throw new InvalidDataException(
                    $"Artifact '{artifact.Path}' disappeared from the verified bundle.");
            if (entry.Length < 0 || entry.Length > MaximumArtifactBytes)
                throw new InvalidDataException(
                    $"Artifact '{artifact.Path}' exceeds the extraction safety limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            long total = 0;
            while ((read = await source.ReadAsync(
                       buffer,
                       cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaximumArtifactBytes)
                    throw new InvalidDataException(
                        $"Artifact '{artifact.Path}' exceeds the extraction safety limit.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!actual.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Artifact '{artifact.Path}' changed during extraction.");
            if (!OperatingSystem.IsWindows()
                && artifact.Kind == "native-executable")
            {
                File.SetUnixFileMode(
                    destination,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute);
            }
        }
        return selected.Length;
    }

    private static void TryDeleteOwnedDirectory(string root)
    {
        try
        {
            var temp = Path.GetFullPath(Path.GetTempPath());
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith(
                    "junexis-local-deploy-",
                    StringComparison.Ordinal))
                Directory.Delete(resolved, recursive: true);
        }
        catch
        {
            // Best effort after all owned child processes have been stopped.
        }
    }
}
