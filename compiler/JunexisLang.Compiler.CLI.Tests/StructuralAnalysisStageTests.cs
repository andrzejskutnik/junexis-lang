using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class StructuralAnalysisStageTests
{
    [Fact]
    public void Analyze_DanglingTask_FailsWithTopologyDiagnostic()
    {
        var parse = new JunexisCompiler().ParseSource("""
            task A() { }
            pipeline Broken { use A -> Missing }
            """);
        Assert.True(parse.Success);

        var result = StructuralAnalysisStage.Analyze(parse.Ast!);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "T0002");
    }

    [Fact]
    public void Analyze_Cycle_RemainsSuccessfulWarning()
    {
        var parse = new JunexisCompiler().ParseSource("""
            task A() { }
            task B() { }
            pipeline Cycle { use A -> B use B -> A }
            """);
        Assert.True(parse.Success);

        var result = StructuralAnalysisStage.Analyze(parse.Ast!);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "T0001");
    }
}
