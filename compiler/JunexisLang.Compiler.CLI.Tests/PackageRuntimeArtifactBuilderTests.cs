using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class PackageRuntimeArtifactBuilderTests
{
    [Fact]
    public async Task BuildAsync_AddsRunnableArtifactsWithoutMutatingGeneratedSourcesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-package-runtime-{Guid.NewGuid():N}");
        var server = Path.Combine(root, "server");
        var report = Path.Combine(root, "report");
        Directory.CreateDirectory(server);
        Directory.CreateDirectory(report);
        try
        {
            const string goModule = "module example.test/junexis\n\ngo 1.22\n";
            await File.WriteAllTextAsync(Path.Combine(server, "main.go"), "package main\nfunc main() {}\n");
            await File.WriteAllTextAsync(Path.Combine(server, "go.mod"), goModule);
            await File.WriteAllTextAsync(Path.Combine(report, "index.html"), "<main>Junexis</main>");
            await File.WriteAllTextAsync(Path.Combine(report, "report.css"), "body { color: black; }");
            await File.WriteAllTextAsync(Path.Combine(report, "serve.mjs"), "// server");
            await File.WriteAllTextAsync(Path.Combine(report, "junexis-report-manifest.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(report, "package.json"), "{}");
            var inventory = new ArtifactInventory(
                ArtifactInventory.CurrentVersion,
                new string('a', 64),
                []);
            await File.WriteAllTextAsync(
                Path.Combine(root, "junexis.artifacts.json"),
                JsonSerializer.Serialize(inventory));
            var go = await CreateGoToolAsync(root);
            var npm = await CreateNpmToolAsync(root);

            await PackageRuntimeArtifactBuilder.BuildAsync(root, go, npm);

            Assert.Equal(goModule, await File.ReadAllTextAsync(Path.Combine(server, "go.mod")));
            Assert.True(File.Exists(Path.Combine(
                server,
                "runtime",
                OperatingSystem.IsWindows() ? "junexis-server.exe" : "junexis-server")));
            Assert.True(File.Exists(Path.Combine(report, "runtime", "dist", "browser.js")));
            var updated = JsonSerializer.Deserialize<ArtifactInventory>(
                await File.ReadAllTextAsync(Path.Combine(root, "junexis.artifacts.json")));
            Assert.NotNull(updated);
            Assert.Contains(updated.Artifacts, entry => entry.Kind == "native-executable");
            Assert.Contains(updated.Artifacts, entry => entry.Kind == "web-bundle");
            Assert.All(updated.Artifacts, entry => Assert.Equal("generated-runtime", entry.Ownership));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<string> CreateGoToolAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-go.cmd");
            await File.WriteAllTextAsync(
                path,
                "@echo off\r\nif not \"%1\"==\"build\" exit /b 0\r\n:scan\r\nif \"%1\"==\"\" exit /b 0\r\nif \"%1\"==\"-o\" goto output\r\nshift\r\ngoto scan\r\n:output\r\nshift\r\ntype nul > \"%~1\"\r\nexit /b 0\r\n");
            return path;
        }
        var pathUnix = Path.Combine(root, "fake-go.sh");
        await File.WriteAllTextAsync(
            pathUnix,
            "#!/usr/bin/env sh\nif [ \"$1\" = \"build\" ]; then shift; while [ $# -gt 0 ]; do if [ \"$1\" = \"-o\" ]; then shift; : > \"$1\"; chmod +x \"$1\"; break; fi; shift; done; fi\nexit 0\n");
        File.SetUnixFileMode(pathUnix, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return pathUnix;
    }

    private static async Task<string> CreateNpmToolAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-npm.cmd");
            await File.WriteAllTextAsync(
                path,
                "@echo off\r\nif not exist dist mkdir dist\r\n> dist\\browser.js echo console.log('ready');\r\nexit /b 0\r\n");
            return path;
        }
        var pathUnix = Path.Combine(root, "fake-npm.sh");
        await File.WriteAllTextAsync(
            pathUnix,
            "#!/usr/bin/env sh\nmkdir -p dist\nprintf '%s' \"console.log('ready');\" > dist/browser.js\nexit 0\n");
        File.SetUnixFileMode(pathUnix, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return pathUnix;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }
}
