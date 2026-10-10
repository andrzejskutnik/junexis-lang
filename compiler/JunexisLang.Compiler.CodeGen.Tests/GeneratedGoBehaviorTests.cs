namespace JunexisLang.Compiler.CodeGen.Tests;

using System.Diagnostics;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>Executable L3 checks for generated Go behavior.</summary>
public sealed class GeneratedGoBehaviorTests
{
    private static async Task<IRDocument> TransformToIrAsync(string source)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, source);
            var result = await new JunexisCompiler().CheckAsync(new CheckOptions(path));
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
            return new AstToIrTransformer().Transform(result.Ast!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "M4Behavior")]
    public async Task InjectedExtern_PreservesTaskBehaviorAsync()
    {
        const string source = """
            extern task double_value(value: Int) @server -> Int

            task Calculate(value: Int) @server -> Int {
                return double_value(value) + 1
            }
            """;

        var files = new GoCodeGenerator().Generate(await TransformToIrAsync(source), new CodeGenOptions("out"));
        var directory = Path.Combine(Path.GetTempPath(), $"junexis-go-l3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in files.Where(file => file.FilePath.StartsWith("server/", StringComparison.Ordinal)))
                await File.WriteAllTextAsync(Path.Combine(directory, Path.GetFileName(file.FilePath)), file.Content);

            await File.WriteAllTextAsync(Path.Combine(directory, "behavior_test.go"), """
                package main

                import "testing"

                func TestInjectedExternBehavior(t *testing.T) {
                    double_value = func(value int64) int64 { return value * 2 }
                    if got := Calculate(4); got != 9 {
                        t.Fatalf("Calculate(4) = %d, want 9", got)
                    }
                }
                """);

            var test = await RunGoAsync(directory, Path.Combine(Path.GetTempPath(), "junexis-codegen-go"), "test", "./...");
            Assert.True(test.ExitCode == 0, $"go test failed:{Environment.NewLine}{test.Output}{test.Error}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "M12Behavior")]
    public async Task StorageExtern_EmitsCompilableProviderNeutralRuntimeClientAsync()
    {
        const string source = """
            task Api() @server { }
            extern storage HomeStore {
                target server
                language go
                fn save(value: Text)
                fn recent() -> List<Text>
            }
            """;

        var files = new GoCodeGenerator().Generate(await TransformToIrAsync(source), new CodeGenOptions("out"));
        var directory = Path.Combine(Path.GetTempPath(), $"junexis-storage-client-m12-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in files.Where(file => file.FilePath.StartsWith("server/", StringComparison.Ordinal)))
            {
                var relative = file.FilePath["server/".Length..].Replace('/', Path.DirectorySeparatorChar);
                var path = Path.Combine(directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content);
            }

            Assert.Contains(files, file => file.FilePath == "server/junexis/storage/runtime/client.go");
            Assert.DoesNotContain(files, file => file.FilePath.Contains("/storage/sqlite/", StringComparison.Ordinal));
            Assert.DoesNotContain(files, file => file.FilePath.Contains("/storage/memory/", StringComparison.Ordinal));
            Assert.DoesNotContain(files, file => file.FilePath.Contains("/storage/postgresql/", StringComparison.Ordinal));
            var test = await RunGoAsync(directory, Path.Combine(Path.GetTempPath(), "junexis-codegen-go"), "test", "./junexis/storage/runtime");
            Assert.True(test.ExitCode == 0, $"generated Storage Runtime client failed to compile:{Environment.NewLine}{test.Output}{test.Error}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "M13Behavior")]
    public async Task StorageQuery_EmitsCompilableTypedRuntimeClientAsync()
    {
        const string source = """
            storage HomeData {
                entity Observation {
                    id: Uuid
                    homeId: Uuid
                    sensor: Text
                    value: Float
                    recordedAt: Time
                    key(id)
                }
                query RecentObservations(homeId: Uuid) from Observation {
                    filter homeId == params.homeId and value > 0.0
                    project sensor as sensorName, value, recordedAt
                    sort recordedAt desc
                    limit 20
                }
                query PortableTypes(count: Int, ratio: Float, active: Bool, label: Text, bytes: Bytes, amount: Decimal, at: Time, id: Uuid, metadata: Json, note: Text | Null) from Observation {
                    project id
                }
            }
            task Api() @server { }
            task ReadHome(homeId: Uuid) @server -> List<HomeData_RecentObservationsRow> {
                return HomeData_RecentObservations(homeId: homeId)
            }
            """;

        var files = new GoCodeGenerator().Generate(await TransformToIrAsync(source), new CodeGenOptions("out"));
        var directory = Path.Combine(Path.GetTempPath(), $"junexis-storage-query-client-m13-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in files.Where(file => file.FilePath.StartsWith("server/", StringComparison.Ordinal)))
            {
                var relative = file.FilePath["server/".Length..].Replace('/', Path.DirectorySeparatorChar);
                var path = Path.Combine(directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content);
            }

            var test = await RunGoAsync(directory, Path.Combine(Path.GetTempPath(), "junexis-codegen-go"), "test", "./...");
            Assert.True(test.ExitCode == 0, $"generated query client failed to compile:{Environment.NewLine}{test.Output}{test.Error}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "M14Behavior")]
    public async Task StorageMutation_EmitsCompilableTypedRuntimeClientAsync()
    {
        const string source = """
            storage HomeData {
                entity Observation {
                    id: Uuid count: Int ratio: Float active: Bool label: Text bytes: Bytes
                    amount: Decimal at: Time metadata: Json note: Text | Null key(id)
                }
                mutation AddObservation(id: Uuid, count: Int, ratio: Float, active: Bool,
                    label: Text, bytes: Bytes, amount: Decimal, at: Time, metadata: Json,
                    note: Text | Null) insert single Observation {
                    set id = params.id
                    set count = params.count
                    set ratio = params.ratio
                    set active = params.active
                    set label = params.label
                    set bytes = params.bytes
                    set amount = params.amount
                    set at = params.at
                    set metadata = params.metadata
                    set note = params.note
                }
            }
            task Api() @server { }
            """;

        var files = new GoCodeGenerator().Generate(await TransformToIrAsync(source), new CodeGenOptions("out"));
        var directory = Path.Combine(Path.GetTempPath(), $"junexis-storage-mutation-client-m14-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in files.Where(file => file.FilePath.StartsWith("server/", StringComparison.Ordinal)))
            {
                var relative = file.FilePath["server/".Length..].Replace('/', Path.DirectorySeparatorChar);
                var path = Path.Combine(directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content);
            }

            var test = await RunGoAsync(directory, Path.Combine(Path.GetTempPath(), "junexis-codegen-go"), "test", "./...");
            Assert.True(test.ExitCode == 0, $"generated mutation client failed to compile:{Environment.NewLine}{test.Output}{test.Error}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunGoAsync(string workingDirectory, string goPath, params string[] arguments)
    {
        var executable = Environment.GetEnvironmentVariable("JUNEXIS_GO_EXE") ?? "go";
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["GOPATH"] = goPath;
        startInfo.Environment["GOMODCACHE"] = Path.Combine(goPath, "pkg", "mod");
        startInfo.Environment["GOCACHE"] = Path.Combine(goPath, "build-cache");
        startInfo.Environment["CGO_ENABLED"] = "0";

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }
}
