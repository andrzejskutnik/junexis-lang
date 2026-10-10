using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class ProjectManifestTests
{
    [Fact]
    public async Task LoadAsync_ReadsTomlManifest()
    {
        var root = CreateTempProject();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, JunexisProjectManifest.FileName),
                """
                # Junexis project manifest
                name = "temperature-monitor"
                entry = "src/main.jnx"
                sourceRoots = ["src", "libraries/local"]
                packages = ["junexis.observation@0.1.0-experimental"]
                packageIndex = "packages/index.json"
                providerPackages = ["providers/postgresql"]
                targets = ["embedded", "server", "report"]
                outDir = "out"

                [storage.home]
                declaration = "HomeStore"
                provider = "postgresql"
                providerVersion = "0.1.0"
                config.connection = "secret:HOME_DATABASE_URL"
                config.poolSize = "10"
                """);

            var manifest = await JunexisProjectManifest.LoadAsync(
                new FileInfo(Path.Combine(root, JunexisProjectManifest.FileName)));

            Assert.Equal("temperature-monitor", manifest.Name);
            Assert.Equal("src/main.jnx", manifest.Entry);
            Assert.Equal(["src", "libraries/local"], manifest.SourceRoots);
            Assert.Equal(["junexis.observation@0.1.0-experimental"], manifest.Packages);
            Assert.Equal("packages/index.json", manifest.PackageIndex);
            Assert.Equal(["providers/postgresql"], manifest.ProviderPackages);
            Assert.Equal(["embedded", "server", "report"], manifest.Targets);
            Assert.Equal("out", manifest.OutDir);
            var binding = Assert.Single(manifest.StorageBindings);
            Assert.Equal("home", binding.Name);
            Assert.Equal("HomeStore", binding.Declaration);
            Assert.Equal("postgresql", binding.Provider);
            Assert.Equal("0.1.0", binding.ProviderVersion);
            Assert.Equal("secret:HOME_DATABASE_URL", binding.Configuration["connection"]);
            Assert.Equal("10", binding.Configuration["poolSize"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsPackageVersionRange()
    {
        var root = CreateTempProject();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, JunexisProjectManifest.FileName),
                """
                name = "broken-package"
                entry = "src/main.jnx"
                packages = ["junexis.observation@^0.1.0"]
                packageIndex = "packages/index.json"
                targets = ["server"]
                outDir = "out"
                """);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => JunexisProjectManifest.LoadAsync(new FileInfo(Path.Combine(root, JunexisProjectManifest.FileName))));

            Assert.Contains("id@exact-version", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_DefaultsSourceRootsToSrcForExistingProjects()
    {
        var root = CreateTempProject();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, JunexisProjectManifest.FileName),
                """
                name = "compatible"
                entry = "src/main.jnx"
                targets = ["server"]
                outDir = "out"
                """);

            var manifest = await JunexisProjectManifest.LoadAsync(
                new FileInfo(Path.Combine(root, JunexisProjectManifest.FileName)));

            Assert.Equal(["src"], manifest.SourceRoots);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_FindsNearestManifestFromNestedDirectory()
    {
        var root = CreateTempProject();

        try
        {
            var nested = Path.Combine(root, "src", "nested");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(root, JunexisProjectManifest.FileName),
                """
                name = "project-a"
                entry = "src/main.jnx"
                targets = ["server"]
                outDir = "out"
                """);

            var project = await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(nested));

            Assert.NotNull(project);
            Assert.Equal(root, project.RootDirectory.FullName);
            Assert.Equal(Path.Combine(root, "src", "main.jnx"), project.EntryFile.FullName);
            Assert.Equal(Path.Combine(root, "out"), project.OutDirectory.FullName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsManifestWithoutTargets()
    {
        var root = CreateTempProject();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, JunexisProjectManifest.FileName),
                """
                name = "broken"
                entry = "src/main.jnx"
                targets = []
                outDir = "out"
                """);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => JunexisProjectManifest.LoadAsync(new FileInfo(Path.Combine(root, JunexisProjectManifest.FileName))));

            Assert.Contains("empty 'targets'", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
