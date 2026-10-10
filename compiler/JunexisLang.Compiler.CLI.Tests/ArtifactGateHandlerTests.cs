using System.Reflection;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

/// <summary>Tests deterministic failures before the native M4 toolchains start.</summary>
public sealed class ArtifactGateHandlerTests
{
    private static string ExamplesDirectory => Path.GetFullPath(
        Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..", "..", "examples"));

    /// <summary>An unknown target is rejected with a non-zero exit code.</summary>
    [Fact]
    public async Task UnknownTarget_ReturnsFailureAsync()
    {
        var input = new FileInfo(Path.Combine(ExamplesDirectory, "05-simple-sensor.jnx"));

        var exitCode = await ArtifactGateHandler.HandleAsync(input.FullName, ["rust"], "missing", "missing", "missing");

        Assert.NotEqual(0, exitCode);
    }

    /// <summary>An explicit target without applicable tasks is not reported as a successful gate.</summary>
    [Fact]
    public async Task TargetWithoutTasks_ReturnsFailureAsync()
    {
        var input = new FileInfo(Path.Combine(ExamplesDirectory, "05-simple-sensor.jnx"));

        var exitCode = await ArtifactGateHandler.HandleAsync(input.FullName, ["server"], "missing", "missing", "missing");

        Assert.NotEqual(0, exitCode);
    }

    /// <summary>Server artifact gate snapshots developer-owned extern Go files before running the toolchain.</summary>
    [Fact]
    public async Task ServerExternUserFiles_AreAvailableToNativeToolchainAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-artifact-gate-extern-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "user", "server"));
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "junexis.project.toml"),
                """
                name = "artifact-gate-extern-test"
                entry = "src/main.jnx"
                targets = ["server"]
                outDir = "out"
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "src", "main.jnx"),
                """
                struct TempReading {
                    celsius: Float
                }

                extern storage TemperatureStore {
                    target server
                    language go
                    file "user/server/storage.go"

                    fn save(reading: TempReading)
                }

                extern task TemperatureStore_save(reading: TempReading) @server

                task Ingest(reading: TempReading) @server {
                    TemperatureStore_save(reading)
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "user", "server", "storage.go"),
                """
                package user

                func TemperatureStore_save(reading any) {
                }
                """);

            var fakeGo = await CreateFakeGoAsync(root);
            var exitCode = await ArtifactGateHandler.HandleAsync(root, ["server"], fakeGo, "missing", "missing");

            Assert.Equal(0, exitCode);
        }
        finally
        {
            TryDelete(root);
        }
    }

    /// <summary>The native gate analyzes the composed project rather than only its entry file.</summary>
    [Fact]
    public async Task ProjectImports_AreComposedBeforeNativeGateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-artifact-gate-modules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src", "workers"));
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "junexis.project.toml"),
                """
                name = "artifact-gate-modules-test"
                entry = "src/main.jnx"
                targets = ["server"]
                outDir = "out"
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "src", "main.jnx"),
                """
                import workers::processor
                pipeline MainPipeline { use ProcessObservation; }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "src", "workers", "processor.jnx"),
                "task ProcessObservation() @server { log(\"imported worker\") }");

            var fakeGo = await CreateSuccessfulToolAsync(root, "fake-go");
            var exitCode = await ArtifactGateHandler.HandleAsync(root, ["server"], fakeGo, "missing", "missing");

            Assert.Equal(0, exitCode);
        }
        finally
        {
            TryDelete(root);
        }
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
                if not exist "%CD%\user\storage.go" exit /b 23
                exit /b 0
                """);
            return path;
        }

        var shellPath = Path.Combine(root, "fake-go.sh");
        await File.WriteAllTextAsync(
            shellPath,
            """
            #!/usr/bin/env sh
            test -f "$PWD/user/storage.go" || exit 23
            exit 0
            """);
        File.SetUnixFileMode(shellPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static async Task<string> CreateSuccessfulToolAsync(string root, string name)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, $"{name}.cmd");
            await File.WriteAllTextAsync(path, "@echo off\r\nexit /b 0\r\n");
            return path;
        }

        var shellPath = Path.Combine(root, $"{name}.sh");
        await File.WriteAllTextAsync(shellPath, "#!/usr/bin/env sh\nexit 0\n");
        File.SetUnixFileMode(shellPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }
}
