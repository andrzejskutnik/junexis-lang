using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.Tests.Parser;

public sealed class ParserContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static IEnumerable<object[]> MainExamples() =>
        Directory.GetFiles(Path.Combine(RepositoryRoot, "examples"), "*.jnx")
            .OrderBy(path => path)
            .Select(path => new object[] { path });

    public static IEnumerable<object[]> GrammarExamples() =>
        Directory.GetFiles(Path.Combine(RepositoryRoot, "examples", "grammar"), "*.jnx")
            .OrderBy(path => path)
            .Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(MainExamples))]
    public async Task EveryMainExampleBuildsAst(string path) => await AssertParses(path);

    [Theory]
    [MemberData(nameof(GrammarExamples))]
    public async Task EveryGrammarExampleBuildsAst(string path) => await AssertParses(path);

    [Fact]
    public void ParseSourceReportsOneBasedSourceLocations()
    {
        const string source = "\n\nstruct Point {\n  x: Float;\n}";

        var result = new JunexisCompiler().ParseSource(source, "location.jnx");

        Assert.True(result.Success);
        var declaration = Assert.Single(result.Ast!.Declarations);
        Assert.Equal("location.jnx", declaration.File);
        Assert.Equal(3, declaration.Line);
        Assert.Equal(1, declaration.Column);
    }

    [Fact]
    public void SyntaxDiagnosticsUseParserNamespaceAndSourceLocation()
    {
        var result = new JunexisCompiler().ParseSource("struct {", "broken.jnx");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, item =>
        {
            Assert.Equal("P0001", item.Code);
            Assert.Equal("broken.jnx", item.File);
            Assert.Equal(1, item.Line);
            Assert.True(item.Column >= 1);
        });
    }

    private static async Task AssertParses(string path)
    {
        var result = await new JunexisCompiler().ParseAsync(new ParseOptions(path));
        Assert.True(result.Success,
            $"{Path.GetFileName(path)} failed to parse:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.NotNull(result.Ast);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ROADMAP.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "examples", "grammar")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the junexis-lang repository root.");
    }
}
