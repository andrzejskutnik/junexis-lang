using System.Diagnostics;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class CleanCommandTests
{
    [Fact]
    public async Task CleanCommand_RemovesGeneratedProjectArtifactsButKeepsSourceManifestAndCache()
    {
        var root = await CreateProjectWithArtifactsAsync();

        try
        {
            var result = await RunCliAsync(["clean"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.False(Directory.Exists(Path.Combine(root, "out")));
            Assert.False(Directory.Exists(Path.Combine(root, ".junexis", "build")));
            Assert.False(Directory.Exists(Path.Combine(root, ".junexis", "diagnostics")));
            Assert.False(Directory.Exists(Path.Combine(root, ".junexis", "state")));
            Assert.True(Directory.Exists(Path.Combine(root, ".junexis", "cache")));
            Assert.True(File.Exists(Path.Combine(root, "junexis.project.toml")));
            Assert.True(File.Exists(Path.Combine(root, "src", "main.jnx")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CleanCommand_DryRunDoesNotRemoveArtifacts()
    {
        var root = await CreateProjectWithArtifactsAsync();

        try
        {
            var result = await RunCliAsync(["clean", "--dry-run"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("would remove", result.StandardOutput);
            Assert.True(Directory.Exists(Path.Combine(root, "out")));
            Assert.True(Directory.Exists(Path.Combine(root, ".junexis", "build")));
            Assert.True(Directory.Exists(Path.Combine(root, ".junexis", "diagnostics")));
            Assert.True(Directory.Exists(Path.Combine(root, ".junexis", "state")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateProjectWithArtifactsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-clean-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "out", "server"));
        Directory.CreateDirectory(Path.Combine(root, ".junexis", "cache"));
        Directory.CreateDirectory(Path.Combine(root, ".junexis", "build"));
        Directory.CreateDirectory(Path.Combine(root, ".junexis", "diagnostics"));
        Directory.CreateDirectory(Path.Combine(root, ".junexis", "state"));

        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "clean-test"
            entry = "src/main.jnx"
            targets = ["server"]
            outDir = "out"
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "src", "main.jnx"), "task Main() @server { log(\"clean\") }");
        await File.WriteAllTextAsync(Path.Combine(root, "out", "server", "main.go"), "package main");
        await File.WriteAllTextAsync(Path.Combine(root, ".junexis", "cache", "index.bin"), "cache");
        await File.WriteAllTextAsync(Path.Combine(root, ".junexis", "build", "tmp.bin"), "build");
        await File.WriteAllTextAsync(Path.Combine(root, ".junexis", "diagnostics", "last.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(root, ".junexis", "state", "latest-build.json"), "{}");

        return root;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string[] arguments,
        string workingDirectory)
    {
        var assembly = typeof(CleanHandler).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
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
