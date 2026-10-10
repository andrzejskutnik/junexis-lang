using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.Tests.Projects;

public sealed class ProjectSymbolBinderTests
{
    [Fact]
    public async Task Bind_AppliesGlobModuleAliasAndSelectiveAlias()
    {
        using var project = TestProject.Create(
            ("src/main.jnx", "import domain::types\nimport domain::types as domain\nimport domain::types { Observation as Reading }\nstruct Local { ready: Bool }"),
            ("src/domain/types.jnx", "struct Observation { value: Float }\nfn Normalize(value: Float) -> Float { return value; }"));
        var resolution = await ProjectImportResolver.ResolveAsync(project.Catalog);

        var result = ProjectSymbolBinder.Bind(resolution.Graph);

        Assert.True(result.Success);
        var scope = result.Scopes["main"];
        Assert.Contains("Observation", scope.Keys);
        Assert.Contains("Normalize", scope.Keys);
        Assert.Contains("domain::Observation", scope.Keys);
        Assert.Contains("domain::Normalize", scope.Keys);
        Assert.Contains("Reading", scope.Keys);
        Assert.True(scope["Local"].IsLocal);
    }

    [Fact]
    public async Task Bind_ReportsMissingSelectiveSymbol()
    {
        using var project = TestProject.Create(
            ("src/main.jnx", "import domain::types { Missing }\nstruct Local { ready: Bool }"),
            ("src/domain/types.jnx", "struct Observation { value: Float }"));
        var resolution = await ProjectImportResolver.ResolveAsync(project.Catalog);

        var result = ProjectSymbolBinder.Bind(resolution.Graph);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "E0007");
    }

    [Fact]
    public async Task Bind_ReportsGlobCollisionWithLocalDeclaration()
    {
        using var project = TestProject.Create(
            ("src/main.jnx", "import domain::types\nstruct Observation { local: Bool }"),
            ("src/domain/types.jnx", "struct Observation { value: Float }"));
        var resolution = await ProjectImportResolver.ResolveAsync(project.Catalog);

        var result = ProjectSymbolBinder.Bind(resolution.Graph);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "E0002");
    }

    [Fact]
    public async Task Bind_WarnsThatModuleAliasDoesNotExposeQualifiedNamedQueries()
    {
        using var project = TestProject.Create(
            ("src/main.jnx", "import data::home as data\ntask Main() @server { }"),
            ("src/data/home.jnx", "storage HomeData { entity Item { id: Uuid key(id) } query All() from Item { project id } }"));
        var resolution = await ProjectImportResolver.ResolveAsync(project.Catalog);

        var result = ProjectSymbolBinder.Bind(resolution.Graph);

        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "SQC006");
        Assert.Equal(DiagnosticLevel.Warning, warning.Level);
        Assert.True(result.Success);
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

        internal static TestProject Create(params (string Path, string Source)[] files)
        {
            var root = Path.Combine(Path.GetTempPath(), $"junexis-symbols-{Guid.NewGuid():N}");
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
