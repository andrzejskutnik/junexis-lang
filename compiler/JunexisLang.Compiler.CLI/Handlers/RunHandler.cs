using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>Builds and runs a server project through one bounded Junexis command.</summary>
internal static class RunHandler
{
    internal static async Task<int> HandleAsync(
        string? inputPath,
        int durationSeconds,
        string goExecutable,
        string npmExecutable = "npm",
        string nodeExecutable = "node",
        bool noOpen = false,
        string? storageRuntimeHost = null)
    {
        if (durationSeconds is < 1 or > 300)
        {
            await Console.Error.WriteLineAsync("error: --duration must be between 1 and 300 seconds");
            return 2;
        }

        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(inputPath);
        }
        catch (CliInputException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        var runnableTargets = (resolved.Targets ?? ["server"])
            .Where(target => target is "server" or "report")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (!runnableTargets.Contains("server", StringComparer.Ordinal))
        {
            await Console.Error.WriteLineAsync(
                "error: junexis run currently requires the project target 'server'");
            return 1;
        }

        var buildExitCode = await CompileHandler.HandleAsync(
            inputPath,
            outDir: null,
            targets: runnableTargets,
            json: false,
            outputSchema: "1.0",
            expectedBuildId: null);
        if (buildExitCode != 0)
            return buildExitCode;

        StorageRuntimeBindingPlan? storageBinding;
        try
        {
            storageBinding = await StorageRuntimeBindingPlanner.CreateAsync(
                resolved,
                storageRuntimeHost,
                resolveConfiguration: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Console.Error.WriteLineAsync($"error: storage launch preflight failed: {ex.Message}");
            return 1;
        }
        if (storageBinding is not null)
        {
            await Console.Out.WriteLineAsync(
                $"VERIFIED storage · {storageBinding.Binding.Binding.Name} -> " +
                $"{storageBinding.Binding.Package.Manifest.Id}@{storageBinding.Binding.Package.Manifest.Version} · " +
                $"sha256:{storageBinding.Binding.Package.PackageSha256[..12]}");
        }

        var serverDirectory = Path.Combine(resolved.OutDirectory!.FullName, "server");
        var mainPath = Path.Combine(serverDirectory, "main.go");
        var modulePath = Path.Combine(serverDirectory, "go.mod");
        if (!File.Exists(mainPath) || !File.Exists(modulePath))
        {
            await Console.Error.WriteLineAsync(
                "error: server build did not produce server/main.go and server/go.mod");
            return 1;
        }

        var hasReport = runnableTargets.Contains("report", StringComparer.Ordinal);
        string? serverPanelPath;
        try
        {
            serverPanelPath = ReadServerPanelPath(resolved.Project?.RootDirectory.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            await Console.Error.WriteLineAsync($"error: invalid panel declaration: {ex.Message}");
            return 1;
        }

        var reportDirectory = Path.Combine(resolved.OutDirectory.FullName, "report");
        if (hasReport && !HasReportApplication(reportDirectory))
        {
            await Console.Error.WriteLineAsync(
                "error: report build did not produce a complete browser application");
            return 1;
        }

        return await RunApplicationAsync(
            serverDirectory,
            hasReport ? reportDirectory : null,
            serverPanelPath,
            durationSeconds,
            goExecutable,
            npmExecutable,
            nodeExecutable,
            noOpen,
            storageBinding);
    }

    internal static string? ReadServerPanelPath(string? projectRoot)
    {
        if (projectRoot is null) return null;
        var path = Path.Combine(projectRoot, "laboratory.json");
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("laboratory.json must contain an object.");
        if (!document.RootElement.TryGetProperty("entrypoint", out var entrypoint)) return null;
        var value = entrypoint.ValueKind == JsonValueKind.String ? entrypoint.GetString() : null;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('/') || value.StartsWith("//")
            || value.Contains('\\') || value.Any(char.IsControl)
            || !Uri.TryCreate("http://127.0.0.1:12345" + value, UriKind.Absolute, out var uri)
            || uri.Host != "127.0.0.1" || uri.Port != 12345)
            throw new InvalidDataException("laboratory.json entrypoint must be a local absolute path.");
        return value;
    }

    private static async Task<int> RunApplicationAsync(
        string serverDirectory,
        string? reportDirectory,
        string? serverPanelPath,
        int durationSeconds,
        string goExecutable,
        string npmExecutable,
        string nodeExecutable,
        bool noOpen,
        StorageRuntimeBindingPlan? storageBinding)
    {
        var processes = new List<OwnedProcess>();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            await Console.Out.WriteLineAsync("Building generated application...");
            await RunCheckedAsync(
                goExecutable,
                ["mod", "tidy"],
                serverDirectory,
                "server dependency resolution",
                configureGo: true, cancellationToken: cancellation.Token);
            if (reportDirectory is not null)
            {
                await RunCheckedAsync(
                    npmExecutable,
                    ["install", "--ignore-scripts"],
                    reportDirectory,
                    "report dependency installation", cancellationToken: cancellation.Token);
                await RunCheckedAsync(
                    npmExecutable,
                    ["run", "app:build"],
                    reportDirectory,
                    "report application build", cancellationToken: cancellation.Token);
            }

            var (httpPort, healthPort, tcpPort, reportPort) =
                ReserveApplicationPortSet(Path.GetDirectoryName(serverDirectory)!);
            var serverUrl = $"http://127.0.0.1:{httpPort}";
            var panelUrl = reportDirectory is not null
                ? $"http://127.0.0.1:{reportPort}/?server={Uri.EscapeDataString(serverUrl)}"
                : serverPanelPath is not null ? serverUrl + serverPanelPath : null;

            var serverInfo = CreateStartInfo(
                goExecutable,
                serverDirectory,
                [
                    "run", ".",
                    "--server-mode", "persistent",
                    "--http-port", httpPort.ToString(CultureInfo.InvariantCulture),
                    "--health-port", healthPort.ToString(CultureInfo.InvariantCulture),
                    "--tcp-port", tcpPort.ToString(CultureInfo.InvariantCulture)
                ]);
            ConfigureGoEnvironment(
                serverInfo,
                Path.Combine(serverDirectory, ".junexis-cache", "go-build"));
            storageBinding?.ApplyTo(serverInfo);
            var server = StartOwnedProcess(serverInfo, "server");
            processes.Add(server);
            await WaitForHealthAsync(
                server.Process,
                $"http://127.0.0.1:{healthPort}/health/ready",
                "server", cancellation.Token);
            await Console.Out.WriteLineAsync(
                $"READY server · {serverUrl} · tcp:{tcpPort} · health:{healthPort}");

            if (reportDirectory is not null)
            {
                var reportInfo = CreateStartInfo(
                    nodeExecutable,
                    reportDirectory,
                    ["serve.mjs"]);
                reportInfo.Environment["JUNEXIS_REPORT_PORT"] =
                    reportPort.ToString(CultureInfo.InvariantCulture);
                reportInfo.Environment["JUNEXIS_SERVER_URL"] = serverUrl;
                var report = StartOwnedProcess(reportInfo, "report");
                processes.Add(report);
                await WaitForHealthAsync(
                    report.Process,
                    $"http://127.0.0.1:{reportPort}/health/ready",
                    "report application", cancellation.Token);

            }
            if (panelUrl is not null)
            {
                await WaitForHealthAsync(processes[^1].Process, panelUrl, "panel", cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (processes.Any(item => item.Process.HasExited))
                    throw new InvalidOperationException("Application exited before panel readiness.");
                await Console.Out.WriteLineAsync("JUNEXIS_READY " + JsonSerializer.Serialize(new
                {
                    schema = "junexis.panel-ready/1",
                    sessionId = Guid.NewGuid().ToString("D"),
                    kind = reportDirectory is null ? "server" : "report",
                    url = panelUrl
                }));
                if (reportDirectory is not null)
                    await Console.Out.WriteLineAsync($"READY report · {panelUrl}");
                if (!noOpen) TryOpenBrowser(panelUrl);
            }
            else
                await Console.Out.WriteLineAsync("RUNNING server without a declared panel");

            await Console.Out.WriteLineAsync(
                $"RUNNING Junexis application · {durationSeconds}s · Ctrl+C stops all owned processes");
            var duration = Task.Delay(TimeSpan.FromSeconds(durationSeconds), cancellation.Token);
            var exits = processes.Select(item => item.Process.WaitForExitAsync()).ToArray();
            var completed = await Task.WhenAny([duration, .. exits]);
            if (completed != duration)
                throw new InvalidOperationException(
                    "A Junexis application process exited before the bounded session completed.");

            await Console.Out.WriteLineAsync("STOPPING Junexis application");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or IOException
                or HttpRequestException
                or TaskCanceledException
                or TimeoutException)
        {
            await Console.Error.WriteLineAsync($"error: could not run Junexis application: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            foreach (var process in processes.AsEnumerable().Reverse())
                await StopAsync(process);
            if (processes.Count > 0)
                await Console.Out.WriteLineAsync("STOPPED all owned Junexis processes");
        }
    }

    private static bool HasReportApplication(string directory) =>
        new[]
        {
            "index.html",
            "package.json",
            "serve.mjs",
            "browser.ts",
            "report-view.ts",
            "report.css",
            "junexis-report-manifest.json"
        }.All(file => File.Exists(Path.Combine(directory, file)));

    private static async Task RunCheckedAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string operation,
        bool configureGo = false,
        CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(executable, workingDirectory, arguments);
        startInfo.Environment["npm_config_cache"] = Path.Combine(
            workingDirectory, ".junexis-cache", "npm");
        if (configureGo)
            ConfigureGoEnvironment(
                startInfo,
                Path.Combine(workingDirectory, ".junexis-cache", "go-build"));
        using var process = new Process
        {
            StartInfo = startInfo
        };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start {operation}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{operation} exceeded 3 minutes.");
        }

        var output = ((await stdout) + Environment.NewLine + (await stderr)).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{operation} failed with exit code {process.ExitCode}: {Limit(output)}");
    }

    private static OwnedProcess StartOwnedProcess(ProcessStartInfo startInfo, string label)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {label}.");
        return new OwnedProcess(
            process,
            PumpAsync(process.StandardOutput, label),
            PumpAsync(process.StandardError, label));
    }

    private static async Task PumpAsync(StreamReader reader, string label)
    {
        while (await reader.ReadLineAsync() is { } line)
            await Console.Out.WriteLineAsync($"[{label}] {line}");
    }

    private static async Task WaitForHealthAsync(
        Process process,
        string healthUrl,
        string label,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"{label} exited before readiness with code {process.ExitCode}.");
            try
            {
                using var response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException($"{label} readiness probe timed out.");
    }

    internal static (int HttpPort, int HealthPort, int TcpPort, int ReportPort)
        ReserveApplicationPortSet(string outputDirectory)
    {
        var tcpPortCount = LocalDeploymentHandler.RequiredTcpBridgePortCount(outputDirectory);
        var portCount = checked(3 + tcpPortCount);
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var basePort = Random.Shared.Next(20000, 60000 - portCount);
            var listeners = new List<TcpListener>(portCount);
            try
            {
                for (var offset = 0; offset < portCount; offset++)
                {
                    var listener = new TcpListener(IPAddress.Any, basePort + offset);
                    listener.Server.ExclusiveAddressUse = true;
                    listener.Start();
                    listeners.Add(listener);
                }
                return (
                    basePort,
                    basePort + 1,
                    basePort + 2,
                    basePort + 2 + tcpPortCount);
            }
            catch (SocketException)
            {
            }
            finally
            {
                foreach (var listener in listeners)
                    listener.Stop();
            }
        }
        throw new InvalidOperationException(
            $"Could not reserve ports for the server, {tcpPortCount} TCP bridge listener(s), and report application.");
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            Console.Out.WriteLine("OPENED panel in the default browser");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"warning: could not open the browser automatically: {ex.Message}");
        }
    }

    private static async Task StopAsync(OwnedProcess owned)
    {
        try
        {
            if (!owned.Process.HasExited)
                owned.Process.Kill(entireProcessTree: true);
            await owned.Process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            await Task.WhenAll(owned.Output, owned.Error);
            owned.Process.Dispose();
        }
    }

    private static string Limit(string value) =>
        value.Length <= 1200 ? value : value[..1200] + "...";

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var resolvedExecutable = ResolveExecutable(executable) ?? executable;
        if (OperatingSystem.IsWindows() &&
            Path.GetExtension(resolvedExecutable) is ".cmd" or ".bat")
        {
            var command = $"\"{resolvedExecutable}\" {string.Join(" ", arguments.Select(QuoteCommandArgument))}";
            return new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /c \"{command}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static void ConfigureGoEnvironment(ProcessStartInfo startInfo, string goCacheDirectory)
    {
        var cacheRoot = Path.GetDirectoryName(goCacheDirectory) ?? goCacheDirectory;
        var goPath = Path.Combine(cacheRoot, "gopath");
        startInfo.Environment["GOCACHE"] = goCacheDirectory;
        startInfo.Environment["GOPATH"] = goPath;
        startInfo.Environment["GOMODCACHE"] = Path.Combine(goPath, "pkg", "mod");
        startInfo.Environment["GOTELEMETRY"] = "off";
    }

    private static string QuoteCommandArgument(string argument) =>
        argument.Any(character => char.IsWhiteSpace(character) || character is '&' or '|' or '<' or '>')
            ? $"\"{argument.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : argument;

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
            var candidate = Path.Combine(directory, executable.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? executable
                : executable + extension);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private sealed record OwnedProcess(Process Process, Task Output, Task Error);
}
