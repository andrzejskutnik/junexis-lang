using System.Diagnostics;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class StorageRuntimeBindingPlanTests
{
    [Fact]
    public async Task CreatesSecretSafeLaunchEnvironmentAfterLockVerification()
    {
        var root = await CreateProjectAsync("env:JUNEXIS_M12_TEST_DB");
        var previous = Environment.GetEnvironmentVariable("JUNEXIS_M12_TEST_DB");
        Environment.SetEnvironmentVariable("JUNEXIS_M12_TEST_DB", "runtime-value-not-in-lock");
        try
        {
            Assert.Equal(0, await CompileHandler.HandleAsync(root, null, null, false, "1.0", null));
            var resolved = await CliProjectInputResolver.ResolveAsync(root);
            var plan = await StorageRuntimeBindingPlanner.CreateAsync(
                resolved,
                Path.Combine(root, "runtime-host.dll"),
                resolveConfiguration: true);

            Assert.NotNull(plan);
            var startInfo = new ProcessStartInfo();
            plan.ApplyTo(startInfo);
            Assert.Equal("sqlite", startInfo.Environment["JUNEXIS_STORAGE_PROVIDER"]);
            Assert.Equal("home", startInfo.Environment["JUNEXIS_STORAGE_BINDING"]);
            Assert.Contains("runtime-value-not-in-lock", startInfo.Environment["JUNEXIS_STORAGE_CONFIG_JSON"]);
            Assert.DoesNotContain(
                "runtime-value-not-in-lock",
                await File.ReadAllTextAsync(plan.LockPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("JUNEXIS_M12_TEST_DB", previous);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsPackageChangedAfterTheBoundBuild()
    {
        var root = await CreateProjectAsync("literal:data.db");
        try
        {
            Assert.Equal(0, await CompileHandler.HandleAsync(root, null, null, false, "1.0", null));
            await File.WriteAllTextAsync(
                Path.Combine(root, "providers", "sqlite", "runtime", "provider.dll"),
                "changed after lock");
            var resolved = await CliProjectInputResolver.ResolveAsync(root);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                StorageRuntimeBindingPlanner.CreateAsync(
                    resolved,
                    Path.Combine(root, "runtime-host.dll"),
                    resolveConfiguration: true));

            Assert.Contains("differs from junexis.providers.lock/0.1", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MaterializesVerifiedHostProviderAndSecretSafeDeploymentManifest()
    {
        var root = await CreateProjectAsync("env:JUNEXIS_M12_BUNDLE_DB");
        try
        {
            Assert.Equal(0, await CompileHandler.HandleAsync(root, null, null, false, "1.0", null));
            var resolved = await CliProjectInputResolver.ResolveAsync(root);
            var plan = await StorageRuntimeBindingPlanner.CreateAsync(
                resolved,
                Path.Combine(root, "runtime-host.dll"),
                resolveConfiguration: false);
            var fakeGo = await CreateFakeGoAsync(root);

            await PackageRuntimeArtifactBuilder.BuildAsync(
                Path.Combine(root, "out"), fakeGo, "unused-npm", plan);

            var storageRoot = Path.Combine(root, "out", "server", "runtime", "storage");
            Assert.True(File.Exists(Path.Combine(storageRoot, "host", "runtime-host.dll")));
            Assert.True(File.Exists(Path.Combine(
                storageRoot, "providers", "sqlite", "runtime", "provider.dll")));
            var descriptor = await File.ReadAllTextAsync(
                Path.Combine(storageRoot, "junexis.storage.deployment.json"));
            Assert.Contains("junexis.storage.deployment/0.1", descriptor);
            Assert.Contains("JUNEXIS_M12_BUNDLE_DB", descriptor);
            Assert.DoesNotContain("runtime-value", descriptor);

            var inventory = JsonSerializer.Deserialize<ArtifactInventory>(
                await File.ReadAllTextAsync(Path.Combine(root, "out", "junexis.artifacts.json")));
            Assert.NotNull(inventory);
            Assert.Contains(inventory.Artifacts, item => item.Kind == "storage-runtime-host");
            Assert.Contains(inventory.Artifacts, item => item.Kind == "storage-provider");
            Assert.Contains(inventory.Artifacts, item => item.Kind == "storage-deployment-manifest");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunHandlerPassesOnlyTheVerifiedBindingToTheServerProcess()
    {
        var root = await CreateProjectAsync("literal:data.db");
        try
        {
            var fakeGo = await CreateFakeRunGoAsync(root);

            var exitCode = await RunHandler.HandleAsync(
                root,
                durationSeconds: 1,
                goExecutable: fakeGo,
                noOpen: true,
                storageRuntimeHost: Path.Combine(root, "runtime-host.dll"));

            // The stub captures the binding but exits before HTTP readiness.
            Assert.Equal(1, exitCode);
            var environment = await File.ReadAllTextAsync(Path.Combine(root, "storage-launch.txt"));
            Assert.Contains("sqlite|home|", environment);
            Assert.Contains("provider.dll", environment);
            Assert.Contains("data.db", environment);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateProjectAsync(string pathConfiguration)
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-m12-launch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "providers", "sqlite", "runtime"));
        await File.WriteAllTextAsync(Path.Combine(root, "runtime-host.dll"), "host");
        await File.WriteAllTextAsync(Path.Combine(root, "runtime-host.deps.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(root, "runtime-host.runtimeconfig.json"), "{}");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Junexis.Storage.Runtime.Abstractions.dll"), "abstractions");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "main.jnx"), """
            task Api() @server { }
            extern storage HomeStore {
                target server
                language go
                fn save(value: Text)
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "junexis.project.toml"), $$"""
            name = "m12-launch"
            entry = "src/main.jnx"
            sourceRoots = ["src"]
            providerPackages = ["providers/sqlite"]
            targets = ["server"]
            outDir = "out"

            [storage.home]
            declaration = "HomeStore"
            provider = "sqlite"
            providerVersion = "0.1.0"
            config.path = "{{pathConfiguration}}"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "providers", "sqlite", "junexis.provider.toml"),
            """
            schemaVersion = "junexis.provider/0.1"
            id = "sqlite"
            version = "0.1.0"
            storageContract = "junexis.storage/0.1"
            runtimeAbi = "junexis.storage.runtime/0.1"
            resourceKind = "relational"
            databaseCompatibility = "sqlite3"
            capabilities = ["health", "read", "transactions", "write"]
            files = []

            [runtime]
            kind = "dotnet"
            entrypoint = "runtime/provider.dll"

            [driver]
            id = "test.sqlite"
            version = "1.0.0"

            [configuration]
            required = ["path"]
            optional = []
            secret = []
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "providers", "sqlite", "runtime", "provider.dll"),
            "provider");
        return root;
    }

    private static async Task<string> CreateFakeGoAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-go.cmd");
            await File.WriteAllTextAsync(path,
                "@echo off\r\nif not \"%1\"==\"build\" exit /b 0\r\n:scan\r\nif \"%1\"==\"\" exit /b 0\r\nif \"%1\"==\"-o\" goto output\r\nshift\r\ngoto scan\r\n:output\r\nshift\r\ntype nul > \"%~1\"\r\nexit /b 0\r\n");
            return path;
        }
        var unix = Path.Combine(root, "fake-go.sh");
        await File.WriteAllTextAsync(unix,
            "#!/usr/bin/env sh\nif [ \"$1\" = \"build\" ]; then shift; while [ $# -gt 0 ]; do if [ \"$1\" = \"-o\" ]; then shift; : > \"$1\"; chmod +x \"$1\"; break; fi; shift; done; fi\nexit 0\n");
        File.SetUnixFileMode(unix, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return unix;
    }

    private static async Task<string> CreateFakeRunGoAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-run-go.cmd");
            await File.WriteAllTextAsync(path,
                "@echo off\r\nif \"%1\"==\"run\" echo %JUNEXIS_STORAGE_PROVIDER%^|%JUNEXIS_STORAGE_BINDING%^|%JUNEXIS_STORAGE_PROVIDER_ENTRYPOINT%^|%JUNEXIS_STORAGE_CONFIG_JSON%>\"%~dp0storage-launch.txt\"\r\nexit /b 0\r\n");
            return path;
        }
        var unix = Path.Combine(root, "fake-run-go.sh");
        await File.WriteAllTextAsync(unix,
            "#!/usr/bin/env sh\nif [ \"$1\" = \"run\" ]; then printf '%s|%s|%s|%s' \"$JUNEXIS_STORAGE_PROVIDER\" \"$JUNEXIS_STORAGE_BINDING\" \"$JUNEXIS_STORAGE_PROVIDER_ENTRYPOINT\" \"$JUNEXIS_STORAGE_CONFIG_JSON\" > \"$(dirname \"$0\")/storage-launch.txt\"; fi\nexit 0\n");
        File.SetUnixFileMode(unix, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return unix;
    }
}
