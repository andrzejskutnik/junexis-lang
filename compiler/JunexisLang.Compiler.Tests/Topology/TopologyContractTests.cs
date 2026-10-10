using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Topology;

namespace JunexisLang.Compiler.Tests.Topology;

public sealed class TopologyContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static IEnumerable<object[]> MainExamples() =>
        Directory.GetFiles(Path.Combine(RepositoryRoot, "examples"), "*.jnx")
            .OrderBy(path => path)
            .Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(MainExamples))]
    public async Task EveryMainExampleBuildsLogicalTopologyWithoutErrors(string path)
    {
        var parse = await new JunexisCompiler().ParseAsync(new ParseOptions(path));
        Assert.True(parse.Success);
        var ir = new AstToIrTransformer().TransformWithDiagnostics(parse.Ast!);
        Assert.True(ir.Success);

        var topology = TopologyAnalyzer.Analyze(ir.Document!);

        Assert.DoesNotContain(topology.Diagnostics,
            diagnostic => diagnostic.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void DanglingEndpointDiagnosticHasSourceLocation()
    {
        var ir = Transform("""
            task Existing() { }
            pipeline Broken {
                use Existing -> Missing
            }
            """);

        var diagnostic = Assert.Single(
            TopologyAnalyzer.Analyze(ir).Diagnostics,
            item => item.Code == "T0002");

        Assert.Equal("topology-test.jnx", diagnostic.File);
        Assert.Equal(3, diagnostic.Line);
        Assert.True(diagnostic.Column > 0);
    }

    [Fact]
    public void CycleDiagnosticHasTaskSourceLocation()
    {
        var ir = Transform("""
            task A() { }
            task B() { }
            pipeline Cycle {
                use A -> B
                use B -> A
            }
            """);

        var diagnostic = Assert.Single(
            TopologyAnalyzer.Analyze(ir).Diagnostics,
            item => item.Code == "T0001");

        Assert.Equal("topology-test.jnx", diagnostic.File);
        Assert.Contains(diagnostic.Line, new[] { 1, 2 });
        Assert.True(diagnostic.Column > 0);
    }

    private static IRDocument Transform(string source)
    {
        var parse = new JunexisCompiler().ParseSource(source, "topology-test.jnx");
        Assert.True(parse.Success);
        var result = new AstToIrTransformer().TransformWithDiagnostics(parse.Ast!);
        Assert.True(result.Success);
        return result.Document!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ROADMAP.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "examples")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the junexis-lang repository root.");
    }
}
