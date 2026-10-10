using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class EmbeddedDeploymentHandler
{
    private const long MaximumArtifactBytes = 64L * 1024 * 1024;
    private const string SelectionSchemaVersion = "junexis.embedded-device-selection/0.1";
    private const string FlashReceiptSchemaVersion = "junexis.embedded-flash-receipt/0.1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    internal static async Task<int> SelectAsync(
        string platformIoExe,
        string port,
        FileInfo? output,
        bool json)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            await Console.Error.WriteLineAsync("error: embedded device selection requires a non-empty --port.");
            return 1;
        }

        var probe = await EmbeddedProbeHandler.ProbeAsync(platformIoExe).ConfigureAwait(false);
        if (!probe.ProbeSucceeded)
        {
            await Console.Error.WriteLineAsync($"error: embedded device selection failed: {probe.Message}").ConfigureAwait(false);
            return 1;
        }

        var device = probe.Devices.SingleOrDefault(candidate =>
            string.Equals(candidate.Port, port, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            await Console.Error.WriteLineAsync(
                $"error: embedded device '{port}' was not found in the current PlatformIO probe.").ConfigureAwait(false);
            return 1;
        }

        var selection = new EmbeddedDeviceSelection(
            SelectionSchemaVersion,
            DateTimeOffset.UtcNow.ToString("O"),
            probe.Tool,
            probe.ToolPath ?? probe.Tool,
            device.Port,
            device.Description,
            device.HardwareId,
            device.SerialNumber,
            device.Manufacturer);
        var selectionPath = output is null
            ? DefaultSelectionPath()
            : Path.GetFullPath(output.FullName);
        await WriteSelectionAsync(selectionPath, selection).ConfigureAwait(false);
        await EmbeddedDeploymentEventPublisher.TryPublishSelectionAsync(selection, selectionPath).ConfigureAwait(false);

        if (json)
        {
            await Console.Out.WriteLineAsync(Serialize(selection)).ConfigureAwait(false);
        }
        else
        {
            await Console.Out.WriteLineAsync($"Embedded device selected: {selection.Port}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"Selection: {selectionPath}").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(selection.Description))
                await Console.Out.WriteLineAsync($"Description: {selection.Description}").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(selection.HardwareId))
                await Console.Out.WriteLineAsync($"Hardware ID: {selection.HardwareId}").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(selection.SerialNumber))
                await Console.Out.WriteLineAsync($"Serial: {selection.SerialNumber}").ConfigureAwait(false);
        }

        return 0;
    }

    internal static async Task<int> FlashAsync(
        FileInfo bundle,
        FileInfo? receipt,
        FileInfo selection,
        string platformIoExe,
        DirectoryInfo? workDirectory,
        FileInfo? receiptOut,
        bool json,
        string authenticityPolicy = "permissive",
        FileInfo? signature = null,
        string[]? trustedSigners = null)
    {
        EmbeddedFlashPlan? plan = null;
        EmbeddedFlashReceipt? flashReceipt = null;
        try
        {
            var selectedDevice = await ReadSelectionAsync(selection.FullName).ConfigureAwait(false);
            var probe = await EmbeddedProbeHandler.ProbeAsync(platformIoExe).ConfigureAwait(false);
            if (!probe.ProbeSucceeded)
                throw new InvalidOperationException($"embedded flash probe failed: {probe.Message}");

            var currentDevice = probe.Devices.SingleOrDefault(candidate =>
                string.Equals(candidate.Port, selectedDevice.Port, StringComparison.OrdinalIgnoreCase));
            if (currentDevice is null)
                throw new InvalidOperationException(
                    $"selected embedded device '{selectedDevice.Port}' is no longer present in the current probe.");
            ValidateSelectedDevice(selectedDevice, currentDevice);

            plan = await PrepareFlashAsync(
                bundle.FullName,
                receipt?.FullName,
                selectedDevice,
                workDirectory?.FullName,
                authenticityPolicy,
                signature?.FullName,
                trustedSigners).ConfigureAwait(false);
            var uploadResult = await RunPlatformIoAsync(
                probe.ToolPath ?? probe.Tool,
                ["run", "--target", "upload", "--upload-port", selectedDevice.Port],
                Path.Combine(plan.WorkDirectory, "embedded"),
                plan.BuildId,
                plan.BundleId).ConfigureAwait(false);
            if (uploadResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(uploadResult.StandardError)
                        ? $"PlatformIO upload failed with exit code {uploadResult.ExitCode}."
                        : uploadResult.StandardError.Trim());
            }

            var effectiveReceiptPath = receiptOut is null
                ? DefaultFlashReceiptPath(selection.FullName)
                : Path.GetFullPath(receiptOut.FullName);
            flashReceipt = new EmbeddedFlashReceipt(
                FlashReceiptSchemaVersion,
                DateTimeOffset.UtcNow.ToString("O"),
                "succeeded",
                $"Uploaded verified embedded bundle to {selectedDevice.Port}.",
                plan.BundleId,
                plan.BuildId,
                plan.PackageReceiptPath,
                Path.GetFullPath(selection.FullName),
                probe.Tool,
                probe.ToolPath ?? probe.Tool,
                selectedDevice.Port,
                selectedDevice.Description,
                selectedDevice.HardwareId,
                selectedDevice.SerialNumber,
                selectedDevice.Manufacturer,
                plan.WorkDirectory,
                plan.ExtractedArtifactCount);
            await WriteFlashReceiptAsync(effectiveReceiptPath, flashReceipt).ConfigureAwait(false);
            await EmbeddedDeploymentEventPublisher.TryPublishFlashReceiptAsync(flashReceipt, effectiveReceiptPath).ConfigureAwait(false);

            if (json)
            {
                await Console.Out.WriteLineAsync(Serialize(flashReceipt)).ConfigureAwait(false);
            }
            else
            {
                await Console.Out.WriteLineAsync($"Embedded flash succeeded: {selectedDevice.Port}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Receipt: {effectiveReceiptPath}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Bundle: {plan.BundleId}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Build: {plan.BuildId}").ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException
                or InvalidOperationException
                or JsonException)
        {
            if (plan is not null)
            {
                var effectiveReceiptPath = receiptOut is null
                    ? DefaultFlashReceiptPath(selection.FullName)
                    : Path.GetFullPath(receiptOut.FullName);
                flashReceipt = new EmbeddedFlashReceipt(
                    FlashReceiptSchemaVersion,
                    DateTimeOffset.UtcNow.ToString("O"),
                    "failed",
                    ex.Message,
                    plan.BundleId,
                    plan.BuildId,
                    plan.PackageReceiptPath,
                    Path.GetFullPath(selection.FullName),
                    platformIoExe,
                    platformIoExe,
                    plan.Selection.Port,
                    plan.Selection.Description,
                    plan.Selection.HardwareId,
                    plan.Selection.SerialNumber,
                    plan.Selection.Manufacturer,
                    plan.WorkDirectory,
                    plan.ExtractedArtifactCount);
                await WriteFlashReceiptAsync(effectiveReceiptPath, flashReceipt).ConfigureAwait(false);
                await EmbeddedDeploymentEventPublisher.TryPublishFlashReceiptAsync(flashReceipt, effectiveReceiptPath).ConfigureAwait(false);
            }

            await Console.Error.WriteLineAsync($"error: embedded flash failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            if (plan is not null && plan.OwnsWorkDirectory)
                TryDeleteOwnedDirectory(plan.WorkDirectory);
        }
    }

    private static async Task<EmbeddedFlashPlan> PrepareFlashAsync(
        string bundlePath,
        string? receiptPath,
        EmbeddedDeviceSelection selection,
        string? workDirectory,
        string authenticityPolicy,
        string? signaturePath,
        string[]? trustedSigners,
        CancellationToken cancellationToken = default)
    {
        var bundle = await SystemBundleContract.VerifyAsync(bundlePath, cancellationToken).ConfigureAwait(false);
        var authenticity = await SystemBundleAuthenticityContract.InspectAsync(
            bundle.Path,
            signaturePath,
            authenticityPolicy,
            trustedSigners,
            cancellationToken).ConfigureAwait(false);
        if (!authenticity.PolicySatisfied)
            throw new InvalidDataException(
                $"Embedded flash blocked: bundle authenticity '{authenticity.Status}' " +
                $"does not satisfy policy '{authenticity.Policy}'.");
        var effectiveReceiptPath = string.IsNullOrWhiteSpace(receiptPath)
            ? PackageReceiptContract.DefaultPath(bundle.Path)
            : Path.GetFullPath(receiptPath);
        var receipt = await PackageReceiptContract.ReadAsync(effectiveReceiptPath, cancellationToken).ConfigureAwait(false);
        PackageReceiptContract.ValidateForBundle(receipt, bundle);

        if (!bundle.Descriptor.Targets.Contains("embedded", StringComparer.Ordinal))
            throw new NotSupportedException("The selected System Bundle has no embedded target to flash.");
        if (!receipt.Gates.Any(gate =>
                gate.Target == "embedded" && gate.Kind == "native-build" && gate.Status == "passed"))
            throw new InvalidDataException("The selected System Bundle has no passed embedded native gate.");

        var (root, ownsRoot) = CreateWorkDirectory(workDirectory, bundle.BundleId);
        try
        {
            var count = await ExtractEmbeddedArtifactsAsync(bundle, root, cancellationToken).ConfigureAwait(false);
            return new EmbeddedFlashPlan(
                bundle.Path,
                bundle.BundleId,
                bundle.Descriptor.BuildId,
                effectiveReceiptPath,
                selection,
                root,
                ownsRoot,
                count,
                authenticity.Status,
                authenticity.Policy,
                authenticity.PublicKeyFingerprintSha256);
        }
        catch
        {
            if (ownsRoot)
                TryDeleteOwnedDirectory(root);
            throw;
        }
    }

    private static async Task<int> ExtractEmbeddedArtifactsAsync(
        SystemBundleInspection bundle,
        string root,
        CancellationToken cancellationToken)
    {
        var selected = bundle.Descriptor.Artifacts
            .Where(artifact => string.Equals(artifact.Target, "embedded", StringComparison.Ordinal))
            .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidDataException("No embedded artifacts exist in the selected System Bundle.");

        await using var input = new FileStream(bundle.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var artifact in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string prefix = "artifacts/embedded/";
            if (!artifact.Path.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException($"Artifact '{artifact.Path}' is outside the embedded target.");

            var relative = artifact.Path[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relative))
                throw new InvalidDataException($"Artifact '{artifact.Path}' has no embedded-relative path.");

            var targetRoot = Path.GetFullPath(Path.Combine(root, "embedded"));
            var destination = Path.GetFullPath(Path.Combine(targetRoot, relative));
            if (!destination.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Artifact '{artifact.Path}' escapes the embedded flash workspace.");

            var entry = archive.GetEntry(artifact.Path)
                ?? throw new InvalidDataException($"Artifact '{artifact.Path}' disappeared from the verified bundle.");
            if (entry.Length < 0 || entry.Length > MaximumArtifactBytes)
                throw new InvalidDataException($"Artifact '{artifact.Path}' exceeds the extraction safety limit.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            long total = 0;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaximumArtifactBytes)
                    throw new InvalidDataException($"Artifact '{artifact.Path}' exceeds the extraction safety limit.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!actual.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Artifact '{artifact.Path}' changed during extraction.");
        }

        return selected.Length;
    }

    private static void ValidateSelectedDevice(EmbeddedDeviceSelection selection, EmbeddedProbeHandler.EmbeddedProbeDevice device)
    {
        if (!string.Equals(selection.Port, device.Port, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected embedded device port no longer matches the current probe.");
        ValidateIdentity(selection.HardwareId, device.HardwareId, "hardware ID");
        ValidateIdentity(selection.SerialNumber, device.SerialNumber, "serial number");
        ValidateIdentity(selection.Manufacturer, device.Manufacturer, "manufacturer");
    }

    private static void ValidateIdentity(string? selected, string? current, string label)
    {
        if (string.IsNullOrWhiteSpace(selected))
            return;
        if (string.IsNullOrWhiteSpace(current) || !string.Equals(selected, current, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Selected embedded device {label} no longer matches the current probe.");
    }

    private static (string Path, bool Owned) CreateWorkDirectory(string? requestedPath, string bundleId)
    {
        var owned = string.IsNullOrWhiteSpace(requestedPath);
        var root = owned
            ? Path.Combine(Path.GetTempPath(), $"junexis-embedded-flash-{bundleId[..12]}-{Guid.NewGuid():N}")
            : Path.GetFullPath(requestedPath!);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("Embedded flash work directory must be new or empty.");
        Directory.CreateDirectory(root);
        return (root, owned);
    }

    private static void TryDeleteOwnedDirectory(string root)
    {
        try
        {
            var temp = Path.GetFullPath(Path.GetTempPath());
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith("junexis-embedded-flash-", StringComparison.Ordinal))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static async Task<ProcessCapture> RunPlatformIoAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string buildId,
        string bundleId)
    {
        var requiresCommandInterpreter = OperatingSystem.IsWindows() &&
            Path.GetExtension(executable) is ".cmd" or ".bat";
        var processExecutable = requiresCommandInterpreter
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : executable;
        var info = new ProcessStartInfo(processExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var existingBuildFlags = info.Environment.TryGetValue("PLATFORMIO_BUILD_FLAGS", out var flags)
            ? flags
            : null;
        info.Environment["PLATFORMIO_BUILD_FLAGS"] = string.Join(
            " ",
            new[]
            {
                existingBuildFlags,
                $"-DJUNEXIS_BUILD_ID=\\\"{buildId}\\\"",
                $"-DJUNEXIS_BUNDLE_ID=\\\"{bundleId}\\\""
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (requiresCommandInterpreter)
        {
            var command = $"\"{executable}\" {string.Join(" ", arguments.Select(QuoteCommandArgument))}";
            info.Arguments = $"/d /c \"{command}\"";
        }
        else
        {
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start PlatformIO upload.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return new ProcessCapture(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static string QuoteCommandArgument(string argument) =>
        argument.Any(character => char.IsWhiteSpace(character) || character is '&' or '|' or '<' or '>')
            ? $"\"{argument.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : argument;

    private static string DefaultSelectionPath() => Path.GetFullPath("embedded-device-selection.json");

    private static string DefaultFlashReceiptPath(string selectionPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(selectionPath))
            ?? Environment.CurrentDirectory;
        return Path.Combine(directory, "embedded-flash-receipt.json");
    }

    private static async Task WriteSelectionAsync(string path, EmbeddedDeviceSelection selection)
    {
        ValidateSelection(selection);
        await WriteAtomicAsync(path, Serialize(selection)).ConfigureAwait(false);
    }

    private static async Task<EmbeddedDeviceSelection> ReadSelectionAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Embedded device selection was not found.", fullPath);
        EmbeddedDeviceSelection? selection;
        try
        {
            selection = JsonSerializer.Deserialize<EmbeddedDeviceSelection>(
                await File.ReadAllTextAsync(fullPath).ConfigureAwait(false),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Embedded device selection contains invalid JSON.", ex);
        }

        if (selection is null)
            throw new InvalidDataException("Embedded device selection is empty.");
        ValidateSelection(selection);
        return selection;
    }

    private static async Task WriteFlashReceiptAsync(string path, EmbeddedFlashReceipt receipt)
    {
        ValidateFlashReceipt(receipt);
        await WriteAtomicAsync(path, Serialize(receipt)).ConfigureAwait(false);
    }

    private static async Task WriteAtomicAsync(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Embedded deployment artifact path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false)).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static void ValidateSelection(EmbeddedDeviceSelection selection)
    {
        if (!string.Equals(selection.SchemaVersion, SelectionSchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported embedded device selection schema '{selection.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(selection.Port))
            throw new InvalidDataException("Embedded device selection must define a non-empty port.");
        if (string.IsNullOrWhiteSpace(selection.ToolPath))
            throw new InvalidDataException("Embedded device selection must preserve the probing tool path.");
    }

    private static void ValidateFlashReceipt(EmbeddedFlashReceipt receipt)
    {
        if (!string.Equals(receipt.SchemaVersion, FlashReceiptSchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported embedded flash receipt schema '{receipt.SchemaVersion}'.");
        if (receipt.LifecycleStatus is not ("succeeded" or "failed"))
            throw new InvalidDataException("Embedded flash receipt must have lifecycleStatus succeeded or failed.");
        if (string.IsNullOrWhiteSpace(receipt.Port))
            throw new InvalidDataException("Embedded flash receipt must preserve the upload port.");
    }

    internal sealed record EmbeddedDeviceSelection(
        string SchemaVersion,
        string SelectedAtUtc,
        string Tool,
        string ToolPath,
        string Port,
        string? Description,
        string? HardwareId,
        string? SerialNumber,
        string? Manufacturer);

    internal sealed record EmbeddedFlashReceipt(
        string SchemaVersion,
        string FlashedAtUtc,
        string LifecycleStatus,
        string Summary,
        string BundleId,
        string BuildId,
        string PackageReceiptPath,
        string SelectionPath,
        string Tool,
        string ToolPath,
        string Port,
        string? Description,
        string? HardwareId,
        string? SerialNumber,
        string? Manufacturer,
        string WorkDirectory,
        int ExtractedArtifactCount);

    private sealed record EmbeddedFlashPlan(
        string BundlePath,
        string BundleId,
        string BuildId,
        string PackageReceiptPath,
        EmbeddedDeviceSelection Selection,
        string WorkDirectory,
        bool OwnsWorkDirectory,
        int ExtractedArtifactCount,
        string AuthenticityStatus,
        string AuthenticityPolicy,
        string? SignerFingerprint);

    private sealed record ProcessCapture(int ExitCode, string StandardOutput, string StandardError);
}
