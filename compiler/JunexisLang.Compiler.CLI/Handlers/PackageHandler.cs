using System.Text.Json;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.CodeGen;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>
/// Orchestrates the experimental project-first package flow:
/// analysis preflight, native gates, bound build, bundle creation, and offline verification.
/// </summary>
internal static class PackageHandler
{
    /// <summary>Builds and verifies one Experimental System Bundle 0.1.</summary>
    internal static async Task<int> HandleAsync(
        string? projectPath,
        FileInfo? destination,
        string[]? targets,
        string goExe,
        string npmExe,
        string platformIoExe,
        FileInfo? signingKey = null,
        string? signer = null,
        string? storageRuntimeHost = null)
    {
        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(
                projectPath,
                explicitTargets: targets);
        }
        catch (Exception ex) when (
            ex is CliInputException
                or IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        if (resolved.Project is null)
        {
            await Console.Error.WriteLineAsync(
                "error: 'junexis package' is project-first; select a directory with junexis.project.toml.");
            return 1;
        }

        var selectedTargets = (resolved.Targets ?? [])
            .Select(target => target.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();
        if (selectedTargets.Length == 0)
        {
            await Console.Error.WriteLineAsync(
                "error: 'junexis package' requires at least one project target.");
            return 1;
        }
        if ((signingKey is null) != string.IsNullOrWhiteSpace(signer))
        {
            await Console.Error.WriteLineAsync(
                "error: package signing requires both --sign-key and --signer.");
            return 1;
        }
        var unsupported = selectedTargets
            .Where(target => target is not ("embedded" or "server" or "report"))
            .ToArray();
        if (unsupported.Length > 0)
        {
            await Console.Error.WriteLineAsync(
                $"error G0002: unknown package target(s): {string.Join(", ", unsupported)}");
            return 1;
        }

        var projectRoot = resolved.Project.RootDirectory.FullName;
        var outputRoot = resolved.OutDirectory?.FullName
            ?? resolved.Project.OutDirectory.FullName;
        var preflightPath = Path.Combine(
            outputRoot,
            "analysis",
            "junexis.analysis.preflight.json");
        try
        {
            await Console.Out.WriteLineAsync(
                $"Package 0.1 · analysis preflight [{string.Join(", ", selectedTargets)}]");
            var analysisExit = await AnalyzeHandler.HandleAsync(
                projectRoot,
                resolved.OutDirectory,
                selectedTargets,
                json: false);
            if (analysisExit != 0)
                return await FailAsync("analysis preflight");

            var preflight = await ReadPreflightAsync(preflightPath);

            await Console.Out.WriteLineAsync("Package 0.1 · native artifact gates");
            var gateExit = await ArtifactGateHandler.HandleAsync(
                projectRoot,
                selectedTargets,
                goExe,
                npmExe,
                platformIoExe);
            if (gateExit != 0)
                return await FailAsync("native artifact gates", gateExit);

            await Console.Out.WriteLineAsync(
                $"Package 0.1 · bound build {preflight.BuildId}");
            var buildExit = await CompileHandler.HandleAsync(
                projectRoot,
                resolved.OutDirectory,
                selectedTargets,
                json: false,
                outputSchema: "2.0",
                expectedBuildId: preflight.BuildId);
            if (buildExit != 0)
                return await FailAsync("bound build", buildExit);

            if (selectedTargets.Contains("server", StringComparer.Ordinal))
            {
                var storageBinding = await StorageRuntimeBindingPlanner.CreateAsync(
                    resolved,
                    storageRuntimeHost,
                    resolveConfiguration: false);
                if (storageBinding is not null
                    || selectedTargets.Contains("report", StringComparer.Ordinal))
                {
                    await Console.Out.WriteLineAsync(
                        "Package 0.1 · materialize runnable server, report and bound runtimes");
                    await PackageRuntimeArtifactBuilder.BuildAsync(
                        outputRoot,
                        goExe,
                        npmExe,
                        storageBinding);
                }
            }

            await Console.Out.WriteLineAsync("Package 0.1 · create and verify System Bundle");
            var created = await SystemBundleContract.CreateAsync(projectRoot, destination);
            var verified = await SystemBundleContract.VerifyAsync(created.Path);
            if (!string.Equals(created.BundleId, verified.BundleId, StringComparison.Ordinal)
                || !string.Equals(preflight.BuildId, verified.Descriptor.BuildId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "created System Bundle does not preserve the verified Build ID and Bundle ID.");
            }
            var packageReceipt = await PackageReceiptContract.WriteAsync(verified);
            (string Path, SystemBundleSignature Signature)? signature = null;
            if (signingKey is not null)
            {
                signature = await SystemBundleAuthenticityContract.SignAsync(
                    verified.Path,
                    signingKey.FullName,
                    signer!);
            }

            await Console.Out.WriteLineAsync($"Package succeeded: {verified.Path}");
            await Console.Out.WriteLineAsync($"Package receipt: {packageReceipt.Path}");
            if (signature is not null)
            {
                await Console.Out.WriteLineAsync($"Package signature: {signature.Value.Path}");
                await Console.Out.WriteLineAsync(
                    $"Signer fingerprint: {signature.Value.Signature.PublicKeyFingerprintSha256}");
            }
            await Console.Out.WriteLineAsync($"Schema: {verified.Descriptor.SchemaVersion}");
            await Console.Out.WriteLineAsync($"Build ID: {verified.Descriptor.BuildId}");
            await Console.Out.WriteLineAsync($"Bundle ID: {verified.BundleId}");
            await Console.Out.WriteLineAsync(
                $"Targets: {string.Join(", ", verified.Descriptor.Targets)} · " +
                $"{verified.VerifiedFileCount} verified payload file(s)");
            return 0;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception
                or JsonException)
        {
            await Console.Error.WriteLineAsync($"error: package failed: {ex.Message}");
            return 1;
        }
        finally
        {
            TryDeletePreflight(preflightPath);
        }

        async Task<int> FailAsync(string phase, int exitCode = 1)
        {
            await Console.Error.WriteLineAsync(
                $"error: package stopped at {phase}; no System Bundle was created.");
            return exitCode == 0 ? 1 : exitCode;
        }
    }

    private static async Task<AnalysisSnapshot> ReadPreflightAsync(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("analysis preflight did not publish its snapshot.");
        var snapshot = JsonSerializer.Deserialize<AnalysisSnapshot>(
            await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("analysis preflight snapshot is empty.");
        if (!string.Equals(
                snapshot.SchemaVersion,
                AnalysisSnapshot.CurrentVersion,
                StringComparison.Ordinal)
            || snapshot.BuildId.Length != 64
            || snapshot.BuildId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "analysis preflight snapshot has an unsupported schema or Build ID.");
        }
        return snapshot;
    }

    private static void TryDeletePreflight(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"warning: could not remove package preflight snapshot: {ex.Message}");
        }
    }
}
