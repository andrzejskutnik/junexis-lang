using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class RunCommandTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public async Task InvalidDuration_IsRejectedBeforeProjectResolutionAsync(int duration)
    {
        var exitCode = await RunHandler.HandleAsync("missing-project", duration, "missing-go");

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ServerProject_RejectsExitBeforeReadinessAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "junexis.project.toml"),
                """
                name = "run-test"
                entry = "src/main.jnx"
                targets = ["server"]
                outDir = "out"
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "src", "main.jnx"),
                """
                task Pulse() @server {
                    every 1s { log("tick") }
                }
                """);

            var fakeGo = await CreateFakeGoAsync(root);
            var exitCode = await RunHandler.HandleAsync(root, 7, fakeGo);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(Path.Combine(root, "out", "server", "main.go")));
            var invocation = await File.ReadAllTextAsync(Path.Combine(root, "go-invocation.txt"));
            Assert.Contains("run . --server-mode persistent", invocation);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ApplicationPorts_PlaceReportAfterCompleteTcpBridgeRangeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-run-ports-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "server"));
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "server", "main.go"),
                "listenerA := *tcpBasePort+0\nlistenerB := *tcpBasePort+3\n");

            var ports = RunHandler.ReserveApplicationPortSet(root);

            Assert.Equal(ports.HttpPort + 1, ports.HealthPort);
            Assert.Equal(ports.HttpPort + 2, ports.TcpPort);
            Assert.Equal(ports.TcpPort + 4, ports.ReportPort);
        }
        finally
        {
            TryDelete(root);
        }
    }

    /// <summary>Only an explicit local panel path can become a browser address.</summary>
    [Theory]
    [InlineData("/laboratory/", true)]
    [InlineData("/custom-panel/?tab=1", true)]
    [InlineData("https://example.com/", false)]
    [InlineData("//example.com/", false)]
    [InlineData("/\\example.com", false)]
    [InlineData("", false)]
    public void PanelDeclaration_RequiresLocalPath(string entrypoint, bool valid)
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-panel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(RunHandler.ReadServerPanelPath(root));
            File.WriteAllText(Path.Combine(root, "laboratory.json"),
                System.Text.Json.JsonSerializer.Serialize(new { entrypoint }));
            if (valid) Assert.Equal(entrypoint, RunHandler.ReadServerPanelPath(root));
            else Assert.Throws<InvalidDataException>(() => RunHandler.ReadServerPanelPath(root));
        }
        finally { TryDelete(root); }
    }

    private static async Task<string> CreateFakeGoAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-go.cmd");
            await File.WriteAllTextAsync(
                path,
                """
                @echo off
                echo %* > "%~dp0go-invocation.txt"
                exit /b 0
                """);
            return path;
        }

        var shellPath = Path.Combine(root, "fake-go.sh");
        await File.WriteAllTextAsync(
            shellPath,
            """
            #!/usr/bin/env sh
            printf '%s' "$*" > "$(dirname "$0")/go-invocation.txt"
            exit 0
            """);
        File.SetUnixFileMode(shellPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }
}
