namespace JunexisLang.Compiler.CodeGen.Tests;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Topology;

[CollectionDefinition("M4Distributed", DisableParallelization = true)]
public sealed class M4DistributedCollection { }

[Collection("M4Distributed")]
public sealed partial class CodeGeneratorTests
{
    [Fact]
    [Trait("Category", "M4Distributed")]
    [Trait("Category", "SemanticConformanceBoundary")]
    public async Task TypedExtern_CrossesServerReportBoundaryThroughVersionedWireContractAsync()
    {
        const string source = """
            extern task decorate(value: Text) @server -> Text

            task Publish() @server -> stream<Text> { send decorate("hello-l4") }
            task Status() @server -> Text { return "ok" }
            task Consume(input: stream<Text>) @report { }

            protocol rest { GET("/status", Status()) }
            pipeline Main { use Publish -> Consume.input }
            deployment Test {
                target server { Publish @server {} Status @server {} }
                target report { Consume @report {} }
            }
            """;

        var document = await TransformToIrAsync(source);
        var distributed = DistributedTopologyLowerer.Lower(document);
        Assert.True(distributed.Success);
        Assert.All(distributed.Plan.Placements, placement => Assert.True(placement.Explicit));
        var distributedEdge = Assert.Single(distributed.Plan.Edges);
        Assert.Equal("http", distributedEdge.Protocol);
        Assert.Equal(WireRuntimeContractCatalog.Json.Version, distributedEdge.WireVersion);
        Assert.Equal("server", distributedEdge.SourcePlatform);
        Assert.Equal("report", distributedEdge.TargetPlatform);
        var semantic = TypedSemanticProjector.Project(document);
        Assert.Contains(
            semantic.Tasks.Single(task => task.Name == "Publish").Operations,
            operation => operation.Kind == TypedSemanticOperationKind.ExternalCall && operation.Effect == TypedSemanticEffect.External);
        var root = Path.Combine(Path.GetTempPath(), $"junexis-l4-{Guid.NewGuid():N}");
        var serverDirectory = Path.Combine(root, "server");
        var reportDirectory = Path.Combine(root, "report");
        var bridgePort = GetFreeTcpPort();
        Directory.CreateDirectory(serverDirectory);
        Directory.CreateDirectory(reportDirectory);
        Process? server = null;

        try
        {
            var serverFiles = new GoCodeGenerator().Generate(document, new CodeGenOptions(root));
            var reportFiles = new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions(root));
            Assert.Contains(serverFiles, file => file.Content.Contains("var decorate = func(value string) string", StringComparison.Ordinal));
            Assert.DoesNotContain(reportFiles, file => file.Content.Contains("bindDecorate", StringComparison.Ordinal));

            foreach (var file in serverFiles)
                await File.WriteAllTextAsync(Path.Combine(root, file.FilePath), file.Content);
            foreach (var file in reportFiles)
                await File.WriteAllTextAsync(Path.Combine(root, file.FilePath), file.Content);

            await File.WriteAllTextAsync(Path.Combine(serverDirectory, "extern_binding.go"), """
                package main

                func init() {
                    decorate = func(value string) string { return "extern:" + value }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "runner.ts"), $$"""
                import { parseStreamBridgeEnvelope, pollStreamBridge } from './index.js';

                let rejectsUnknownVersion = false;
                try {
                  parseStreamBridgeEnvelope<string>('{"version":"junexis-wire/9.9","payload":"invalid"}');
                } catch {
                  rejectsUnknownVersion = true;
                }

                for (let attempt = 0; attempt < 100; attempt++) {
                  try {
                    const envelope = await pollStreamBridge<string>('http://127.0.0.1:{{bridgePort}}/stream/Publish');
                    if (envelope?.payload === 'extern:hello-l4' && envelope.version === '{{WireRuntimeContractCatalog.Json.Version}}' && rejectsUnknownVersion) {
                      console.log(JSON.stringify({
                        extern: envelope.payload,
                        wire: envelope.version,
                        boundary: 'server->report',
                        rejectsUnknownVersion
                      }));
                      process.exit(0);
                    }
                  } catch { /* server may still be starting */ }
                  await new Promise(resolve => setTimeout(resolve, 50));
                }
                throw new Error('versioned stream envelope was not received');
                """);
            // Avoid @types/node: only the two process members used by the runner are required.
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "node-globals.d.ts"), """
                declare const process: { exit(code?: number): never };
                """);

            await RunCheckedAsync("go", ["mod", "tidy"], serverDirectory);
            var serverExecutable = Path.Combine(serverDirectory, OperatingSystem.IsWindows() ? "junexis-l4.exe" : "junexis-l4");
            await RunCheckedAsync("go", ["build", "-o", serverExecutable, "."], serverDirectory);
            await RunCheckedAsync(
                OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                OperatingSystem.IsWindows()
                    ? ["/d", "/s", "/c", "npm install --ignore-scripts"]
                    : ["install", "--ignore-scripts"],
                reportDirectory);
            await RunCheckedAsync(
                OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                OperatingSystem.IsWindows()
                    ? ["/d", "/s", "/c", "npm exec tsc -- --noEmit false --outDir dist"]
                    : ["exec", "tsc", "--", "--noEmit", "false", "--outDir", "dist"],
                reportDirectory);

            server = StartProcess(serverExecutable, ["--server-mode", "persistent", "--http-port", bridgePort.ToString()], serverDirectory);
            var clientResult = await RunCheckedAsync("node", [Path.Combine("dist", "runner.js")], reportDirectory);
            Assert.Contains(
                "{\"extern\":\"extern:hello-l4\",\"wire\":\"junexis-wire/0.2\",\"boundary\":\"server->report\",\"rejectsUnknownVersion\":true}",
                clientResult);
        }
        finally
        {
            if (server is { HasExited: false })
            {
                server.Kill(entireProcessTree: true);
                await server.WaitForExitAsync();
            }
            server?.Dispose();
            if (Directory.Exists(root))
                await DeleteDirectoryWithRetryAsync(root);
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task DeleteDirectoryWithRetryAsync(string path)
    {
        const int attempts = 10;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && attempt < attempts)
            {
                await Task.Delay(100 * attempt);
            }
            catch (IOException) when (OperatingSystem.IsWindows() && attempt < attempts)
            {
                await Task.Delay(100 * attempt);
            }
        }

        Directory.Delete(path, recursive: true);
    }

    private static Process StartProcess(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
    }

    private static async Task<string> RunCheckedAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        using var process = StartProcess(executable, arguments, workingDirectory);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        var output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, $"{executable} failed:{Environment.NewLine}{output}");
        return output;
    }
}
