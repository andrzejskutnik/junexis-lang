using System.Security.Cryptography;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Tests;

[CollectionDefinition("installed-provider-profile", DisableParallelization = true)]
public sealed class InstalledProviderProfileCollection;

[Collection("installed-provider-profile")]
public sealed class InstalledProviderResolutionTests : IDisposable
{
    private const string SdkVersion = "0.1.0-preview.10";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"junexis-installed-provider-{Guid.NewGuid():N}");
    private readonly string? _previousProviderHome = Environment.GetEnvironmentVariable("JUNEXIS_PROVIDER_HOME");

    [Fact]
    public async Task Build_ResolvesInstalledJsonProviderWithoutLeakingProfilePath()
    {
        var profile = await CreateInstalledProviderAsync("memory", "0.1.0", SdkVersion);
        var projectRoot = await CreateProjectAsync(SdkVersion);
        Environment.SetEnvironmentVariable("JUNEXIS_PROVIDER_HOME", profile);

        var exitCode = await CompileHandler.HandleAsync(projectRoot, null, null, false, "1.0", null);

        Assert.Equal(0, exitCode);
        var lockSource = await File.ReadAllTextAsync(
            Path.Combine(projectRoot, "out", "debug", "junexis.providers.lock.json"));
        Assert.Contains("junexis.provider/0.2", lockSource);
        Assert.Contains("profile:memory@0.1.0/sdk:0.1.0-preview.10", lockSource);
        Assert.DoesNotContain(profile, lockSource, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(lockSource);
        Assert.Equal("memory", document.RootElement.GetProperty("bindings")[0].GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Resolve_RejectsProviderInstalledForDifferentSdk()
    {
        var profile = await CreateInstalledProviderAsync("memory", "0.1.0", "0.1.0-preview.9");
        var projectRoot = await CreateProjectAsync(SdkVersion);
        Environment.SetEnvironmentVariable("JUNEXIS_PROVIDER_HOME", profile);
        var project = await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(projectRoot));
        Assert.NotNull(project);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => ProviderPackageResolver.ResolveAsync(project));

        Assert.Contains("for SDK '0.1.0-preview.10' was not found", error.Message);
        Assert.Contains("junexis provider install", error.Message);
    }

    private async Task<string> CreateProjectAsync(string sdkVersion)
    {
        var project = Path.Combine(_root, $"project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(project, "src"));
        await File.WriteAllTextAsync(Path.Combine(project, "src", "main.jnx"),
            "task Api() @server { }\nextern storage HomeStore { target server language go fn save(value: Text) }\n");
        await File.WriteAllTextAsync(Path.Combine(project, JunexisProjectManifest.FileName), $$"""
            name = "installed-provider-test"
            entry = "src/main.jnx"
            sourceRoots = ["src"]
            providerSdkVersion = "{{sdkVersion}}"
            targets = ["server"]
            outDir = "out"

            [storage.home]
            declaration = "HomeStore"
            provider = "memory"
            providerVersion = "0.1.0"
            """);
        return project;
    }

    private async Task<string> CreateInstalledProviderAsync(string id, string version, string sdkVersion)
    {
        var profile = Path.Combine(_root, $"profile-{Guid.NewGuid():N}");
        var relative = $"packages/{id}/{version}/{sdkVersion}";
        var package = Path.Combine(profile, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.Combine(package, "runtime"));
        var manifestPath = Path.Combine(package, ProviderPackageManifest.JsonFileName);
        await File.WriteAllTextAsync(Path.Combine(package, "runtime", "provider.dll"), "provider runtime");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.provider/0.2",
            id,
            packageId = "Junexis.Storage.Provider.Memory",
            version,
            abiVersion = "junexis.storage.runtime/0.1",
            factoryType = "Test.MemoryProvider",
            resourceKind = "key-value",
            capabilities = new[] { "read", "write" },
            sdk = new { packageId = "Junexis.Storage.Runtime.Abstractions", version = "0.1.0" },
            artifact = new { project = "provider.csproj", assembly = "provider.dll", targetFramework = "net10.0" },
            distribution = new
            {
                storageContract = "junexis.storage/0.1",
                databaseCompatibility = "not-applicable",
                runtime = new { kind = "dotnet", entrypoint = "runtime/provider.dll" },
                driver = new { id = "dotnet-bcl", version = "10.0" },
                files = Array.Empty<string>(),
                configuration = new { required = Array.Empty<string>(), optional = Array.Empty<string>(), secret = Array.Empty<string>() }
            }
        }));
        await File.WriteAllTextAsync(Path.Combine(package, "junexis.provider.release.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.provider.release/0.1",
            platformVersion = sdkVersion,
            provider = new { id, version, manifest = ProviderPackageManifest.JsonFileName, manifestSha256 = Hash(manifestPath) },
            sdk = new { packageId = "Junexis.Storage.Runtime.Abstractions", packageVersion = sdkVersion, abiVersion = "junexis.storage.runtime/0.1" },
            source = new { repository = "test", revision = new string('0', 40), state = "clean" },
            files = Array.Empty<object>()
        }));
        Directory.CreateDirectory(profile);
        await File.WriteAllTextAsync(Path.Combine(profile, "registry.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.provider-registry/0.1",
            providers = new[]
            {
                new { id, providerVersion = version, sdkVersion, contract = "junexis.provider/0.2", abiVersion = "junexis.storage.runtime/0.1", path = relative }
            }
        }));
        return profile;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("JUNEXIS_PROVIDER_HOME", _previousProviderHome);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
