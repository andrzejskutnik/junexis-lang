using System.Diagnostics;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class BuildCommandTests
{
    [Fact]
    public async Task BuildCommand_GeneratesServerArtifactContract()
    {
        var root = await CreateTemporaryExampleAsync("junexis-m5-build");
        var input = Path.Combine(root, "main.jnx");
        var output = Path.Combine(root, "out");

        try
        {
            var result = await RunCliAsync("build", input, "--target", "server", "--out", output);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(output, "server", "main.go")));
            Assert.True(File.Exists(Path.Combine(output, "server", "go.mod")));
            Assert.True(File.Exists(Path.Combine(output, "debug", "junexis.debug.toml")));
            Assert.True(File.Exists(Path.Combine(output, "junexis.artifacts.json")));
            Assert.True(File.Exists(Path.Combine(output, "junexis.bundle.json")));
            Assert.True(File.Exists(Path.Combine(output, "analysis", "junexis.analysis.json")));
            var manifest = await File.ReadAllTextAsync(Path.Combine(output, "debug", "junexis.debug.toml"));
            Assert.Contains("kind = \"junexis.debug-manifest\"", manifest);
            Assert.Contains("traceCollectorEventModel = \"junexis.tracecollector.v1\"", manifest);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_RemovesRetiredGeneratedStorageProviders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-m12-retired-storage-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "out");
        var input = Path.Combine(root, "main.jnx");
        var retired = new[] { "memory", "sqlite", "postgresql" }
            .Select(provider => Path.Combine(output, "server", "junexis", "storage", provider, "provider.go"))
            .ToArray();
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(input, """
                task Api() @server { }
                extern storage Store {
                    target server
                    language go
                    fn save(value: Text)
                }
                """);
            foreach (var path in retired)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "// retired compiler output");
            }

            var result = await RunCliAsync("build", input, "--target", "server", "--out", output);

            Assert.Equal(0, result.ExitCode);
            Assert.All(retired, path => Assert.False(File.Exists(path)));
            Assert.True(File.Exists(Path.Combine(output, "server", "junexis", "storage", "runtime", "client.go")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_OutputSchema2_ReportsProjectAndArtifactContracts()
    {
        var root = await CreateTemporaryExampleAsync("junexis-m5-schema2");
        var input = Path.Combine(root, "main.jnx");
        var output = Path.Combine(root, "out");

        try
        {
            var result = await RunCliAsync(
                "build", input, "--target", "server", "--out", output,
                "--json", "--output-schema", "2.0");

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var response = document.RootElement;
            Assert.Equal("2.0", response.GetProperty("schemaVersion").GetString());
            Assert.Equal("file", response.GetProperty("mode").GetString());
            Assert.Equal(Path.GetFullPath(output), response.GetProperty("outputRoot").GetString());
            Assert.Equal(64, response.GetProperty("buildId").GetString()!.Length);
            Assert.Equal("server", response.GetProperty("targets")[0].GetString());
            Assert.Equal("server", response.GetProperty("requestedTargets")[0].GetString());
            Assert.Contains(response.GetProperty("applicableTargets").EnumerateArray(),
                item => item.GetString() == "server");
            Assert.Equal("server", response.GetProperty("builtTargets")[0].GetString());
            Assert.Equal("explicit", response.GetProperty("targetSelectionSource").GetString());
            Assert.Equal(
                Path.Combine(output, "junexis.bundle.json"),
                response.GetProperty("bundleManifest").GetString());
            Assert.Equal(
                Path.Combine(output, "junexis.artifacts.json"),
                response.GetProperty("artifactManifest").GetString());
            Assert.Equal(
                Path.Combine(output, "analysis", "junexis.analysis.json"),
                response.GetProperty("analysisSnapshot").GetString());
            var receiptPath = Path.Combine(root, ".junexis", "state", "main", "latest-build.json");
            Assert.Equal(receiptPath, response.GetProperty("latestBuildReceipt").GetString());
            Assert.True(File.Exists(receiptPath));

            using var inventoryDocument = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(output, "junexis.artifacts.json")));
            var inventory = inventoryDocument.RootElement;
            Assert.Equal("1.0", inventory.GetProperty("schemaVersion").GetString());
            Assert.Contains(
                inventory.GetProperty("artifacts").EnumerateArray(),
                artifact => artifact.GetProperty("path").GetString() == "debug/junexis.debug.toml");
            Assert.Contains(
                inventory.GetProperty("artifacts").EnumerateArray(),
                artifact => artifact.GetProperty("path").GetString() == "analysis/junexis.analysis.json" &&
                            artifact.GetProperty("kind").GetString() == "analysis-snapshot");

            using var snapshotDocument = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(output, "analysis", "junexis.analysis.json")));
            var snapshot = snapshotDocument.RootElement;
            Assert.Equal("1.1", snapshot.GetProperty("schemaVersion").GetString());
            Assert.Equal(response.GetProperty("buildId").GetString(), snapshot.GetProperty("buildId").GetString());
            Assert.NotEmpty(snapshot.GetProperty("symbols").EnumerateArray());
            Assert.NotEmpty(snapshot.GetProperty("relations").EnumerateArray());
            Assert.NotEmpty(snapshot.GetProperty("semanticRelations").EnumerateArray());
            Assert.True(snapshot.GetProperty("topology").TryGetProperty("maxDepth", out _));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_FileModeInfersOnlyApplicableExecutionTargets()
    {
        var root = await CreateTemporaryExampleAsync("junexis-inferred-target");
        var input = Path.Combine(root, "main.jnx");
        var output = Path.Combine(root, "out");

        try
        {
            await File.WriteAllTextAsync(input,
                "task Main() @server { log(\"inferred\") }");
            var result = await RunCliAsync(
                "build", input, "--out", output, "--json", "--output-schema", "2.0");

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var response = document.RootElement;
            Assert.Empty(response.GetProperty("requestedTargets").EnumerateArray());
            Assert.Equal(["server"], response.GetProperty("applicableTargets")
                .EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal(["server"], response.GetProperty("builtTargets")
                .EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal(["server"], response.GetProperty("targets")
                .EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal("inferred", response.GetProperty("targetSelectionSource").GetString());
            Assert.False(File.Exists(Path.Combine(output, "schema.proto")));
            Assert.False(Directory.Exists(Path.Combine(output, "embedded")));
            Assert.False(Directory.Exists(Path.Combine(output, "report")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_RejectsRequestedTargetWithoutApplicableTask()
    {
        var root = await CreateTemporaryExampleAsync("junexis-inapplicable-target");
        var input = Path.Combine(root, "main.jnx");
        var output = Path.Combine(root, "out");

        try
        {
            await File.WriteAllTextAsync(input,
                "task Main() @server { log(\"mismatch\") }");
            var result = await RunCliAsync(
                "build", input, "--target", "embedded", "--out", output,
                "--json", "--output-schema", "2.0");

            Assert.Equal(1, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                item => item.GetProperty("code").GetString() == "B0003");
            Assert.False(File.Exists(Path.Combine(output, "junexis.bundle.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("extern type NativeHandle @embedded")]
    [InlineData("extern task NativeRead() @server")]
    [InlineData("extern storage Store { target server language go file \"user/server/store.go\" fn save(value: Int) }")]
    public async Task BuildCommand_ExternDeclarationWithoutExecutableTask_FailsWithControlledDiagnostic(
        string source)
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-extern-only-{Guid.NewGuid():N}");
        var input = Path.Combine(root, "main.jnx");
        var output = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(input, source);

            var result = await RunCliAsync(
                "build", input, "--out", output, "--json", "--output-schema", "2.0");

            Assert.Equal(1, result.ExitCode);
            Assert.DoesNotContain("Unhandled exception", result.StandardError, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var response = document.RootElement;
            Assert.Contains(response.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                diagnostic.GetProperty("code").GetString() == "B0003");
            Assert.Empty(response.GetProperty("applicableTargets").EnumerateArray());
            Assert.Empty(response.GetProperty("builtTargets").EnumerateArray());
            Assert.Equal("inferred", response.GetProperty("targetSelectionSource").GetString());
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_UnknownTarget_ReportsStableDiagnosticAndSelectionMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-unknown-target-{Guid.NewGuid():N}");
        var input = Path.Combine(root, "main.jnx");

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(input, "task Main() @server { log(\"ok\") }");

            var result = await RunCliAsync(
                "build", input, "--target", "mars", "--json", "--output-schema", "2.0");

            Assert.Equal(1, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var response = document.RootElement;
            var diagnostic = Assert.Single(response.GetProperty("diagnostics").EnumerateArray(), item =>
                item.GetProperty("code").GetString() == "G0002");
            Assert.Equal("CodeGen", diagnostic.GetProperty("category").GetString());
            Assert.Equal(Path.GetFullPath(input), diagnostic.GetProperty("file").GetString());
            Assert.Equal(["mars"], response.GetProperty("requestedTargets")
                .EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal(["server"], response.GetProperty("applicableTargets")
                .EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Empty(response.GetProperty("builtTargets").EnumerateArray());
            Assert.Equal("explicit", response.GetProperty("targetSelectionSource").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_AnalysisSnapshot11_ExportsMentorSemanticRelations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-m26-snapshot-{Guid.NewGuid():N}");
        var input = Path.Combine(root, "main.jnx");
        var output = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                input,
                """
                task Sensor() @server -> queue<Text> {
                    return "sample";
                }

                task Store(value: Text) @server {
                    log(value);
                }

                pipeline Telemetry {
                    use Sensor -> Store;
                }

                narrative Safety {
                    compliance "safety"
                    trace full
                    applies_to { Telemetry }
                }

                supervisor Guard {
                    strategy one_for_one
                    children {
                        worker Sensor { restart: permanent }
                        worker Store { restart: permanent }
                    }
                }

                circuit_breaker StoreBreaker {
                    failure_threshold: 3
                    protects: [Store]
                }
                """);

            var result = await RunCliAsync("build", input, "--target", "server", "--out", output);

            Assert.Equal(0, result.ExitCode);
            using var snapshotDocument = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(output, "analysis", "junexis.analysis.json")));
            var snapshot = snapshotDocument.RootElement;
            Assert.Equal("1.1", snapshot.GetProperty("schemaVersion").GetString());

            var semanticRelations = snapshot.GetProperty("semanticRelations").EnumerateArray().ToArray();
            Assert.Contains(semanticRelations, relation =>
                relation.GetProperty("kind").GetString() == "applies-to" &&
                relation.GetProperty("sourceSymbolId").GetString() == "narrative:Safety" &&
                relation.GetProperty("targetSymbolId").GetString() == "pipeline:Telemetry");
            Assert.Equal(2, semanticRelations.Count(relation =>
                relation.GetProperty("kind").GetString() == "participates-in-narrative"));
            Assert.Equal(2, semanticRelations.Count(relation =>
                relation.GetProperty("kind").GetString() == "supervised-by"));
            Assert.Contains(semanticRelations, relation =>
                relation.GetProperty("kind").GetString() == "protected-by" &&
                relation.GetProperty("sourceSymbolId").GetString() == "task:Store");

            var narrative = Assert.Single(snapshot.GetProperty("symbols").EnumerateArray(), symbol =>
                symbol.GetProperty("id").GetString() == "narrative:Safety");
            Assert.Equal("safety", narrative.GetProperty("properties").GetProperty("compliance").GetString());
            Assert.Equal("full", narrative.GetProperty("properties").GetProperty("tracePolicy").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeCommand_PublishesSnapshotBeforeCodeGeneration_AndBuildKeepsIdentity()
    {
        var root = await CreateTemporaryExampleAsync("junexis-preflight");
        var input = Path.Combine(root, "main.jnx");
        var output = Path.Combine(root, "out");

        try
        {
            var preflight = await RunCliAsync(
                "analyze", input, "--target", "server", "--out", output, "--json");

            Assert.Equal(0, preflight.ExitCode);
            using var preflightDocument = JsonDocument.Parse(preflight.StandardOutput);
            var preflightResponse = preflightDocument.RootElement;
            var buildId = preflightResponse.GetProperty("buildId").GetString();
            var snapshotPath = preflightResponse.GetProperty("analysisSnapshot").GetString();
            Assert.Equal("analyze", preflightResponse.GetProperty("command").GetString());
            Assert.Equal(64, buildId!.Length);
            Assert.True(File.Exists(snapshotPath));
            Assert.False(Directory.Exists(Path.Combine(output, "server")));
            Assert.False(File.Exists(Path.Combine(output, "junexis.artifacts.json")));

            var rejectedBuild = await RunCliAsync(
                "build", input, "--target", "server", "--out", output,
                "--build-id", new string('0', 64), "--json", "--output-schema", "2.0");
            Assert.Equal(1, rejectedBuild.ExitCode);
            using (var rejectedDocument = JsonDocument.Parse(rejectedBuild.StandardOutput))
            {
                var rejectedResponse = rejectedDocument.RootElement;
                Assert.Contains(
                    rejectedResponse.GetProperty("diagnostics").EnumerateArray(),
                    diagnostic => diagnostic.GetProperty("code").GetString() == "B0001");
                Assert.Equal(["server"], rejectedResponse.GetProperty("requestedTargets")
                    .EnumerateArray().Select(item => item.GetString()!).ToArray());
                Assert.Contains(rejectedResponse.GetProperty("applicableTargets").EnumerateArray(),
                    item => item.GetString() == "server");
                Assert.Empty(rejectedResponse.GetProperty("builtTargets").EnumerateArray());
                Assert.Equal("explicit", rejectedResponse.GetProperty("targetSelectionSource").GetString());
            }
            Assert.False(Directory.Exists(Path.Combine(output, "server")));

            var build = await RunCliAsync(
                "build", input, "--target", "server", "--out", output,
                "--build-id", buildId, "--json", "--output-schema", "2.0");

            Assert.Equal(0, build.ExitCode);
            using var buildDocument = JsonDocument.Parse(build.StandardOutput);
            Assert.Equal(buildId, buildDocument.RootElement.GetProperty("buildId").GetString());
            Assert.True(File.Exists(Path.Combine(output, "server", "main.go")));
            Assert.False(File.Exists(snapshotPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeCommand_IdenticalRelocatedSource_PreservesBuildIdentity()
    {
        var firstRoot = await CreateTemporaryExampleAsync("junexis-identity-first");
        var secondRoot = await CreateTemporaryExampleAsync("junexis-identity-second");

        try
        {
            var secondSource = Path.Combine(secondRoot, "main.jnx");
            var sourceWithCrLf = (await File.ReadAllTextAsync(secondSource))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
            await File.WriteAllTextAsync(secondSource, sourceWithCrLf);

            var first = await RunCliAsync(
                "analyze", Path.Combine(firstRoot, "main.jnx"),
                "--target", "server", "--out", Path.Combine(firstRoot, "out"), "--json");
            var second = await RunCliAsync(
                "analyze", secondSource,
                "--target", "server", "--out", Path.Combine(secondRoot, "out"), "--json");

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            using var firstDocument = JsonDocument.Parse(first.StandardOutput);
            using var secondDocument = JsonDocument.Parse(second.StandardOutput);
            Assert.Equal(
                firstDocument.RootElement.GetProperty("buildId").GetString(),
                secondDocument.RootElement.GetProperty("buildId").GetString());

            var firstBuild = await RunCliAsync(
                "build", Path.Combine(firstRoot, "main.jnx"),
                "--target", "server", "--out", Path.Combine(firstRoot, "build"),
                "--json", "--output-schema", "2.0");
            var secondBuild = await RunCliAsync(
                "build", secondSource,
                "--target", "server", "--out", Path.Combine(secondRoot, "build"),
                "--json", "--output-schema", "2.0");
            Assert.Equal(0, firstBuild.ExitCode);
            Assert.Equal(0, secondBuild.ExitCode);
            using var firstBuildDocument = JsonDocument.Parse(firstBuild.StandardOutput);
            using var secondBuildDocument = JsonDocument.Parse(secondBuild.StandardOutput);
            Assert.Equal(
                firstDocument.RootElement.GetProperty("buildId").GetString(),
                firstBuildDocument.RootElement.GetProperty("buildId").GetString());
            Assert.Equal(
                firstBuildDocument.RootElement.GetProperty("buildId").GetString(),
                secondBuildDocument.RootElement.GetProperty("buildId").GetString());

            await File.AppendAllTextAsync(secondSource, "\n// identity change\n");
            var changed = await RunCliAsync(
                "analyze", secondSource,
                "--target", "server", "--out", Path.Combine(secondRoot, "out-changed"), "--json");
            Assert.Equal(0, changed.ExitCode);
            using var changedDocument = JsonDocument.Parse(changed.StandardOutput);
            Assert.NotEqual(
                firstDocument.RootElement.GetProperty("buildId").GetString(),
                changedDocument.RootElement.GetProperty("buildId").GetString());
        }
        finally
        {
            if (Directory.Exists(firstRoot)) Directory.Delete(firstRoot, recursive: true);
            if (Directory.Exists(secondRoot)) Directory.Delete(secondRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CompileAlias_RemainsAvailableForPrototypeCompatibility()
    {
        var result = await RunCliAsync("compile", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Build a .jnx file", result.StandardOutput);
    }

    [Fact]
    public async Task BuildCommand_FileModeWithoutOut_UsesIsolatedJunexisOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-file-mode-{Guid.NewGuid():N}");
        var input = Path.Combine(root, "sample-system.jnx");

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                input,
                """
                task Main() @server {
                    log("file mode");
                }

                pipeline MainPipeline {
                    use Main;
                }
                """);

            var result = await RunCliAsync("build", input, "--target", "server");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(root, ".junexis", "out", "sample-system", "server", "main.go")));
            Assert.True(File.Exists(Path.Combine(
                root, ".junexis", "state", "sample-system", "latest-build.json")));
            Assert.False(Directory.Exists(Path.Combine(root, "out")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildCommand_ProjectModePublishesLatestBuildReceipt_AndFailedBuildKeepsIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-latest-build-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "src", "main.jnx");
        var receiptPath = Path.Combine(root, ".junexis", "state", "latest-build.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllTextAsync(
                Path.Combine(root, "junexis.project.toml"),
                """
                name = "latest-build-test"
                entry = "src/main.jnx"
                targets = ["server"]
                outDir = "out"
                """);
            await File.WriteAllTextAsync(source, "task Main() @server { log(\"ok\") }");

            var successfulBuild = await RunCliAsync(
                "build", root, "--json", "--output-schema", "2.0");

            Assert.Equal(0, successfulBuild.ExitCode);
            var originalReceiptJson = await File.ReadAllTextAsync(receiptPath);
            var receipt = LatestBuildReceiptContract.Parse(originalReceiptJson);
            Assert.Equal("1.0", receipt.SchemaVersion);
            Assert.Equal("project", receipt.Mode);
            Assert.Equal(Path.GetFullPath(root), receipt.ProjectRoot);
            Assert.Equal(Path.GetFullPath(source), receipt.EntryFile);
            Assert.Equal(Path.Combine(root, "out"), receipt.OutputRoot);
            Assert.Equal(["server"], receipt.Targets);
            Assert.Equal(64, receipt.BuildId.Length);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(receiptPath)!, "*.tmp"));

            await File.WriteAllTextAsync(source, "task Main( {");
            var failedBuild = await RunCliAsync(
                "build", root, "--json", "--output-schema", "2.0");

            Assert.Equal(1, failedBuild.ExitCode);
            Assert.Equal(originalReceiptJson, await File.ReadAllTextAsync(receiptPath));
            using var failedDocument = JsonDocument.Parse(failedBuild.StandardOutput);
            Assert.Equal(JsonValueKind.Null,
                failedDocument.RootElement.GetProperty("latestBuildReceipt").ValueKind);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        params string[] arguments)
    {
        var assembly = typeof(CompileHandler).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
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
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "compiler", "JunexisLang.Compiler.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private static async Task<string> CreateTemporaryExampleAsync(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var repositoryRoot = FindRepositoryRoot();
        var source = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "examples", "13-minimal-pipeline.jnx"));
        await File.WriteAllTextAsync(Path.Combine(root, "main.jnx"), source);
        return root;
    }
}
