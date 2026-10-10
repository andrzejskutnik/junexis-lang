namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_ExplicitSendTarget_UsesJunexisSendDispatcher()
    {
        const string source = """
            task Forward(outbox: queue<Int>) @server {
                send 42 to outbox;
            }
            """;

        var ir = await TransformToIrAsync(source);
        var content = new GoCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("func junexis_send[T any](target any, value T)", content);
        Assert.Contains("case *Queue[T]:", content);
        Assert.Contains("t.Enqueue(value)", content);
        Assert.Contains("junexis_send(outbox,", content);
        Assert.DoesNotContain("outbox <- int64(42)", content);
    }

    [Fact]
    public async Task TypeScript_ExplicitSendTarget_HelperSupportsQueueAndStream()
    {
        const string source = """
            task Forward(outbox: queue<Int>, live: stream<Int>) @report {
                send 42 to outbox;
                send 43 to live;
            }
            """;

        var ir = await TransformToIrAsync(source);
        var runtime = new TypeScriptCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "report/runtime.ts")
            .Content;
        var index = new TypeScriptCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("enqueue?(value: T): unknown;", runtime);
        Assert.Contains("publish?(value: T): unknown;", runtime);
        Assert.Contains("if (target.enqueue) { await target.enqueue(value); return; }", runtime);
        Assert.Contains("if (target.publish) { await target.publish(value); return; }", runtime);
        Assert.Contains("await junexis.send(outbox, 42);", index);
        Assert.Contains("await junexis.send(live, 43);", index);
    }
}
