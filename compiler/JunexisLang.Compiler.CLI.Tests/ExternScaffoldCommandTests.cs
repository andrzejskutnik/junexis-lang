using System.Diagnostics;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class ExternScaffoldCommandTests
{
    [Fact]
    public async Task ExternScaffold_CreatesMissingGoExternFile()
    {
        var root = await CreateExternProjectAsync();

        try
        {
            var result = await RunCliAsync(["extern", "scaffold"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("created user/server/storage.go", result.StandardOutput);

            var scaffold = await File.ReadAllTextAsync(Path.Combine(root, "user", "server", "storage.go"));
            Assert.Contains("package user", scaffold);
            Assert.Contains("func TemperatureStore_save(reading any)", scaffold);
            Assert.Contains("func TemperatureStore_recent(limit int64) []any", scaffold);
            Assert.Contains("return nil", scaffold);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExternScaffold_DoesNotOverwriteExistingFile()
    {
        var root = await CreateExternProjectAsync();

        try
        {
            var file = Path.Combine(root, "user", "server", "storage.go");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "package main\n\n// custom implementation\n");

            var result = await RunCliAsync(["extern", "scaffold"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("skip existing user/server/storage.go", result.StandardOutput);
            Assert.Contains("custom implementation", await File.ReadAllTextAsync(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExternScaffold_DryRunDoesNotCreateFile()
    {
        var root = await CreateExternProjectAsync();

        try
        {
            var result = await RunCliAsync(["extern", "scaffold", "--dry-run"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("would create user/server/storage.go", result.StandardOutput);
            Assert.False(File.Exists(Path.Combine(root, "user", "server", "storage.go")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExternScaffold_CreatesMissingCppMotionExternFile()
    {
        var root = await CreateEmbeddedExternBuildProjectAsync();

        try
        {
            var result = await RunCliAsync(["extern", "scaffold"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("created user/embedded/motion_driver.cpp", result.StandardOutput);

            var scaffold = await File.ReadAllTextAsync(Path.Combine(root, "user", "embedded", "motion_driver.cpp"));
            Assert.Contains("Motion externs are safety-critical", scaffold);
            Assert.Contains("void MotionDriver_enable()", scaffold);
            Assert.Contains("void MotionDriver_move_linear(MotionCommand cmd)", scaffold);
            Assert.Contains("MachinePosition MotionDriver_read_position()", scaffold);
            Assert.Contains("return MachinePosition{};", scaffold);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Build_IncludesUserServerGoFilesAsBuildSnapshot()
    {
        var root = await CreateExternBuildProjectAsync();

        try
        {
            var userFile = Path.Combine(root, "user", "server", "storage.go");
            Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
            await File.WriteAllTextAsync(
                userFile,
                """
                package user

                func TemperatureStore_save(reading any) {
                    // developer implementation
                }
                """);

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("include server/user/storage.go <- user/server/storage.go", result.StandardOutput);

            var generatedSnapshot = Path.Combine(root, "out", "server", "user", "storage.go");
            Assert.True(File.Exists(generatedSnapshot));
            Assert.Contains("developer implementation", await File.ReadAllTextAsync(generatedSnapshot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Build_IncludesOrderedSqlMigrationsAsServerSnapshot()
    {
        var root = await CreateExternBuildProjectAsync();

        try
        {
            var migrations = Path.Combine(root, "migrations");
            Directory.CreateDirectory(migrations);
            await File.WriteAllTextAsync(Path.Combine(migrations, "002-index.sql"), "CREATE INDEX idx_reading ON readings(celsius);");
            await File.WriteAllTextAsync(Path.Combine(migrations, "001-initial.sql"), "CREATE TABLE readings(celsius REAL NOT NULL);");

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("include server/migrations/001-initial.sql <- migrations/001-initial.sql", result.StandardOutput);
            Assert.Contains("include server/migrations/002-index.sql <- migrations/002-index.sql", result.StandardOutput);
            Assert.True(File.Exists(Path.Combine(root, "out", "server", "migrations", "001-initial.sql")));
            Assert.True(File.Exists(Path.Combine(root, "out", "server", "migrations", "002-index.sql")));
            Assert.True(File.Exists(Path.Combine(root, "out", "server", "junexis", "storage", "runtime", "client.go")));
            Assert.False(File.Exists(Path.Combine(root, "out", "server", "junexis", "storage", "sqlite", "provider.go")));
            Assert.DoesNotContain("modernc.org/sqlite", await File.ReadAllTextAsync(Path.Combine(root, "out", "server", "go.mod")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Build_IncludesUserEmbeddedCppFilesAsBuildSnapshot()
    {
        var root = await CreateEmbeddedExternBuildProjectAsync();

        try
        {
            var userFile = Path.Combine(root, "user", "embedded", "motion_driver.cpp");
            Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
            await File.WriteAllTextAsync(
                userFile,
                """
                // developer-owned motion driver
                void MotionDriver_enable() {}
                """);

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("include embedded/user/motion_driver.cpp <- user/embedded/motion_driver.cpp", result.StandardOutput);

            var generatedSnapshot = Path.Combine(root, "out", "embedded", "user", "motion_driver.cpp");
            Assert.True(File.Exists(generatedSnapshot));
            Assert.Contains("developer-owned motion driver", await File.ReadAllTextAsync(generatedSnapshot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateExternProjectAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-extern-scaffold-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "extern-scaffold-test"
            entry = "src/main.jnx"
            targets = ["server"]
            outDir = "out"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            """
            struct TempReading {
                celsius: Float
                timestamp: Time
            }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn save(reading: TempReading)
                fn recent(limit: Int) -> List<TempReading>
            }
            """);
        return root;
    }

    private static async Task<string> CreateExternBuildProjectAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-extern-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "extern-build-test"
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
        return root;
    }

    private static async Task<string> CreateEmbeddedExternBuildProjectAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-embedded-extern-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "embedded-extern-build-test"
            entry = "src/main.jnx"
            targets = ["embedded"]
            outDir = "out"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            """
            struct MotionCommand {
                x: Float
            }

            struct MachinePosition {
                x: Float
            }

            extern motion MotionDriver {
                target embedded
                language cpp
                file "user/embedded/motion_driver.cpp"

                fn enable()
                fn move_linear(cmd: MotionCommand)
                fn read_position() -> MachinePosition
            }

            extern task MotionDriver_enable() @embedded
            extern task MotionDriver_move_linear(cmd: MotionCommand) @embedded
            extern task MotionDriver_read_position() @embedded -> MachinePosition

            task Boot() @embedded(esp32c3) {
                MotionDriver_enable()
            }
            """);
        return root;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string[] arguments,
        string workingDirectory)
    {
        var assembly = typeof(ExternScaffoldHandler).Assembly.Location;
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
