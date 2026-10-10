namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Testy bridge generation dla transportu chan.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void TS_SingleChanConsumer_UsesGeneratedBridgeTemplate_WhenDescriptorExists()
    {
        var gen = new TypeScriptCodeGenerator();
        var sourceTask = ServerTask("Publish", outputType: new IRCommType(IRCommKind.Chan, new IRNamedType("Text")));
        var targetTask = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)]);
        var files = gen.Generate(DocWith(
                [sourceTask, targetTask],
                chanBridges:
                [
                    new IRChanBridgeDescriptor("Main", "Publish", "live", "Consume", "input", "/chan/Publish/live")
                ]), Opts);
        var index = files.Single(f => f.FilePath == "report/index.ts");
        var bootstrap = files.Single(f => f.FilePath == "report/deploy.ts");
        var package = files.Single(f => f.FilePath == "report/package.json");

        Assert.Contains("const __inputChanBridgeUrl = `${junexisServerWsUrl}/chan/Publish/live/ws`;", index.Content);
        Assert.Contains("void bindChanBridgeConsumer<string>(__inputChanBridgeUrl, async payload => {", index.Content);
        Assert.Contains("consumeConsumeItem(payload);", index.Content);
        Assert.Contains("} // Auto-generated consumer template for single chan input", index.Content);
        Assert.DoesNotContain("}} // Auto-generated consumer template for single chan input", index.Content);
        Assert.Contains("report bridge requires a deployed server runtime", bootstrap.Content);
        Assert.Contains("\"@msgpack/msgpack\": \"^3.0.0\"", package.Content);
    }

    [Fact]
    public void Go_ChanNetworkingBridge_EmitsDataHelpers()
    {
        var gen = new GoCodeGenerator();
        var sourceTask = ServerTask(
            "Publish",
            outputType: new IRCommType(IRCommKind.Chan, new IRNamedType("Text")),
            body: [new IRSendStmt(new IRLiteralExpr(new IRTextValue("hello")), null)]);
        var targetTask = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)]);
        var content = gen.Generate(DocWith(
                [sourceTask, targetTask],
                pipelines:
                [
                    new IRPipeline("Main", [new IRUseConn("Publish", "live", "Consume", "input")])
                ],
                chanBridges:
                [
                    new IRChanBridgeDescriptor("Main", "Publish", "live", "Consume", "input", "/chan/Publish/live")
                ]), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("type ChanBridgeEnvelope[T any] struct {", content);
        Assert.Contains("func encodeChanBridgeEnvelope[T any](payload T) ([]byte, error) {", content);
        Assert.Contains("func registerChanBridgeWebSocketEndpoint[T any](path string, channel <-chan T) {", content);
        Assert.Contains("registerChanBridgeWebSocketEndpoint(\"/chan/Publish/live\", result)", content);
        Assert.Contains("result <- \"hello\"", content);
        Assert.Contains("startNetworkBridgeServer(*httpPort)", content);
    }

    [Fact]
    public async Task ServerAndReport_ChanBridgePair_UsesPipelineDerivedDescriptor_EndToEnd()
    {
        const string source = """
            task Publish() @server -> chan<Text> {
                send "hello";
            }

            task Consume(input: chan<Text>) @report {
            }

            pipeline Main {
                use Publish.outbox -> Consume.input
            }
            """;

        var (ir, goContent, tsContent) = await GenerateServerAndReportAsync(source);
        AssertChanBridgeDescriptor(ir, "Publish", "outbox", "Consume", "input", "/chan/Publish/outbox");

        Assert.Contains("registerChanBridgeWebSocketEndpoint(\"/chan/Publish/outbox\", result)", goContent);
        Assert.Contains("startNetworkBridgeServer(*httpPort)", goContent);
        Assert.Contains("const __inputChanBridgeUrl = `${junexisServerWsUrl}/chan/Publish/outbox/ws`;", tsContent);
        Assert.Contains("void bindChanBridgeConsumer<string>(__inputChanBridgeUrl, async payload => {", tsContent);
        Assert.Contains("function consumeConsumeItem(item: string): void {", tsContent);
    }
}
