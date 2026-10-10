namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Testy szablonow generowanych przez TypeScriptCodeGenerator.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: szablony generowane ───────────────────────

    [Fact]
    public async Task TS_EmptyChanProducer_UsesGeneratedProducerTemplate()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask("Publish", outputType: new IRCommType(IRCommKind.Chan, new IRNamedType("Int")));
        var index = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("export async function Publish(): Promise<Channel<number>>", index.Content);
        Assert.Contains("const result: Channel<number> = new Channel<number>(1);", index.Content);
        Assert.Contains("await new Promise(resolve => setTimeout(resolve, 1000));", index.Content);
        Assert.Contains("await result.send(emitPublishValue());", index.Content);
        Assert.Contains("function emitPublishValue(): number {", index.Content);
        Assert.Contains("void PublishPlatformProfile.supportsThreading;", index.Content);
        Assert.Contains("void PublishPlatformProfile.supportsNetworking;", index.Content);
        Assert.Contains("return 0;", index.Content);
        Assert.Contains("Auto-generated producer template for empty chan task", index.Content);
    }

    [Fact]
    public void TS_SingleChanConsumer_UsesGeneratedConsumerTemplate()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)]);
        var index = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("export async function Consume(input: Channel<string>): Promise<void>", index.Content);
        Assert.Contains("for await (const item of input) {", index.Content);
        Assert.Contains("consumeConsumeItem(item);", index.Content);
        Assert.Contains("function consumeConsumeItem(item: string): void {", index.Content);
        Assert.Contains("void ConsumePlatformProfile.supportsThreading;", index.Content);
        Assert.Contains("void ConsumePlatformProfile.supportsNetworking;", index.Content);
        Assert.Contains("console.log(item);", index.Content);
        Assert.Contains("Auto-generated consumer template for single chan input", index.Content);
    }

    [Fact]
    public void TS_SingleQueueConsumer_UsesGeneratedConsumerTemplate()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Queue, new IRNamedType("Text")), null)]);
        var index = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("export async function Consume(input: Queue<string>): Promise<void>", index.Content);
        Assert.Contains("const __inputBridgeUrl = `${junexisServerUrl}/queue/Consume`;", index.Content);
        Assert.Contains("const __inputBridgeAckUrl = `${junexisServerUrl}/queue/Consume/ack`;", index.Content);
        Assert.Contains("void bindQueueBridgeConsumer<string>(__inputBridgeUrl, __inputBridgeAckUrl, async envelope => {", index.Content);
        Assert.Contains("consumeConsumeItem(envelope.payload);", index.Content);
        Assert.Contains("const [recvResult, messageId] = await input.recv();", index.Content);
        Assert.Contains("if (!recvResult.ok || messageId === undefined) {", index.Content);
        Assert.Contains("consumeConsumeItem(recvResult.value);", index.Content);
        Assert.Contains("await input.ack(messageId);", index.Content);
        Assert.Contains("// Runtime queue policy decides retry vs DLQ after nack.", index.Content);
        Assert.Contains("await input.nack(messageId, error instanceof Error ? error.message : 'consume failed');", index.Content);
        Assert.Contains("function consumeConsumeItem(item: string): void {", index.Content);
        Assert.Contains("Auto-generated consumer template for single queue input", index.Content);
    }

    [Fact]
    public void TS_QueueAckNackStatements_LowerThroughHiddenMessageId()
    {
        var gen = new TypeScriptCodeGenerator();
        var queueType = new IRCommType(IRCommKind.Queue, new IRNamedType("Text"));
        var task = ReportTask(
            "Handle",
            @params: [new IRParam("input", queueType, null)],
            body:
            [
                new IRLetStmt("let", "item", null, new IRRecvExpr(new IRIdentExpr("input"))),
                new IRNackStmt(new IRIdentExpr("item"), new IRLiteralExpr(new IRTextValue("retry"))),
                new IRAckStmt(new IRIdentExpr("item"))
            ]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("const __itemFrame = await input.recv();", content);
        Assert.Contains("if (__itemFrame === undefined) { return; }", content);
        Assert.Contains("let item = __itemFrame.value;", content);
        Assert.Contains("input.nack(__itemFrame.messageId, 'retry');", content);
        Assert.Contains("input.ack(__itemFrame.messageId);", content);
    }

    [Fact]
    public void TS_LatestPrimitive_LowersToStreamLatestMethod()
    {
        var gen = new TypeScriptCodeGenerator();
        var streamType = new IRCommType(IRCommKind.Stream, new IRNamedType("Float"));
        var task = ReportTask(
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
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("let current = metrics.latest()!;", content);
    }

    [Fact]
    public void TS_BatchPrimitive_LowersToQueueBatchMethod()
    {
        var gen = new TypeScriptCodeGenerator();
        var queueType = new IRCommType(IRCommKind.Queue, new IRNamedType("Text"));
        var task = ReportTask(
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

        var files = gen.Generate(DocWith(task), Opts);
        var content = files.Single(file => file.FilePath == "report/index.ts").Content;
        var runtime = files.Single(file => file.FilePath == "report/runtime.ts").Content;

        Assert.Contains("let items = jobs.batch(10);", content);
        Assert.Contains("batch(limit: number): T[]", runtime);
    }

    [Fact]
    public void TS_ChanOutput_UsesSharedChanContract()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "Publish",
            outputType: new IRCommType(IRCommKind.Chan, new IRNamedType("Int")),
            body: [new IRSendStmt(new IRLiteralExpr(new IRIntValue(42L)), null)]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("const result: Channel<number> = new Channel<number>(1);", content);
        Assert.Contains("await result.send(42);", content);
    }
}
