namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_OutputTask_WithQueueOutput_EmitsInitializationHandle()
    {
        const string source = """
            task Publish() @server -> queue<Int> {
                log("tick")
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("result := NewQueue[int64]()", content);
        Assert.DoesNotContain("\t\"io\"", content);
        Assert.DoesNotContain("func registerQueueBridgeDataEndpoint[T any](path string, queue *Queue[T]) {", content);
        Assert.DoesNotContain("func registerQueueBridgeAckEndpoint[T any](path string, queue *Queue[T]) {", content);
        Assert.DoesNotContain("func startQueueBridgeServer() {", content);
        Assert.DoesNotContain("registerQueueBridgeDataEndpoint(\"/queue/Publish\", result)", content);
        Assert.DoesNotContain("registerQueueBridgeAckEndpoint(\"/queue/Publish/ack\", result)", content);
        Assert.DoesNotContain("startQueueBridgeServer()", content);
        Assert.DoesNotContain("// TODO: initialize queue output handle before enqueueing into result", content);
    }

    [Fact]
    public async Task Go_OutputTask_WithStreamOutput_EmitsInitializationSkeleton()
    {
        const string source = """
            task Publish() @server -> stream<Int> {
                log("tick")
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("result := NewStream[int64]()", content);
        Assert.DoesNotContain("// TODO: initialize stream output handle before publishing into result", content);
    }
}

