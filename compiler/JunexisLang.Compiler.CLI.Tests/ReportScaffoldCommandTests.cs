using System.Diagnostics;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class ReportScaffoldCommandTests
{
    [Fact]
    public async Task Scaffold_CreatesAdapterAndBuildUsesDeveloperOwnedOverlayAsync()
    {
        var root = await CreateProjectAsync();
        try
        {
            var scaffold = await RunCliAsync(["report", "scaffold"], root);
            Assert.Equal(0, scaffold.ExitCode);
            Assert.Contains("created user/report/adapter.ts", scaffold.StandardOutput);
            var source = Path.Combine(root, "user", "report", "adapter.ts");
            Assert.Contains("userReportAdapter", await File.ReadAllTextAsync(source));

            var build = await RunCliAsync(["build"], root);
            Assert.Equal(0, build.ExitCode);
            Assert.Contains("overlay report/user-adapter.ts <- user", build.StandardOutput);
            var generated = Path.Combine(root, "out", "report", "user-adapter.ts");
            Assert.Equal(await File.ReadAllTextAsync(source), await File.ReadAllTextAsync(generated));
            Assert.Contains(
                "\"renderer\": \"user\"",
                await File.ReadAllTextAsync(Path.Combine(
                    root,
                    "out",
                    "report",
                    "junexis-report-manifest.json")));

            var inventory = System.Text.Json.JsonSerializer.Deserialize<ArtifactInventory>(
                await File.ReadAllTextAsync(Path.Combine(root, "out", "junexis.artifacts.json")));
            Assert.NotNull(inventory);
            Assert.Contains(inventory.Artifacts, entry =>
                entry.Path == "report/user-adapter.ts" && entry.Ownership == "user-overlay");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Scaffold_DoesNotOverwriteExistingAdapterAsync()
    {
        var root = await CreateProjectAsync();
        var file = Path.Combine(root, "user", "report", "adapter.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "// moje UI");
        try
        {
            var result = await RunCliAsync(["report", "scaffold"], root);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("skip existing", result.StandardOutput);
            Assert.Equal("// moje UI", await File.ReadAllTextAsync(file));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ReactScaffold_CreatesFrameworkAdapterAndMergesOwnedDependenciesAsync()
    {
        var root = await CreateProjectAsync();
        try
        {
            var scaffold = await RunCliAsync(["report", "scaffold", "--framework", "react"], root);
            Assert.Equal(0, scaffold.ExitCode);
            Assert.Contains("created user/report/adapter.ts", scaffold.StandardOutput);
            Assert.Contains("created user/report/package.json", scaffold.StandardOutput);
            Assert.Contains(
                "from 'react-dom/client'",
                await File.ReadAllTextAsync(Path.Combine(root, "user", "report", "adapter.ts")));

            var build = await RunCliAsync(["build"], root);
            Assert.Equal(0, build.ExitCode);
            var package = await File.ReadAllTextAsync(Path.Combine(root, "out", "report", "package.json"));
            Assert.Contains("\"react\": \"^19.1.0\"", package);
            Assert.Contains("\"@types/react\": \"^19.1.0\"", package);
            Assert.Contains("\"app:build\"", package);

            var userPackage = Path.Combine(root, "out", "report", "user-package.json");
            Assert.True(File.Exists(userPackage));
            var inventory = System.Text.Json.JsonSerializer.Deserialize<ArtifactInventory>(
                await File.ReadAllTextAsync(Path.Combine(root, "out", "junexis.artifacts.json")));
            Assert.NotNull(inventory);
            Assert.Contains(inventory.Artifacts, entry =>
                entry.Path == "report/user-package.json" && entry.Ownership == "user-overlay");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Build_RejectsUserReportPackageScriptsAsync()
    {
        var root = await CreateProjectAsync();
        var package = Path.Combine(root, "user", "report", "package.json");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        await File.WriteAllTextAsync(package, "{\"scripts\":{\"build\":\"mine\"}}");
        try
        {
            var build = await RunCliAsync(["build"], root);
            Assert.Equal(1, build.ExitCode);
            Assert.Contains("invalid user/report/package.json", build.StandardError);
            Assert.Contains("unsupported field(s): scripts", build.StandardError);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task DeclarativeScaffold_CreatesValidatedViewAndBuildsTypedConfigurationAsync()
    {
        var root = await CreateProjectAsync();
        try
        {
            var scaffold = await RunCliAsync(
                ["report", "scaffold", "--declarative"],
                root);
            Assert.Equal(0, scaffold.ExitCode);
            Assert.Contains("created user/report/view.json", scaffold.StandardOutput);
            Assert.False(File.Exists(Path.Combine(root, "user", "report", "adapter.ts")));
            var viewSource = await File.ReadAllTextAsync(
                Path.Combine(root, "user", "report", "view.json"));
            Assert.Contains("\"task\": \"Dashboard\"", viewSource);

            var build = await RunCliAsync(["build"], root);
            Assert.Equal(0, build.ExitCode);
            var generated = await File.ReadAllTextAsync(
                Path.Combine(root, "out", "report", "user-view.ts"));
            Assert.Contains("JunexisReportView", generated);
            Assert.Contains("\"schema\": \"junexis.report-view/0.1\"", generated);
            Assert.Contains(
                "\"renderer\": \"declarative\"",
                await File.ReadAllTextAsync(Path.Combine(
                    root,
                    "out",
                    "report",
                    "junexis-report-manifest.json")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Build_RejectsDeclarativeViewForUnknownTaskAsync()
    {
        var root = await CreateProjectAsync();
        var view = Path.Combine(root, "user", "report", "view.json");
        Directory.CreateDirectory(Path.GetDirectoryName(view)!);
        await File.WriteAllTextAsync(
            view,
            """
            {
              "$schema": "junexis.report-view/0.1",
              "title": "Panel",
              "layout": "grid",
              "panels": [{ "task": "NieIstnieje", "title": "Błąd", "fields": [] }]
            }
            """);
        try
        {
            var build = await RunCliAsync(["build"], root);
            Assert.Equal(1, build.ExitCode);
            Assert.Contains("invalid user/report/view.json", build.StandardError);
            Assert.Contains("unknown @report task", build.StandardError);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<string> CreateProjectAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-report-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            "name = \"report-adapter\"\nentry = \"src/main.jnx\"\ntargets = [\"report\"]\noutDir = \"out\"\n");
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            "task Dashboard() @report { log(\"ready\") }\n");
        return root;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string[] arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(typeof(ReportScaffoldHandler).Assembly.Location);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Junexis CLI.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }
}
