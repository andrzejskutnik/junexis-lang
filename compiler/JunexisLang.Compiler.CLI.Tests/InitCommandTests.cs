using System.Diagnostics;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class InitCommandTests
{
    [Fact]
    public async Task InitCommand_CreatesProjectLayout()
    {
        var output = Path.Combine(Path.GetTempPath(), $"junexis-init-{Guid.NewGuid():N}");

        try
        {
            var result = await RunCliAsync("init", output, "--name", "temperature-monitor");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(output, "junexis.project.toml")));
            Assert.True(File.Exists(Path.Combine(output, "src", "main.jnx")));
            Assert.True(Directory.Exists(Path.Combine(output, "out", "server")));
            Assert.True(Directory.Exists(Path.Combine(output, "out", "embedded")));
            Assert.True(Directory.Exists(Path.Combine(output, "out", "report")));
            Assert.True(Directory.Exists(Path.Combine(output, "out", "deployment")));
            Assert.True(Directory.Exists(Path.Combine(output, ".junexis", "cache")));
            Assert.True(Directory.Exists(Path.Combine(output, ".junexis", "build")));
            Assert.True(Directory.Exists(Path.Combine(output, ".junexis", "diagnostics")));

            var manifest = await File.ReadAllTextAsync(Path.Combine(output, "junexis.project.toml"));
            Assert.Contains("name = \"temperature-monitor\"", manifest);
            Assert.Contains("entry = \"src/main.jnx\"", manifest);
            Assert.Contains("targets = [\"server\"]", manifest);
            Assert.Contains("outDir = \"out\"", manifest);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task InitCommand_RefusesNonEmptyDirectoryWithoutForce()
    {
        var output = Path.Combine(Path.GetTempPath(), $"junexis-init-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "existing.txt"), "user-owned");

            var result = await RunCliAsync("init", output);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("target directory is not empty", result.StandardError);
            Assert.False(File.Exists(Path.Combine(output, "junexis.project.toml")));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task InitCommand_WithoutPath_UsesCurrentDirectoryName()
    {
        var output = Path.Combine(Path.GetTempPath(), $"junexis-init-default-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(output);

            var result = await RunCliAsync(["init"], output);

            Assert.Equal(0, result.ExitCode);
            var manifest = await File.ReadAllTextAsync(Path.Combine(output, "junexis.project.toml"));
            Assert.Contains($"name = \"{Path.GetFileName(output)}\"", manifest);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        params string[] arguments) =>
        await RunCliAsync(arguments, workingDirectory: null);

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string[] arguments,
        string? workingDirectory)
    {
        var assembly = typeof(InitHandler).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Junexis CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await standardOutput, await standardError);
    }
}
