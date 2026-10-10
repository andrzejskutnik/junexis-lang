using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>Materializes host-native runtime artifacts before a System Bundle is created.</summary>
internal static class PackageRuntimeArtifactBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task BuildAsync(
        string outputRoot,
        string goExecutable,
        string npmExecutable,
        StorageRuntimeBindingPlan? storageBinding = null)
    {
        var serverRoot = Path.Combine(outputRoot, "server");
        var reportRoot = Path.Combine(outputRoot, "report");
        var serverRuntime = Path.Combine(serverRoot, "runtime");
        var reportRuntime = Path.Combine(reportRoot, "runtime");
        Directory.CreateDirectory(serverRuntime);
        var runtimeArtifacts = new List<(string Path, string Target, string Kind)>();

        var serverName = OperatingSystem.IsWindows()
            ? "junexis-server.exe"
            : "junexis-server";
        var serverOutput = Path.Combine(serverRuntime, serverName);
        var isolatedServerRoot = Path.Combine(
            Path.GetTempPath(),
            $"junexis-package-server-{Guid.NewGuid():N}");
        try
        {
            CopyServerSources(serverRoot, isolatedServerRoot);
            await RunCheckedAsync(
                goExecutable,
                ["mod", "tidy"],
                isolatedServerRoot,
                "Go module resolution");
            await RunCheckedAsync(
                goExecutable,
                ["build", "-buildvcs=false", "-o", serverOutput, "."],
                isolatedServerRoot,
                "native server materialization");
        }
        finally
        {
            TryDeleteOwnedDirectory(isolatedServerRoot);
        }
        if (!File.Exists(serverOutput))
            throw new InvalidOperationException(
                "Go reported success but did not create the packaged server executable.");
        runtimeArtifacts.Add(($"server/runtime/{serverName}", "server", "native-executable"));

        if (Directory.Exists(reportRoot) && File.Exists(Path.Combine(reportRoot, "package.json")))
        {
            await RunCheckedAsync(
                npmExecutable,
                ["install", "--ignore-scripts"],
                reportRoot,
                "report dependency installation");
            await RunCheckedAsync(
                npmExecutable,
                ["run", "app:build"],
                reportRoot,
                "report application materialization");

            var reportFiles = new[]
            {
                ("index.html", "web-entrypoint"),
                ("report.css", "web-asset"),
                ("serve.mjs", "runtime-entrypoint"),
                ("junexis-report-manifest.json", "application-manifest"),
                ("dist/browser.js", "web-bundle")
            };
            foreach (var (relative, kind) in reportFiles)
            {
                var source = Path.Combine(reportRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source))
                    throw new InvalidOperationException(
                        $"Report materialization did not create '{relative}'.");
                var destination = Path.Combine(
                    reportRuntime,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                runtimeArtifacts.Add(($"report/runtime/{relative}", "report", kind));
            }
        }

        if (storageBinding is not null)
            runtimeArtifacts.AddRange(await MaterializeStorageRuntimeAsync(serverRuntime, storageBinding));

        await ExtendInventoryAsync(outputRoot, runtimeArtifacts);
        await Console.Out.WriteLineAsync(
            $"PACKAGED runtime · {serverName}" +
            (storageBinding is null
                ? string.Empty
                : $" · storage:{storageBinding.Binding.Package.Manifest.Id}@{storageBinding.Binding.Package.Manifest.Version}"));
    }

    private static async Task<IReadOnlyList<(string Path, string Target, string Kind)>>
        MaterializeStorageRuntimeAsync(
            string serverRuntime,
            StorageRuntimeBindingPlan plan)
    {
        var storageRoot = Path.Combine(serverRuntime, "storage");
        if (Directory.Exists(storageRoot))
        {
            var storageDirectory = new DirectoryInfo(storageRoot);
            if ((storageDirectory.Attributes & FileAttributes.ReparsePoint) != 0
                || !Path.GetFullPath(storageRoot).StartsWith(
                    Path.GetFullPath(serverRuntime) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Refusing an unsafe storage runtime output directory.");
            Directory.Delete(storageRoot, recursive: true);
        }
        var hostRoot = Path.Combine(storageRoot, "host");
        var providerRoot = Path.Combine(
            storageRoot, "providers", plan.Binding.Package.Manifest.Id);
        Directory.CreateDirectory(hostRoot);
        Directory.CreateDirectory(providerRoot);

        var artifacts = new List<(string Path, string Target, string Kind)>();
        var hostFiles = ResolveHostFiles(plan.RuntimeHostPath);
        foreach (var source in hostFiles)
        {
            var destination = Path.Combine(hostRoot, Path.GetFileName(source));
            File.Copy(source, destination, overwrite: true);
            artifacts.Add((
                $"server/runtime/storage/host/{Path.GetFileName(source)}",
                "server",
                "storage-runtime-host"));
        }

        var package = plan.Binding.Package;
        var packageFiles = package.Files
            .Select(file => file.Path)
            .Append(package.ManifestFile.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var relative in packageFiles)
        {
            var source = relative == package.ManifestFile.Name
                ? package.ManifestFile.FullName
                : Path.Combine(package.PackageDirectory.FullName,
                    relative.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(
                providerRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            artifacts.Add((
                $"server/runtime/storage/providers/{package.Manifest.Id}/{relative.Replace('\\', '/')}",
                "server",
                "storage-provider"));
        }

        var hostEntry = $"host/{Path.GetFileName(plan.RuntimeHostPath)}";
        var providerEntry =
            $"providers/{package.Manifest.Id}/{package.Manifest.Runtime.Entrypoint}";
        var descriptor = new
        {
            schemaVersion = "junexis.storage.deployment/0.1",
            runtimeAbi = package.Manifest.RuntimeAbi,
            runtimeHost = hostEntry,
            binding = new
            {
                name = plan.Binding.Binding.Name,
                declaration = plan.Binding.Binding.Declaration,
                provider = package.Manifest.Id,
                providerVersion = package.Manifest.Version,
                packageSha256 = package.PackageSha256,
                entrypoint = providerEntry,
                configuration = plan.Binding.Configuration.Select(item => new
                {
                    name = item.Name,
                    source = item.Source,
                    reference = item.Reference,
                    value = item.Value
                }).ToArray()
            }
        };
        var descriptorPath = Path.Combine(storageRoot, "junexis.storage.deployment.json");
        await File.WriteAllTextAsync(
            descriptorPath,
            JsonSerializer.Serialize(descriptor, JsonOptions) + "\n");
        artifacts.Add((
            "server/runtime/storage/junexis.storage.deployment.json",
            "server",
            "storage-deployment-manifest"));
        return artifacts;
    }

    private static IReadOnlyList<string> ResolveHostFiles(string hostPath)
    {
        if (!Path.GetExtension(hostPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return [hostPath];
        var directory = Path.GetDirectoryName(hostPath)!;
        var stem = Path.GetFileNameWithoutExtension(hostPath);
        var required = new[]
        {
            hostPath,
            Path.Combine(directory, stem + ".deps.json"),
            Path.Combine(directory, stem + ".runtimeconfig.json"),
            Path.Combine(directory, "Junexis.Storage.Runtime.Abstractions.dll")
        };
        var missing = required.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
            throw new InvalidDataException(
                $"Storage Runtime host package is incomplete; missing '{Path.GetFileName(missing)}'.");
        return required.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task ExtendInventoryAsync(
        string outputRoot,
        IReadOnlyList<(string Path, string Target, string Kind)> runtimeArtifacts)
    {
        var inventoryPath = Path.Combine(outputRoot, "junexis.artifacts.json");
        var inventory = JsonSerializer.Deserialize<ArtifactInventory>(
            await File.ReadAllTextAsync(inventoryPath),
            JsonOptions)
            ?? throw new InvalidDataException("Artifact inventory is empty.");
        if (inventory.SchemaVersion != ArtifactInventory.CurrentVersion)
            throw new InvalidDataException("Artifact inventory has an unsupported schema.");

        var entries = inventory.Artifacts
            .Where(entry => !runtimeArtifacts.Any(runtime =>
                runtime.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var runtime in runtimeArtifacts)
        {
            var file = Path.Combine(
                outputRoot,
                runtime.Path.Replace('/', Path.DirectorySeparatorChar));
            entries.Add(new ArtifactInventoryEntry(
                runtime.Path,
                runtime.Target,
                runtime.Kind,
                "generated-runtime",
                Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file)))));
        }

        var updated = inventory with
        {
            Artifacts = entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray()
        };
        var temporary = inventoryPath + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(updated, JsonOptions));
        File.Move(temporary, inventoryPath, overwrite: true);
    }

    private static void CopyServerSources(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            if (relative.StartsWith("runtime" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(".junexis-cache" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static void TryDeleteOwnedDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)
                && Path.GetFileName(path).StartsWith("junexis-package-server-", StringComparison.Ordinal))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task RunCheckedAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string operation)
    {
        using var process = Process.Start(CreateStartInfo(executable, arguments, workingDirectory))
            ?? throw new InvalidOperationException($"Could not start {operation}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{operation} exceeded 3 minutes.");
        }
        var output = ((await stdout) + Environment.NewLine + (await stderr)).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{operation} failed with exit code {process.ExitCode}: {Limit(output)}");
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var resolvedExecutable = ResolveExecutable(executable) ?? executable;
        if (OperatingSystem.IsWindows() && Path.GetExtension(resolvedExecutable) is ".cmd" or ".bat")
        {
            var command = $"\"{resolvedExecutable}\" {string.Join(" ", arguments.Select(Quote))}";
            var windowsInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /c \"{command}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ConfigureGoCache(windowsInfo, workingDirectory);
            return windowsInfo;
        }

        var info = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        ConfigureGoCache(info, workingDirectory);
        return info;
    }

    private static void ConfigureGoCache(ProcessStartInfo info, string workingDirectory)
    {
        var root = Path.Combine(workingDirectory, ".junexis-cache");
        var goPath = Path.Combine(root, "gopath");
        info.Environment["GOCACHE"] = Path.Combine(root, "go-build");
        info.Environment["GOPATH"] = goPath;
        info.Environment["GOMODCACHE"] = Path.Combine(goPath, "pkg", "mod");
        info.Environment["GOTELEMETRY"] = "off";
        info.Environment["npm_config_cache"] = Path.Combine(root, "npm");
    }

    private static string? ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar))
            return File.Exists(executable) ? executable : null;
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(
                directory,
                executable.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    ? executable
                    : executable + extension);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string Quote(string value) =>
        value.Any(character => char.IsWhiteSpace(character) || character is '&' or '|' or '<' or '>')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string Limit(string value) =>
        value.Length <= 1200 ? value : value[..1200] + "...";
}
