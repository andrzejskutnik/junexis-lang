using System.Diagnostics;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class PackageCommandTests
{
    [Fact]
    [Trait("Issue", "38")]
    public async Task Package_ProjectFirstFlow_CreatesVerifiedBundle()
    {
        var root = await CreateProjectAsync();
        var bundlePath = Path.Combine(root, "system.package.zip");
        try
        {
            var fakeGo = await CreateFakeGoAsync(root);

            var exitCode = await PackageHandler.HandleAsync(
                root,
                new FileInfo(bundlePath),
                ["server"],
                fakeGo,
                "missing",
                "missing");

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(bundlePath));
            Assert.True(File.Exists(
                PackageReceiptContract.DefaultPath(bundlePath)));
            Assert.True(File.Exists(
                Path.Combine(root, ".junexis", "state", "latest-build.json")));
            Assert.False(File.Exists(
                Path.Combine(root, "out", "analysis", "junexis.analysis.preflight.json")));
            var verified = await SystemBundleContract.VerifyAsync(bundlePath);
            Assert.Equal("m7-package", verified.Descriptor.ProjectName);
            Assert.Equal(["server"], verified.Descriptor.Targets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "38")]
    public async Task Package_WhenNativeGateFails_DoesNotPublishBuildOrBundle()
    {
        var root = await CreateProjectAsync();
        var bundlePath = Path.Combine(root, "must-not-exist.zip");
        try
        {
            var failingGo = await CreateFakeGoAsync(root, exitCode: 23);

            var exitCode = await PackageHandler.HandleAsync(
                root,
                new FileInfo(bundlePath),
                ["server"],
                failingGo,
                "missing",
                "missing");

            Assert.Equal(23, exitCode);
            Assert.False(File.Exists(bundlePath));
            Assert.False(File.Exists(
                Path.Combine(root, ".junexis", "state", "latest-build.json")));
            Assert.False(File.Exists(
                Path.Combine(root, "out", "analysis", "junexis.analysis.preflight.json")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "38")]
    public async Task Package_WhenSourceChangesDuringGate_RejectsBoundBuild()
    {
        var root = await CreateProjectAsync();
        var bundlePath = Path.Combine(root, "stale-source.zip");
        try
        {
            var mutatingGo = await CreateFakeGoAsync(root, mutateSource: true);

            var exitCode = await PackageHandler.HandleAsync(
                root,
                new FileInfo(bundlePath),
                ["server"],
                mutatingGo,
                "missing",
                "missing");

            Assert.NotEqual(0, exitCode);
            Assert.False(File.Exists(bundlePath));
            Assert.False(File.Exists(
                Path.Combine(root, ".junexis", "state", "latest-build.json")));
            Assert.False(File.Exists(
                Path.Combine(root, "out", "analysis", "junexis.analysis.preflight.json")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "38")]
    public async Task Package_RejectsFileMode()
    {
        var root = await CreateProjectAsync();
        try
        {
            var fakeGo = await CreateFakeGoAsync(root);

            var exitCode = await PackageHandler.HandleAsync(
                Path.Combine(root, "src", "main.jnx"),
                null,
                ["server"],
                fakeGo,
                "missing",
                "missing");

            Assert.NotEqual(0, exitCode);
            Assert.False(File.Exists(
                Path.Combine(root, ".junexis", "state", "latest-build.json")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "38")]
    public async Task PackageCommand_WorksThroughProcessBoundary()
    {
        var root = await CreateProjectAsync();
        var bundlePath = Path.Combine(root, "cli.package.zip");
        try
        {
            var fakeGo = await CreateFakeGoAsync(root);

            var result = await RunCliAsync(
                [
                    "package", root,
                    "--target", "server",
                    "--out", bundlePath,
                    "--go-exe", fakeGo
                ],
                root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Package succeeded:", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Package receipt:", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Build ID:", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Bundle ID:", result.StandardOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(bundlePath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<string> CreateProjectAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"junexis-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "m7-package"
            entry = "src/main.jnx"
            targets = ["server"]
            outDir = "out"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            """
            task Main() @server {
                log("M7 package");
            }

            pipeline MainPipeline {
                use Main;
            }
            """);
        return root;
    }

    private static async Task<string> CreateFakeGoAsync(
        string root,
        int exitCode = 0,
        bool mutateSource = false)
    {
        var sourcePath = Path.Combine(root, "src", "main.jnx");
        var markerPath = Path.Combine(root, ".fake-go-mutated");
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-go.cmd");
            var mutation = mutateSource
                ? $"""
                  if not exist "{markerPath}" (
                    type nul > "{markerPath}"
                    echo // changed during native package preflight>>"{sourcePath}"
                  )
                  """
                : string.Empty;
            await File.WriteAllTextAsync(
                path,
                $"""
                @echo off
                {mutation}
                exit /b {exitCode}
                """);
            return path;
        }

        var shellPath = Path.Combine(root, "fake-go.sh");
        var shellMutation = mutateSource
            ? $"""
              if [ ! -f "{markerPath}" ]; then
                touch "{markerPath}"
                printf '\n// changed during native package preflight\n' >> "{sourcePath}"
              fi
              """
            : string.Empty;
        await File.WriteAllTextAsync(
            shellPath,
            $"""
            #!/usr/bin/env sh
            {shellMutation}
            exit {exitCode}
            """);
        File.SetUnixFileMode(
            shellPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string[] arguments,
        string workingDirectory)
    {
        var assembly = typeof(CompileHandler).Assembly.Location;
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Junexis CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await standardOutput, await standardError);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
