using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class ProviderPackageTests
{
    [Fact]
    public async Task Build_WritesProviderLockForMatchingStorageDeclaration()
    {
        var root = await CreateProjectAsync("secret:HOME_DATABASE_URL", includeStorageDeclaration: true);
        try
        {
            var exitCode = await CompileHandler.HandleAsync(root, null, null, false, "1.0", null);

            Assert.Equal(0, exitCode);
            var lockPath = Path.Combine(root, "out", "debug", "junexis.providers.lock.json");
            Assert.True(File.Exists(lockPath));
            var first = await File.ReadAllTextAsync(lockPath);
            Assert.Contains("junexis.providers.lock/0.1", first);
            Assert.Contains("HomeStore", first);

            Assert.Equal(0, await CompileHandler.HandleAsync(root, null, null, false, "1.0", null));
            Assert.Equal(first, await File.ReadAllTextAsync(lockPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Build_RejectsBindingForUnknownStorageDeclaration()
    {
        var root = await CreateProjectAsync("secret:HOME_DATABASE_URL");
        try
        {
            var exitCode = await CompileHandler.HandleAsync(root, null, null, false, "1.0", null);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(Path.Combine(root, "out", "debug", "junexis.providers.lock.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Build_RejectsStorageDeclarationWithoutBindingWhenProviderPackagesAreEnabled()
    {
        var root = await CreateProjectAsync("secret:HOME_DATABASE_URL", includeStorageDeclaration: true);
        try
        {
            var manifestPath = Path.Combine(root, JunexisProjectManifest.FileName);
            var manifest = await File.ReadAllTextAsync(manifestPath);
            await File.WriteAllTextAsync(
                manifestPath,
                manifest[..manifest.IndexOf("[storage.home]", StringComparison.Ordinal)]);

            var exitCode = await CompileHandler.HandleAsync(root, null, null, false, "1.0", null);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(Path.Combine(root, "out", "debug", "junexis.providers.lock.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_LoadsProviderAndWritesDeterministicSecretSafeLock()
    {
        var root = await CreateProjectAsync("secret:HOME_DATABASE_URL");
        try
        {
            var project = await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(root));
            Assert.NotNull(project);

            var first = await ProviderPackageResolver.ResolveAsync(project);
            var firstLock = ProviderLockWriter.Generate(first, project.RootDirectory).Content;
            var second = await ProviderPackageResolver.ResolveAsync(project);
            var secondLock = ProviderLockWriter.Generate(second, project.RootDirectory).Content;

            Assert.Equal(firstLock, secondLock);
            using var document = JsonDocument.Parse(firstLock);
            var lockRoot = document.RootElement;
            Assert.Equal("junexis.providers.lock/0.1", lockRoot.GetProperty("schemaVersion").GetString());
            var provider = lockRoot.GetProperty("providers")[0];
            Assert.Equal("postgresql", provider.GetProperty("id").GetString());
            Assert.Equal("junexis.storage.runtime/0.1", provider.GetProperty("runtimeAbi").GetString());
            Assert.Equal(64, provider.GetProperty("packageSha256").GetString()!.Length);
            var configuration = lockRoot.GetProperty("bindings")[0].GetProperty("configuration")[0];
            Assert.Equal("secret", configuration.GetProperty("source").GetString());
            Assert.Equal("HOME_DATABASE_URL", configuration.GetProperty("reference").GetString());
            Assert.False(configuration.TryGetProperty("value", out _));
            Assert.DoesNotContain("postgresql://", firstLock, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_RejectsLiteralForSecretConfiguration()
    {
        var root = await CreateProjectAsync("postgresql://admin:unsafe@example/home");
        try
        {
            var project = await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(root));
            Assert.NotNull(project);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => ProviderPackageResolver.ResolveAsync(project));

            Assert.Contains("must use secret: or env:", error.Message);
            Assert.DoesNotContain("admin:unsafe", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ChangesPackageHashWhenDeclaredFileChanges()
    {
        var root = await CreateProjectAsync("env:HOME_DATABASE_URL");
        try
        {
            var project = await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(root));
            Assert.NotNull(project);
            var before = await ProviderPackageResolver.ResolveAsync(project);

            await File.WriteAllTextAsync(
                Path.Combine(root, "providers", "postgresql", "runtime", "provider.bin"),
                "changed runtime");
            var after = await ProviderPackageResolver.ResolveAsync(project);

            Assert.NotEqual(before.Packages[0].PackageSha256, after.Packages[0].PackageSha256);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_RejectsProviderPackageEscapingProject()
    {
        var root = await CreateProjectAsync("secret:HOME_DATABASE_URL");
        try
        {
            var manifestPath = Path.Combine(root, JunexisProjectManifest.FileName);
            var source = await File.ReadAllTextAsync(manifestPath);
            await File.WriteAllTextAsync(
                manifestPath,
                source.Replace("providers/postgresql", "../postgresql", StringComparison.Ordinal));
            var project = await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(root));
            Assert.NotNull(project);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => ProviderPackageResolver.ResolveAsync(project));

            Assert.Contains("escapes the project", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_RejectsProviderVersionMismatch()
    {
        var root = await CreateProjectAsync("secret:HOME_DATABASE_URL");
        try
        {
            var manifestPath = Path.Combine(root, JunexisProjectManifest.FileName);
            var source = await File.ReadAllTextAsync(manifestPath);
            await File.WriteAllTextAsync(
                manifestPath,
                source.Replace("providerVersion = \"0.1.0\"", "providerVersion = \"0.2.0\"", StringComparison.Ordinal));
            var project = await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(root));
            Assert.NotNull(project);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => ProviderPackageResolver.ResolveAsync(project));

            Assert.Contains("requires provider 'postgresql' version '0.2.0'", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProviderManifest_RejectsUnknownKey()
    {
        var root = await CreateProjectAsync("secret:HOME_DATABASE_URL");
        try
        {
            var manifestPath = Path.Combine(root, "providers", "postgresql", ProviderPackageManifest.FileName);
            await File.AppendAllTextAsync(manifestPath, "\nunknown = \"value\"\n");

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => ProviderPackageManifest.LoadAsync(new FileInfo(manifestPath)));

            Assert.Contains("Unknown provider key 'unknown'", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateProjectAsync(string connection, bool includeStorageDeclaration = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-provider-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "providers", "postgresql", "runtime"));
        var source = includeStorageDeclaration
            ? """
              task Api() @server { }
              extern storage HomeStore {
                  target server
                  language go
                  fn save(value: Text)
              }
              """
            : "task Api() @server { }";
        await File.WriteAllTextAsync(Path.Combine(root, "src", "main.jnx"), source);
        await File.WriteAllTextAsync(
            Path.Combine(root, JunexisProjectManifest.FileName),
            $$"""
            name = "provider-test"
            entry = "src/main.jnx"
            sourceRoots = ["src"]
            providerPackages = ["providers/postgresql"]
            targets = ["server"]
            outDir = "out"

            [storage.home]
            declaration = "HomeStore"
            provider = "postgresql"
            providerVersion = "0.1.0"
            config.connection = "{{connection}}"
            config.poolSize = "10"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "providers", "postgresql", ProviderPackageManifest.FileName),
            """
            schemaVersion = "junexis.provider/0.1"
            id = "postgresql"
            version = "0.1.0"
            storageContract = "junexis.storage/0.1"
            runtimeAbi = "junexis.storage.runtime/0.1"
            resourceKind = "relational"
            databaseCompatibility = ">=16 <17"
            capabilities = ["records", "transactions", "migrations", "native-sql"]
            files = []

            [runtime]
            kind = "dotnet"
            entrypoint = "runtime/provider.bin"

            [driver]
            id = "npgsql"
            version = "10.0.0"

            [configuration]
            required = ["connection"]
            optional = ["poolSize"]
            secret = ["connection"]
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "providers", "postgresql", "runtime", "provider.bin"),
            "provider runtime");
        return root;
    }
}
