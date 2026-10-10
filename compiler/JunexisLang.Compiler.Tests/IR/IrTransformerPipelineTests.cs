using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

/// <summary>Testy podstawowej transformacji pipeline do IR.</summary>
public sealed partial class IrTransformerTests
{
    [Fact]
    public async Task Pipeline_UseConnections_TransformsToIRPipeline()
    {
        var ir = await TransformAsync("""
            task A() { }
            task B() { }
            pipeline Main {
                use A -> B
            }
            """);
        Assert.Single(ir.Pipelines);
        var pipeline = ir.Pipelines[0];
        Assert.Equal("Main", pipeline.Name);
        Assert.Single(pipeline.Connections);
        var connection = Assert.IsType<IRUseConn>(pipeline.Connections[0]);
        Assert.Equal("A", connection.SourceTask);
        Assert.Equal("B", connection.TargetTask);
    }
}