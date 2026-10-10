using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.Tests.Projects;

public sealed class ProjectModuleCatalogTests
{
    [Fact]
    public void Discover_MapsPathsToDeterministicallyOrderedModuleIdentities()
    {
        using var project = TestProject.Create(
            "src/main.jnx",
            "src/domain/observations.jnx",
            "src/domain/decisions.jnx");

        var catalog = ProjectModuleCatalog.Discover(project.Root, ["src"]);

        Assert.Equal(
            ["domain::decisions", "domain::observations", "main"],
            catalog.Modules.Select(module => module.Identity.Value));
        Assert.True(catalog.TryResolve(new ModuleIdentity("domain::observations"), out var observation));
        Assert.Equal("observations.jnx", observation!.SourceFile.Name);
        Assert.False(catalog.TryResolve(new ModuleIdentity("Domain::observations"), out _));
    }

    [Fact]
    public void Discover_RejectsSourceRootOutsideProject()
    {
        using var project = TestProject.Create("src/main.jnx");

        var error = Assert.Throws<InvalidDataException>(
            () => ProjectModuleCatalog.Discover(project.Root, ["../shared"]));

        Assert.Contains("escapes the project", error.Message);
    }

    [Fact]
    public void Discover_RejectsIdentityCollisionAcrossSourceRoots()
    {
        using var project = TestProject.Create("src/domain/model.jnx", "library/domain/model.jnx");

        var error = Assert.Throws<InvalidDataException>(
            () => ProjectModuleCatalog.Discover(project.Root, ["src", "library"]));

        Assert.Contains("domain::model", error.Message);
        Assert.Contains("ambiguous", error.Message);
    }

    [Fact]
    public void Discover_RejectsOverlappingSourceRoots()
    {
        using var project = TestProject.Create("src/domain/model.jnx");

        var error = Assert.Throws<InvalidDataException>(
            () => ProjectModuleCatalog.Discover(project.Root, ["src", "src/domain"]));

        Assert.Contains("overlapping source roots", error.Message);
    }

    [Fact]
    public void FromRelativeSourcePath_RejectsNonPortableSegment()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => ModuleIdentity.FromRelativeSourcePath("home-system/observations.jnx"));

        Assert.Contains("invalid module segment", error.Message);
    }

    private sealed class TestProject : IDisposable
    {
        private TestProject(string path) => Root = new DirectoryInfo(path);

        internal DirectoryInfo Root { get; }

        internal static TestProject Create(params string[] sourcePaths)
        {
            var root = Path.Combine(Path.GetTempPath(), $"junexis-modules-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            foreach (var sourcePath in sourcePaths)
            {
                var path = Path.Combine(root, sourcePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "// module fixture");
            }

            return new TestProject(root);
        }

        public void Dispose() => Directory.Delete(Root.FullName, recursive: true);
    }
}
