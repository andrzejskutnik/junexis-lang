namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy szablonow generowanych przez GoCodeGenerator dla pustych lub prostych taskow.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── GoCodeGenerator: generated templates ──────────────────────────────

    [Fact]
    public async Task Go_EmptyChanProducer_UsesGeneratedProducerTemplate()
    {
        const string source = """
            task Publish() @server -> chan<Int> {
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("\t\"time\"", content);
        Assert.Contains("result := make(chan int64)", content);
        Assert.Contains("go func() {", content);
        Assert.Contains("ticker := time.NewTicker(time.Second)", content);
        Assert.Contains("result <- emitPublishValue()", content);
        Assert.Contains("func emitPublishValue() int64 {", content);
        Assert.Contains("_ = PublishSupportsThreading", content);
        Assert.Contains("_ = PublishSupportsNetworking", content);
        Assert.Contains("return int64(0)", content);
        Assert.Contains("Auto-generated producer template for empty chan task", content);
    }

    [Fact]
    public async Task Go_SingleChanConsumer_UsesGeneratedConsumerTemplate()
    {
        const string source = """
            task Consume(input: chan<Text>) @server {
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("func Consume(input chan string)", content);
        Assert.Contains("for item := range input {", content);
        Assert.Contains("consumeConsumeItem(item)", content);
        Assert.Contains("func consumeConsumeItem(item string) {", content);
        Assert.Contains("_ = ConsumeSupportsThreading", content);
        Assert.Contains("_ = ConsumeSupportsNetworking", content);
        Assert.Contains("log.Println(item)", content);
        Assert.Contains("Auto-generated consumer template for single chan input", content);
    }

    [Fact]
    public async Task Go_SingleQueueConsumer_UsesGeneratedConsumerTemplate()
    {
        const string source = """
            task Consume(input: queue<Text>) @server {
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("func Consume(input *Queue[string])", content);
        Assert.Contains("recvResult, messageId := input.Recv()", content);
        Assert.Contains("if !recvResult.Ok || messageId == nil {", content);
        Assert.Contains("if err := consumeConsumeItem(recvResult.Value); err != nil {", content);
        Assert.Contains("// Runtime queue policy decides retry vs DLQ after Nack.", content);
        Assert.Contains("input.Nack(*messageId, err.Error())", content);
        Assert.Contains("input.Ack(*messageId)", content);
        Assert.Contains("func consumeConsumeItem(item string) error {", content);
        Assert.Contains("return nil", content);
        Assert.Contains("Auto-generated consumer template for single queue input", content);
    }

    [Fact]
    public void Go_QueueAckNackStatements_LowerThroughHiddenMessageId()
    {
        var gen = new GoCodeGenerator();
        var queueType = new IRCommType(IRCommKind.Queue, new IRNamedType("Text"));
        var task = ServerTask(
            "Handle",
            @params: [new IRParam("input", queueType, null)],
            body:
            [
                new IRLetStmt("let", "item", null, new IRRecvExpr(new IRIdentExpr("input"))),
                new IRNackStmt(new IRIdentExpr("item"), new IRLiteralExpr(new IRTextValue("retry"))),
                new IRAckStmt(new IRIdentExpr("item"))
            ]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("__itemRecv, __itemMsgId := input.Recv()", content);
        Assert.Contains("if !__itemRecv.Ok || __itemMsgId == nil {", content);
        Assert.Contains("item := __itemRecv.Value", content);
        Assert.Contains("input.Nack(*__itemMsgId, \"retry\")", content);
        Assert.Contains("input.Ack(*__itemMsgId)", content);
    }

    [Fact]
    public void Go_LatestPrimitive_LowersToStreamLatestMethod()
    {
        var gen = new GoCodeGenerator();
        var streamType = new IRCommType(IRCommKind.Stream, new IRNamedType("Float"));
        var task = ServerTask(
            "Dashboard",
            @params: [new IRParam("metrics", streamType, null)],
            body:
            [
                new IRLetStmt(
                    "let",
                    "current",
                    null,
                    new IRCallExpr(
                        new IRIdentExpr("latest"),
                        [new IRCallArg(null, new IRIdentExpr("metrics"))]))
            ]);

        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("current := metrics.Latest()", content);
    }

    [Fact]
    public void Go_BatchPrimitive_LowersToQueueBatchMethod()
    {
        var gen = new GoCodeGenerator();
        var queueType = new IRCommType(IRCommKind.Queue, new IRNamedType("Text"));
        var task = ServerTask(
            "Worker",
            @params: [new IRParam("jobs", queueType, null)],
            body:
            [
                new IRLetStmt(
                    "let",
                    "items",
                    null,
                    new IRCallExpr(
                        new IRIdentExpr("batch"),
                        [
                            new IRCallArg(null, new IRIdentExpr("jobs")),
                            new IRCallArg(null, new IRLiteralExpr(new IRIntValue(10)))
                        ]))
            ]);

        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("items := jobs.Batch(10)", content);
        Assert.Contains("func (q *Queue[T]) Batch(limit int64) []T", content);
    }
}
