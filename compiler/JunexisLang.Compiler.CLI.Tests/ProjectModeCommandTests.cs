using System.Diagnostics;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class ProjectModeCommandTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task CheckCommand_WithoutInput_UsesDiscoveredProjectManifest()
    {
        var root = await CreateProjectAsync();

        try
        {
            var result = await RunCliAsync(["check"], root);

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_WithoutInput_UsesProjectEntryTargetsAndOutDir()
    {
        var root = await CreateProjectAsync();

        try
        {
            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(root, "out", "server", "main.go")));
            Assert.False(Directory.Exists(Path.Combine(root, "out", "embedded")));
            Assert.False(Directory.Exists(Path.Combine(root, "out", "report")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_WithProjectDirectoryInput_UsesProjectManifest()
    {
        var root = await CreateProjectAsync();
        var caller = Path.Combine(Path.GetTempPath(), $"junexis-project-caller-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(caller);

            var result = await RunCliAsync(["build", root], caller);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(root, "out", "server", "main.go")));
        }
        finally
        {
            if (Directory.Exists(caller))
                Directory.Delete(caller, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckCommand_ProjectExample01_UsesManifestEntry()
    {
        var project = Path.Combine(RepositoryRoot, "examples", "projects", "01-temperature-monitor");

        var result = await RunCliAsync(["check", project], RepositoryRoot);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task CheckCommand_RejectsEntryOutsideConfiguredSourceRoots()
    {
        var root = await CreateProjectAsync();

        try
        {
            var manifestPath = Path.Combine(root, "junexis.project.toml");
            var manifest = await File.ReadAllTextAsync(manifestPath);
            await File.WriteAllTextAsync(
                manifestPath,
                manifest.Replace("entry = \"src/main.jnx\"", "entry = \"main.jnx\"", StringComparison.Ordinal));
            File.Move(Path.Combine(root, "src", "main.jnx"), Path.Combine(root, "main.jnx"));

            var result = await RunCliAsync(["check"], root);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("outside configured source roots", result.StandardError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckCommand_ResolvesImportsAcrossProjectFiles()
    {
        var root = await CreateProjectAsync();

        try
        {
            var domainDirectory = Path.Combine(root, "src", "domain");
            Directory.CreateDirectory(domainDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(domainDirectory, "types.jnx"),
                "struct Observation { value: Float }");
            var mainPath = Path.Combine(root, "src", "main.jnx");
            var main = await File.ReadAllTextAsync(mainPath);
            await File.WriteAllTextAsync(mainPath, $"import domain::types{Environment.NewLine}{Environment.NewLine}{main}");

            var result = await RunCliAsync(["check"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("E0004", result.StandardError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckCommand_ResolvesSelectivelyAliasedTypeFromImportedModule()
    {
        var root = await CreateProjectAsync(source:
            """
            import domain::types { Observation as Reading }

            task Main(reading: Reading) @server {
                log("project mode");
            }

            pipeline MainPipeline {
                use Main;
            }
            """, targets: "[\"server\"]");

        try
        {
            var domainDirectory = Path.Combine(root, "src", "domain");
            Directory.CreateDirectory(domainDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(domainDirectory, "types.jnx"),
                "struct Observation { value: Float }");

            var result = await RunCliAsync(["check"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("E0021", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_RejectsMissingImportedModuleBeforeGeneratingArtifacts()
    {
        var root = await CreateProjectAsync();

        try
        {
            var mainPath = Path.Combine(root, "src", "main.jnx");
            var main = await File.ReadAllTextAsync(mainPath);
            await File.WriteAllTextAsync(mainPath, $"import missing::module{Environment.NewLine}{Environment.NewLine}{main}");

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("E0004", result.StandardError);
            Assert.False(Directory.Exists(Path.Combine(root, "out", "server")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_GeneratesImportedServerTaskAndEntryPipeline()
    {
        var root = await CreateProjectAsync(source:
            """
            import workers::processor

            pipeline MainPipeline {
                use ProcessObservation;
            }
            """, targets: "[\"server\"]");

        try
        {
            var workers = Path.Combine(root, "src", "workers");
            Directory.CreateDirectory(workers);
            await File.WriteAllTextAsync(
                Path.Combine(workers, "processor.jnx"),
                """
                task ProcessObservation() @server {
                    log("imported worker");
                }
                """);

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            var generated = await File.ReadAllTextAsync(Path.Combine(root, "out", "server", "main.go"));
            Assert.Contains("ProcessObservation", generated);
            Assert.Contains("imported worker", generated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("import data::home")]
    [InlineData("import data::home { HomeData }")]
    public async Task BuildCommand_BindsNamedQueryFromImportedStorageModule(string importDeclaration)
    {
        var root = await CreateProjectAsync(source:
            $$"""
            {{importDeclaration}}

            task ReadHome(homeId: Uuid) @server -> List<HomeData_ByHomeRow> {
                return HomeData_ByHome(homeId: homeId)
            }
            """, targets: "[\"server\"]");

        try
        {
            var data = Path.Combine(root, "src", "data");
            Directory.CreateDirectory(data);
            await File.WriteAllTextAsync(
                Path.Combine(data, "home.jnx"),
                """
                storage HomeData {
                    entity Observation {
                        id: Uuid
                        homeId: Uuid
                        sensor: Text
                        key(id)
                    }
                    query ByHome(homeId: Uuid) from Observation {
                        filter homeId == params.homeId
                        project sensor
                    }
                }
                """);

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            var generated = await File.ReadAllTextAsync(Path.Combine(root, "out", "server", "main.go"));
            Assert.Contains("func HomeData_ByHome(homeId string) []HomeData_ByHomeRow", generated);
            Assert.Contains("return HomeData_ByHome(homeId)", generated);
            Assert.True(File.Exists(Path.Combine(
                root, "out", "server", "junexis", "storage", "queries", "generated.go")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_LowersStorageAliasWithoutChangingCanonicalModelIdentity()
    {
        var root = await CreateProjectAsync(source:
            """
            import data::home { HomeData as Store }

            task ReadHome(homeId: Uuid) @server -> List<Store_ByHomeRow> {
                return Store_ByHome(homeId: homeId)
            }
            """, targets: "[\"server\"]");

        try
        {
            var data = Path.Combine(root, "src", "data");
            Directory.CreateDirectory(data);
            await File.WriteAllTextAsync(
                Path.Combine(data, "home.jnx"),
                """
                storage HomeData {
                    entity Observation { id: Uuid homeId: Uuid sensor: Text key(id) }
                    query ByHome(homeId: Uuid) from Observation {
                        filter homeId == params.homeId
                        project sensor
                    }
                }
                """);

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            var main = await File.ReadAllTextAsync(Path.Combine(root, "out", "server", "main.go"));
            var queries = await File.ReadAllTextAsync(Path.Combine(
                root, "out", "server", "junexis", "storage", "queries", "generated.go"));
            Assert.Contains("func HomeData_ByHome(homeId string) []HomeData_ByHomeRow", main);
            Assert.Contains("return HomeData_ByHome(homeId)", main);
            Assert.DoesNotContain("Store_ByHome", main);
            Assert.Contains("HomeData_ByHome", queries);
            Assert.DoesNotContain("Store_ByHome", queries);
            Assert.Contains(@"\u0022model\u0022:\u0022HomeData\u0022", queries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeCommand_ComposesImportedTasksForTopologyAndProtocolPreflight()
    {
        var root = await CreateProjectAsync(source:
            """
            import api::operations

            protocol rest {
                GET("/status", ReadStatus())
            }

            pipeline MainPipeline { use PublishStatus; }
            """, targets: "[\"server\"]");

        try
        {
            var api = Path.Combine(root, "src", "api");
            Directory.CreateDirectory(api);
            await File.WriteAllTextAsync(
                Path.Combine(api, "operations.jnx"),
                """
                task ReadStatus() @server -> Text { return "ready" }
                task PublishStatus() @server { log("ready") }
                """);

            var result = await RunCliAsync(["analyze"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("unknown task", result.StandardError);
            Assert.True(File.Exists(Path.Combine(
                root, "out", "analysis", "junexis.analysis.preflight.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_CompilesNamespacedLocalLibraryAndWritesModuleLock()
    {
        var root = await CreateProjectAsync(source:
            """
            import shared::worker

            pipeline MainPipeline { use SharedWorker; }
            """, targets: "[\"server\"]");
        var library = Path.Combine(Path.GetDirectoryName(root)!, $"junexis-local-library-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(library);
            await File.WriteAllTextAsync(Path.Combine(library, "worker.jnx"),
                "task SharedWorker() @server { log(\"from local library\"); }");
            var manifestPath = Path.Combine(root, "junexis.project.toml");
            await File.AppendAllTextAsync(manifestPath,
                $"{Environment.NewLine}localLibraries = [\"shared=../{Path.GetFileName(library)}\"]{Environment.NewLine}");

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            var generated = await File.ReadAllTextAsync(Path.Combine(root, "out", "server", "main.go"));
            Assert.Contains("SharedWorker", generated);
            var lockFile = await File.ReadAllTextAsync(Path.Combine(root, "out", "debug", "junexis.modules.lock.json"));
            Assert.Contains("junexis.modules.lock/0.1", lockFile);
            Assert.Contains("shared::worker", lockFile);
            Assert.Contains("sha256", lockFile);
            var repeated = await RunCliAsync(["build"], root);
            Assert.Equal(0, repeated.ExitCode);
            Assert.Equal(lockFile, await File.ReadAllTextAsync(
                Path.Combine(root, "out", "debug", "junexis.modules.lock.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(library, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_ProjectExample01_GeneratesAllManifestTargets()
    {
        var project = Path.Combine(RepositoryRoot, "examples", "projects", "01-temperature-monitor");
        var output = Path.Combine(Path.GetTempPath(), $"junexis-project-example-01-{Guid.NewGuid():N}");

        try
        {
            var result = await RunCliAsync(["build", project, "--out", output], RepositoryRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(output, "server", "main.go")));
            Assert.True(File.Exists(Path.Combine(output, "embedded", "platformio.ini")));
            Assert.True(File.Exists(Path.Combine(output, "report", "package.json")));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_ProjectMode_OverlaysDeveloperOwnedPlatformIo()
    {
        var root = await CreateProjectAsync(
            targets: "[\"embedded\"]",
            source:
            """
            task Blink() @embedded(esp32c3) {
                log("blink");
            }

            pipeline BlinkPipeline {
                use Blink;
            }
            """);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "user", "embedded"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "user", "embedded", "platformio.ini"),
                """
                ; developer-owned PlatformIO override
                [platformio]
                src_dir = .

                [env:junexis]
                platform = espressif32
                board = esp32-c3-devkitm-1
                framework = arduino
                lib_deps =
                    adafruit/Adafruit Unified Sensor
                """);

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("overlay embedded/platformio.ini <- user", result.StandardOutput);

            var platformIo = await File.ReadAllTextAsync(Path.Combine(root, "out", "embedded", "platformio.ini"));
            Assert.Contains("developer-owned PlatformIO override", platformIo);
            Assert.Contains("adafruit/Adafruit Unified Sensor", platformIo);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_ProjectMode_OverlaysDeveloperOwnedGoMod()
    {
        var root = await CreateProjectAsync();

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "user", "server"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "user", "server", "go.mod"),
                """
                module example.com/developer-owned-server

                go 1.25
                """);

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("overlay server/go.mod <- user", result.StandardOutput);

            var goMod = await File.ReadAllTextAsync(Path.Combine(root, "out", "server", "go.mod"));
            Assert.Contains("module example.com/developer-owned-server", goMod);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_ProjectMode_CopiesDeveloperOwnedEmbeddedCMakeLists()
    {
        var root = await CreateProjectAsync(
            targets: "[\"embedded\"]",
            source:
            """
            task Blink() @embedded(esp32c3) {
                log("blink");
            }

            pipeline BlinkPipeline {
                use Blink;
            }
            """);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "user", "embedded"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "user", "embedded", "CMakeLists.txt"),
                """
                # developer-owned embedded CMake overlay
                cmake_minimum_required(VERSION 3.20)
                """);

            var result = await RunCliAsync(["build"], root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("overlay embedded/CMakeLists.txt <- user", result.StandardOutput);

            var cmake = await File.ReadAllTextAsync(Path.Combine(root, "out", "embedded", "CMakeLists.txt"));
            Assert.Contains("developer-owned embedded CMake overlay", cmake);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateProjectAsync()
        => await CreateProjectAsync("[\"server\"]",
            """
            task Main() @server {
                log("project mode");
            }

            pipeline MainPipeline {
                use Main;
            }
            """);

    private static async Task<string> CreateProjectAsync(string targets, string source)
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-project-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            $$"""
            name = "project-mode"
            entry = "src/main.jnx"
            targets = {{targets}}
            outDir = "out"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            source);

        return root;
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Junexis CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await standardOutput, await standardError);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ROADMAP.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "examples", "grammar")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the junexis-lang repository root.");
    }
}
