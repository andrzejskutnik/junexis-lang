using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.Tests.Projects;

public sealed class ProjectImportResolverTests
{
    [Fact]
    public async Task ResolveAsync_PreservesGlobAliasAndSelectiveImports()
    {
        using var project = TestProject.Create(
            ("src/main.jnx",
                """
                import domain::types
                import domain::types as types
                import domain::types { Observation as Reading }

                struct MainState { ready: Bool }
                """),
            ("src/domain/types.jnx", "struct Observation { value: Float }"));

        var result = await ProjectImportResolver.ResolveAsync(project.Catalog);

        Assert.True(result.Success);
        Assert.Equal(3, result.Graph.Edges.Count);
        Assert.Null(result.Graph.Edges[0].Import.Alias);
        Assert.Equal("types", result.Graph.Edges[1].Import.Alias);
        Assert.Equal("Reading", result.Graph.Edges[2].Import.Symbols.Single().Alias);
        Assert.Equal(
            ["domain::types", "main"],
            result.Graph.BuildOrder.Select(document => document.Module.Identity.Value));
    }

    [Fact]
    public async Task ResolveAsync_ReportsMissingModuleAtImportLocation()
    {
        using var project = TestProject.Create(
            ("src/main.jnx", "import missing::module\nstruct MainState { ready: Bool }"));

        var result = await ProjectImportResolver.ResolveAsync(project.Catalog);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "E0004");
        Assert.False(result.Success);
        Assert.Equal(project.PathOf("src/main.jnx"), diagnostic.File);
        Assert.Equal(1, diagnostic.Line);
    }

    [Fact]
    public async Task ResolveAsync_ReportsPortableCaseMismatch()
    {
        using var project = TestProject.Create(
            ("src/main.jnx", "import Domain::types\nstruct MainState { ready: Bool }"),
            ("src/domain/types.jnx", "struct Observation { value: Float }"));

        var result = await ProjectImportResolver.ResolveAsync(project.Catalog);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "E0006");
        Assert.Contains("domain::types", diagnostic.Message);
    }

    [Fact]
    public async Task ResolveAsync_ReportsCompleteCyclePath()
    {
        using var project = TestProject.Create(
            ("src/a.jnx", "import b\nstruct A { value: Int }"),
            ("src/b.jnx", "import c\nstruct B { value: Int }"),
            ("src/c.jnx", "import a\nstruct C { value: Int }"));

        var result = await ProjectImportResolver.ResolveAsync(project.Catalog);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "E0005");
        Assert.Equal("Module import cycle: a -> b -> c -> a.", diagnostic.Message);
        Assert.Equal(project.PathOf("src/c.jnx"), diagnostic.File);
    }

    [Fact]
    public async Task ResolveAsync_PreservesParserErrorFromImportedModuleWithoutFalseCaseMismatch()
    {
        using var project = TestProject.Create(
            ("src/main.jnx", "import broken\nstruct MainState { ready: Bool }"),
            ("src/broken.jnx", "struct Broken {"));

        var result = await ProjectImportResolver.ResolveAsync(project.Catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.File == project.PathOf("src/broken.jnx"));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "E0004" or "E0006");
    }

    private sealed class TestProject : IDisposable
    {
        private TestProject(string root)
        {
            Root = root;
            Catalog = ProjectModuleCatalog.Discover(new DirectoryInfo(root), ["src"]);
        }

        internal string Root { get; }

        internal ProjectModuleCatalog Catalog { get; }

        internal string PathOf(string relativePath) => Path.GetFullPath(Path.Combine(Root, relativePath));

        internal static TestProject Create(params (string Path, string Source)[] files)
        {
            var root = Path.Combine(Path.GetTempPath(), $"junexis-imports-{Guid.NewGuid():N}");
            foreach (var file in files)
            {
                var path = Path.Combine(root, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Source);
            }

            return new TestProject(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
