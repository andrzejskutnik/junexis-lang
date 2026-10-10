using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>Buduje, sprawdza i uruchamia samodzielne paczki Laboratorium Junexis.</summary>
internal static class LaboratoryHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Buduje zweryfikowaną paczkę laboratorium dla wybranego RID-u M19.</summary>
    internal static async Task<int> PackageAsync(
        string? projectPath,
        FileInfo? destination,
        string goExe,
        string? runtimeIdentifier)
    {
        string? temporaryRoot = null;
        try
        {
            var hostRid = GetCurrentRuntimeIdentifier()
                ?? throw new NotSupportedException("Current host is outside the M19 laboratory matrix.");
            var variant = ResolveVariant(runtimeIdentifier ?? hostRid);

            var resolved = await CliProjectInputResolver.ResolveAsync(
                projectPath,
                explicitTargets: ["server"]);
            var project = resolved.Project
                ?? throw new InvalidDataException(
                    "'junexis lab package' requires a directory with junexis.project.toml.");
            var root = project.RootDirectory.FullName;
            var laboratoryPath = Path.Combine(root, "laboratory.json");
            var readmePath = Path.Combine(root, "README.md");
            if (!File.Exists(laboratoryPath) || !File.Exists(readmePath))
                throw new InvalidDataException(
                    "Laboratory project requires laboratory.json and README.md in its root.");

            var definition = JsonSerializer.Deserialize<LaboratoryDefinition>(
                await File.ReadAllTextAsync(laboratoryPath), JsonOptions)
                ?? throw new InvalidDataException("laboratory.json is empty.");
            ValidateDefinition(definition);

            var outputRoot = resolved.OutDirectory?.FullName ?? project.OutDirectory.FullName;
            var laboratoryOutput = Path.Combine(outputRoot, "laboratory");
            Directory.CreateDirectory(laboratoryOutput);
            var systemBundlePath = Path.Combine(
                laboratoryOutput,
                $"{definition.Id}.system-bundle.zip");

            await Console.Out.WriteLineAsync("Laboratorium · analiza, build i dowód System Bundle");
            var packageExit = await PackageHandler.HandleAsync(
                root,
                new FileInfo(systemBundlePath),
                ["server"],
                goExe,
                "npm",
                "pio");
            if (packageExit != 0)
                return packageExit;

            var systemBundle = await SystemBundleContract.VerifyAsync(systemBundlePath);
            temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "junexis-laboratory-build",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            var executablePath = Path.Combine(temporaryRoot, variant.ExecutableName);
            await Console.Out.WriteLineAsync(
                variant.RuntimeIdentifier == hostRid
                    ? $"Laboratorium · natywny serwer {variant.RuntimeIdentifier}"
                    : $"Laboratorium · serwer {variant.RuntimeIdentifier} składany krzyżowo na {hostRid}");
            await BuildNativeServerAsync(
                goExe,
                Path.Combine(outputRoot, "server"),
                executablePath,
                variant);

            var version = definition.SchemaVersion[(definition.SchemaVersion.LastIndexOf('/') + 1)..];
            var destinationPath = destination?.FullName ?? Path.Combine(
                laboratoryOutput,
                $"{definition.Id}-{version}-{variant.RuntimeIdentifier}.junexis-lab.zip");
            var package = await LaboratoryPackageContract.CreateAsync(
                destinationPath,
                laboratoryPath,
                readmePath,
                executablePath,
                systemBundlePath,
                definition.Id,
                definition.Name,
                version,
                variant.RuntimeIdentifier,
                systemBundle.Descriptor.BuildId,
                systemBundle.BundleId);

            await Console.Out.WriteLineAsync("Laboratorium gotowe.");
            await Console.Out.WriteLineAsync($"Paczka: {package.Path}");
            await Console.Out.WriteLineAsync($"Build ID: {package.Manifest.BuildId}");
            await Console.Out.WriteLineAsync($"Bundle ID: {package.Manifest.BundleId}");
            await Console.Out.WriteLineAsync(
                $"Integralność: {package.VerifiedFileCount} plików zweryfikowanych");
            return 0;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or JsonException
            or NotSupportedException
            or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync($"error: laboratory package failed: {ex.Message}");
            return 1;
        }
        finally
        {
            TryDeleteOwnedTemporaryDirectory(temporaryRoot, "junexis-laboratory-build");
        }
    }

    /// <summary>Sprawdza integralność samodzielnej paczki laboratorium.</summary>
    internal static async Task<int> VerifyAsync(FileInfo package, bool json)
    {
        try
        {
            var inspection = await LaboratoryPackageContract.VerifyAsync(package.FullName);
            if (json)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    path = inspection.Path,
                    inspection.Manifest.LaboratoryId,
                    inspection.Manifest.Name,
                    inspection.Manifest.Version,
                    inspection.Manifest.RuntimeIdentifier,
                    inspection.Manifest.BuildId,
                    inspection.Manifest.BundleId,
                    inspection.VerifiedFileCount
                }, JsonOptions));
            }
            else
            {
                await Console.Out.WriteLineAsync(
                    $"Paczka laboratorium poprawna: {inspection.Manifest.Name}");
                await Console.Out.WriteLineAsync(
                    $"Wersja: {inspection.Manifest.Version} · {inspection.Manifest.RuntimeIdentifier}");
                await Console.Out.WriteLineAsync($"Build ID: {inspection.Manifest.BuildId}");
                await Console.Out.WriteLineAsync($"Bundle ID: {inspection.Manifest.BundleId}");
                await Console.Out.WriteLineAsync(
                    $"Integralność: {inspection.VerifiedFileCount} plików zweryfikowanych");
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or JsonException)
        {
            await Console.Error.WriteLineAsync($"error: laboratory verification failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Uruchamia zweryfikowane laboratorium bez checkoutu źródeł i kompilacji.</summary>
    internal static async Task<int> RunAsync(
        FileInfo package,
        int httpPort,
        int healthPort,
        int tcpPort,
        int durationSeconds,
        string? traceUrl)
    {
        string? extractionRoot = null;
        Process? process = null;
        try
        {
            ValidatePorts(httpPort, healthPort, tcpPort, durationSeconds);
            var inspection = await LaboratoryPackageContract.VerifyAsync(package.FullName);
            var hostRid = GetCurrentRuntimeIdentifier()
                ?? throw new NotSupportedException("Current host is outside the M19 laboratory matrix.");
            if (!string.Equals(inspection.Manifest.RuntimeIdentifier, hostRid, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Package requires '{inspection.Manifest.RuntimeIdentifier}', but this host is '{hostRid}'.");
            }

            extractionRoot = Path.Combine(
                Path.GetTempPath(),
                "junexis-laboratory-run",
                Guid.NewGuid().ToString("N"));
            await LaboratoryPackageContract.ExtractVerifiedAsync(package.FullName, extractionRoot);
            var executablePath = ResolveExtractedPath(extractionRoot, inspection.Manifest.Entrypoint);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    executablePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            var info = new ProcessStartInfo(executablePath)
            {
                WorkingDirectory = extractionRoot,
                UseShellExecute = false
            };
            info.ArgumentList.Add("--server-mode");
            info.ArgumentList.Add("persistent");
            info.ArgumentList.Add("--http-port");
            info.ArgumentList.Add(httpPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add("--health-port");
            info.ArgumentList.Add(healthPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add("--tcp-port");
            info.ArgumentList.Add(tcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.Environment["JUNEXIS_BUILD_ID"] = inspection.Manifest.BuildId;
            info.Environment["JUNEXIS_BUNDLE_ID"] = inspection.Manifest.BundleId;
            var stateDirectory = ResolveLaboratoryStateDirectory(inspection.Manifest.LaboratoryId);
            Directory.CreateDirectory(stateDirectory);
            info.Environment["JUNEXIS_LAB_STATE_DIR"] = stateDirectory;
            if (!string.IsNullOrWhiteSpace(traceUrl))
                info.Environment["JUNEXIS_TRACE_URL"] = traceUrl;

            process = Process.Start(info)
                ?? throw new InvalidOperationException("Laboratory process could not be started.");
            var panelUrl = new Uri($"http://127.0.0.1:{httpPort}{inspection.Manifest.PanelPath}");
            await WaitForPanelAsync(process, panelUrl, TimeSpan.FromSeconds(15));
            await Console.Out.WriteLineAsync($"Laboratorium działa: {panelUrl}");
            await Console.Out.WriteLineAsync($"Trwały stan laboratorium: {stateDirectory}");
            await Console.Out.WriteLineAsync(
                durationSeconds > 0
                    ? $"Automatyczne zatrzymanie za {durationSeconds} s."
                    : "Zatrzymaj laboratorium skrótem Ctrl+C.");

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                if (durationSeconds > 0)
                    cancellation.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
                await process.WaitForExitAsync(cancellation.Token);
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                await Console.Out.WriteLineAsync("Zatrzymywanie laboratorium…");
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or JsonException
            or NotSupportedException
            or InvalidOperationException
            or HttpRequestException)
        {
            await Console.Error.WriteLineAsync($"error: laboratory run failed: {ex.Message}");
            return 1;
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                catch (InvalidOperationException)
                {
                    // Proces zakończył się pomiędzy sprawdzeniem i zatrzymaniem.
                }
            }
            process?.Dispose();
            TryDeleteOwnedTemporaryDirectory(extractionRoot, "junexis-laboratory-run");
        }
    }

    private static async Task BuildNativeServerAsync(
        string goExe,
        string workingDirectory,
        string executablePath,
        LaboratoryRuntimeVariant variant)
    {
        if (!Directory.Exists(workingDirectory))
            throw new InvalidDataException("Generated server output is missing after the verified build.");
        var info = new ProcessStartInfo(goExe)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("build");
        info.ArgumentList.Add("-trimpath");
        info.ArgumentList.Add("-buildvcs=false");
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add(executablePath);
        info.ArgumentList.Add(".");
        info.Environment["GOOS"] = variant.GoOs;
        info.Environment["GOARCH"] = variant.GoArch;
        info.Environment["CGO_ENABLED"] = "0";
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Go compiler could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
            throw new InvalidDataException(
                $"Native laboratory build failed ({process.ExitCode}): {error.Trim()}");
        if (!string.IsNullOrWhiteSpace(output))
            await Console.Out.WriteLineAsync(output.Trim());
        if (!File.Exists(executablePath))
            throw new InvalidDataException("Go build did not publish the laboratory executable.");
    }

    private static async Task WaitForPanelAsync(Process process, Uri panelUrl, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"Laboratory process exited before readiness ({process.ExitCode}).");
            try
            {
                using var response = await client.GetAsync(panelUrl);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Serwer jeszcze nie nasłuchuje.
            }
            catch (TaskCanceledException)
            {
                // Pojedyncza próba przekroczyła limit; próbujemy do końca okna gotowości.
            }
            await Task.Delay(150);
        }
        throw new InvalidOperationException("Laboratory panel did not become ready within 15 seconds.");
    }

    private static string ResolveExtractedPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Laboratory entrypoint escapes the extraction root.");
        return path;
    }

    private static string? GetCurrentRuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows() && architecture == Architecture.X64) return "win-x64";
        if (OperatingSystem.IsLinux() && architecture == Architecture.X64) return "linux-x64";
        if (OperatingSystem.IsMacOS() && architecture == Architecture.Arm64) return "osx-arm64";
        return null;
    }

    private static LaboratoryRuntimeVariant ResolveVariant(string runtimeIdentifier) => runtimeIdentifier switch
    {
        "win-x64" => new(runtimeIdentifier, "windows", "amd64", "junexis-laboratory.exe"),
        "linux-x64" => new(runtimeIdentifier, "linux", "amd64", "junexis-laboratory"),
        "osx-arm64" => new(runtimeIdentifier, "darwin", "arm64", "junexis-laboratory"),
        _ => throw new NotSupportedException(
            $"Laboratory runtime '{runtimeIdentifier}' is outside the M19 matrix.")
    };

    private static void ValidateDefinition(LaboratoryDefinition definition)
    {
        if (!definition.SchemaVersion.StartsWith("junexis.laboratory/", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(definition.Id)
            || string.IsNullOrWhiteSpace(definition.Name))
            throw new InvalidDataException("laboratory.json has an invalid schema, id, or name.");
    }

    private static string ResolveLaboratoryStateDirectory(string laboratoryId)
    {
        if (string.IsNullOrWhiteSpace(laboratoryId)
            || laboratoryId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidDataException("Laboratory id cannot be used as a durable state directory.");
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            throw new InvalidOperationException("Local application data directory is unavailable.");
        return Path.Combine(localData, "Junexis", "Laboratories", laboratoryId);
    }

    private static void ValidatePorts(int httpPort, int healthPort, int tcpPort, int durationSeconds)
    {
        if (new[] { httpPort, healthPort, tcpPort }.Any(port => port is < 1 or > 65535)
            || new[] { httpPort, healthPort, tcpPort }.Distinct().Count() != 3)
            throw new ArgumentException("HTTP, health, and TCP ports must be distinct values from 1 to 65535.");
        if (durationSeconds < 0)
            throw new ArgumentException("Duration cannot be negative.");
    }

    private static void TryDeleteOwnedTemporaryDirectory(string? directory, string ownerSegment)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;
        try
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ownerSegment));
            var candidate = Path.GetFullPath(directory);
            if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;
            Directory.Delete(candidate, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"warning: could not remove temporary laboratory files: {ex.Message}");
        }
    }

    private sealed record LaboratoryDefinition(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record LaboratoryRuntimeVariant(
        string RuntimeIdentifier,
        string GoOs,
        string GoArch,
        string ExecutableName);
}
