using System.Diagnostics;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class DoctorCommandTests
{
    [Fact]
    public async Task DoctorCommand_ReportsHealthyProject()
    {
        var root = await CreateProjectAsync(
            """
            name = "doctor-test"
            entry = "src/main.jnx"
            targets = ["server"]
            outDir = "out"
            """);

        try
        {
            var result = await RunCliAsync(["doctor"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[ok] project root:", result.StandardOutput);
            Assert.Contains("[ok] manifest:", result.StandardOutput);
            Assert.Contains("[ok] source check: pass", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DoctorCommand_ReturnsFailureForUnknownTarget()
    {
        var root = await CreateProjectAsync(
            """
            name = "doctor-test"
            entry = "src/main.jnx"
            targets = ["server", "mars"]
            outDir = "out"
            """);

        try
        {
            var result = await RunCliAsync(["doctor"], root);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("[error] unknown target in manifest: mars", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DoctorCommand_ReturnsFailureWhenManifestTargetHasNoApplicableTask()
    {
        var root = await CreateProjectAsync(
            """
            name = "doctor-target-mismatch"
            entry = "src/main.jnx"
            targets = ["embedded", "server"]
            outDir = "out"
            """);

        try
        {
            var result = await RunCliAsync(["doctor"], root);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                "[error] target declared in manifest has no applicable task: embedded",
                result.StandardOutput);
            Assert.Contains("[ok] target: server", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DoctorCommand_FileModeExplainsCompatibilityPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-doctor-file-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "single.jnx");

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(source, "task Main() @server { log(\"doctor\") }");

            var result = await RunCliAsync(["doctor", source], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("file mode compatibility path", result.StandardOutput);
            Assert.Contains(".junexis", result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DoctorCommand_WarnsWhenExternFilePointsIntoOutDir()
    {
        var root = await CreateProjectAsync(
            """
            name = "doctor-test"
            entry = "src/main.jnx"
            targets = ["server"]
            outDir = "out"
            """,
            """
            struct TempReading {
                celsius: Float
            }

            extern storage TemperatureStore {
                target server
                language go
                file "out/server/user/storage.go"

                fn save(reading: TempReading)
            }
            """);

        try
        {
            var result = await RunCliAsync(["doctor"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("extern ownership", result.StandardOutput);
            Assert.Contains("file points into generated outDir", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DoctorCommand_ReportsDeveloperOwnedExternFileInUserDirectory()
    {
        var root = await CreateProjectAsync(
            """
            name = "doctor-test"
            entry = "src/main.jnx"
            targets = ["server"]
            outDir = "out"
            """,
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
            """);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "user", "server"));
            await File.WriteAllTextAsync(Path.Combine(root, "user", "server", "storage.go"), "package user");

            var result = await RunCliAsync(["doctor"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("developer-owned file present: user/server/storage.go", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DoctorCommand_ReportsPlatformIoProjectOverlay()
    {
        var root = await CreateProjectAsync(
            """
            name = "doctor-test"
            entry = "src/main.jnx"
            targets = ["embedded"]
            outDir = "out"
            """,
            "task Main() @embedded(esp32c3) { log(\"doctor\") }");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "user", "embedded"));
            await File.WriteAllTextAsync(Path.Combine(root, "user", "embedded", "platformio.ini"), "[platformio]");

            var result = await RunCliAsync(["doctor"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("project overlay: user/embedded/platformio.ini -> out/embedded/platformio.ini", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DoctorCommand_ReportsServerGoModAndEmbeddedCMakeProjectOverlays()
    {
        var root = await CreateProjectAsync(
            """
            name = "doctor-test"
            entry = "src/main.jnx"
            targets = ["embedded", "server"]
            outDir = "out"
            """,
            """
            task EmbeddedMain() @embedded(esp32c3) { log("embedded") }
            task ServerMain() @server { log("server") }
            """);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "user", "embedded"));
            Directory.CreateDirectory(Path.Combine(root, "user", "server"));
            await File.WriteAllTextAsync(Path.Combine(root, "user", "embedded", "CMakeLists.txt"), "cmake_minimum_required(VERSION 3.20)");
            await File.WriteAllTextAsync(Path.Combine(root, "user", "server", "go.mod"), "module example.com/doctor");

            var result = await RunCliAsync(["doctor"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("project overlay: user/embedded/CMakeLists.txt -> out/embedded/CMakeLists.txt", result.StandardOutput);
            Assert.Contains("project overlay: user/server/go.mod -> out/server/go.mod", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateProjectAsync(string manifest, string? source = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-doctor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "junexis.project.toml"), manifest);
        await File.WriteAllTextAsync(Path.Combine(root, "src", "main.jnx"), source ?? "task Main() @server { log(\"doctor\") }");
        return root;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string[] arguments,
        string workingDirectory)
    {
        var assembly = typeof(DoctorHandler).Assembly.Location;
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
