using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

/// <summary>Testy transformacji instrukcji taska do IR.</summary>
public sealed partial class IrTransformerTests
{
    [Fact]
    public async Task Task_LetStatement_TransformsToIRLetStmt()
    {
        var ir = await TransformAsync("task Foo() { let x = 42 }");
        var body = ir.Tasks[0].Body;
        Assert.Single(body);
        var let = Assert.IsType<IRLetStmt>(body[0]);
        Assert.Equal("x", let.Name);
        var lit = Assert.IsType<IRLiteralExpr>(let.Init);
        Assert.Equal(42L, ((IRIntValue)lit.Value).Value);
    }

    [Fact]
    public async Task Task_EveryStatement_TransformsToIREveryStmt()
    {
        var ir = await TransformAsync("""
            task Sensor() {
                every 1000 {
                    log("tick")
                }
            }
            """);
        var body = ir.Tasks[0].Body;
        Assert.Single(body);
        var every = Assert.IsType<IREveryStmt>(body[0]);
        Assert.NotEmpty(every.Body);
    }

    [Fact]
    public async Task Task_MatchStatement_TransformsToIRMatchStmt()
    {
        var ir = await TransformAsync("""
            task Foo() {
                match x {
                    1 -> { return x }
                    _ -> { return 0 }
                }
            }
            """);
        var match = Assert.IsType<IRMatchStmt>(ir.Tasks[0].Body[0]);
        Assert.Equal(2, match.Arms.Count);
        Assert.IsType<IRLiteralPattern>(match.Arms[0].Pattern);
        Assert.IsType<IRWildcardPattern>(match.Arms[1].Pattern);
    }

    [Fact]
    public async Task Task_IfElse_TransformsToIRIfStmt()
    {
        var ir = await TransformAsync("""
            task Foo() {
                if x > 0 {
                    return x
                } else {
                    return 0
                }
            }
            """);
        var ifStmt = Assert.IsType<IRIfStmt>(ir.Tasks[0].Body[0]);
        Assert.NotEmpty(ifStmt.Then);
        var elseBlock = Assert.IsType<IRElseBlock>(ifStmt.Else);
        Assert.Single(elseBlock.Body);
    }
}