using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

public sealed class IrTransformerContractTests
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
    public async Task EveryMainExampleTransformsToStructuralIr(string path) => await AssertTransforms(path);

    [Theory]
    [MemberData(nameof(GrammarExamples))]
    public async Task EveryGrammarExampleTransformsToStructuralIr(string path) => await AssertTransforms(path);

    [Fact]
    public void ExpressionInitializersArePreserved()
    {
        var ir = Transform("""
            struct Sample { created: Time = now() }
            task Work(since: Time = epoch()) {
                state started: Time = now()
            }
            """);

        AssertCall(ir.Structs[0].Fields[0].Default, "now");
        AssertCall(ir.Tasks[0].Params[0].Default, "epoch");
        AssertCall(ir.Tasks[0].States[0].Init, "now");
    }

    [Fact]
    public void DurationAndMemoryLiteralsBecomeUnitValues()
    {
        var ir = Transform("""
            task Work() {
                config {
                    timeout: Duration : 5s
                    budget: Int : 32MB
                }
            }
            """);

        var config = ir.Tasks[0].Config!;
        var timeout = Assert.IsType<IRLiteralExpr>(config.Fields[0].Default);
        Assert.Equal(new IRUnitValue(5, "s"), timeout.Value);
        var budget = Assert.IsType<IRLiteralExpr>(config.Fields[1].Default);
        Assert.Equal(new IRUnitValue(32, "MB"), budget.Value);
    }

    [Fact]
    public async Task TaskAndDeviceNativeSectionsArePreserved()
    {
        var path = Path.Combine(RepositoryRoot, "examples", "grammar", "08-device-and-native.jnx");
        var parse = await new JunexisCompiler().ParseAsync(new ParseOptions(path));
        Assert.True(parse.Success);

        var ir = new AstToIrTransformer().Transform(parse.Ast!);

        var task = Assert.Single(ir.Tasks, task => task.Name == "SensorTask");
        Assert.NotNull(task.Native);
        Assert.Contains(task.Native.Languages, language => language.Name == "cpp");
        var device = Assert.Single(ir.Devices, device => device.Name == "sensor_with_lib");
        Assert.NotNull(device.Native);
    }

    [Fact]
    public void SourceMapIncludesNestedInitializerLocation()
    {
        var ir = Transform("\n\ntask Work() {\n  state started: Time = now()\n}");

        var entry = Assert.Single(ir.SourceMap,
            item => item.Path == "/Tasks[0]/States[0]/Init");
        Assert.Equal("ir-contract.jnx", entry.Source.File);
        Assert.Equal(4, entry.Source.Line);
        Assert.True(entry.Source.Column > 1);
        Assert.Equal(ir.SourceMap.Count, ir.SourceMap.Select(item => item.Path).Distinct().Count());
    }

    [Fact]
    public void MultipleConfigBlocksReturnControlledDiagnostic()
    {
        var parse = new JunexisCompiler().ParseSource("task Work() { config { } config { } }", "duplicate-config.jnx");
        Assert.True(parse.Success);

        var result = new AstToIrTransformer().TransformWithDiagnostics(parse.Ast!);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("R0001", diagnostic.Code);
        Assert.Equal("duplicate-config.jnx", diagnostic.File);
        Assert.True(diagnostic.Line > 0);
    }

    private static IRDocument Transform(string source)
    {
        var parse = new JunexisCompiler().ParseSource(source, "ir-contract.jnx");
        Assert.True(parse.Success,
            string.Join(Environment.NewLine, parse.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var result = new AstToIrTransformer().TransformWithDiagnostics(parse.Ast!);
        Assert.True(result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return result.Document!;
    }

    private static void AssertCall(IRExpr? expression, string name)
    {
        var call = Assert.IsType<IRCallExpr>(expression);
        Assert.Equal(name, Assert.IsType<IRIdentExpr>(call.Callee).Name);
    }

    private static async Task AssertTransforms(string path)
    {
        var parse = await new JunexisCompiler().ParseAsync(new ParseOptions(path));
        Assert.True(parse.Success);
        var result = new AstToIrTransformer().TransformWithDiagnostics(parse.Ast!);
        Assert.True(result.Success,
            $"{Path.GetFileName(path)}: " +
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.NotNull(result.Document);
        Assert.NotEmpty(result.Document.SourceMap);
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
