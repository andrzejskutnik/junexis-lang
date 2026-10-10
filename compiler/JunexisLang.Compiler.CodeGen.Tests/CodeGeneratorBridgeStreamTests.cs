namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Testy bridge generation dla transportu stream.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task ServerAndReport_StreamBridgePair_UsesPipelineDerivedDescriptor_EndToEnd()
    {
        const string source = """
            task Publish() @server -> stream<Text> {
                send "hello";
            }

            task Consume(input: stream<Text>) @report {
            }

            pipeline Main {
                use Publish.live -> Consume.input
            }
            """;

        var (ir, goContent, tsContent) = await GenerateServerAndReportAsync(source);
        AssertStreamBridgeDescriptor(ir, "Publish", "live", "Consume", "input", "/stream/Publish/live");

        Assert.Contains("registerStreamBridgeDataEndpoint(\"/stream/Publish/live\", func() (string, bool) { return __latestResult, __hasLatestResult })", goContent);
        Assert.Contains("startNetworkBridgeServer(*httpPort)", goContent);
        Assert.Contains("const __inputStreamBridgeUrl = `${junexisServerUrl}/stream/Publish/live`;", tsContent);
        Assert.Contains("void bindStreamBridgeConsumer<string>(__inputStreamBridgeUrl, async payload => {", tsContent);
        Assert.Contains("function consumeConsumeItem(item: string): void {", tsContent);
    }

    [Fact]
    public void TS_SingleStreamConsumer_UsesGeneratedSnapshotBridgeTemplate()
    {
        var gen = new TypeScriptCodeGenerator();
        var sourceTask = ServerTask("Publish", outputType: new IRCommType(IRCommKind.Stream, new IRNamedType("Text")));
        var targetTask = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Stream, new IRNamedType("Text")), null)]);
        var index = gen.Generate(DocWith(
                [sourceTask, targetTask],
                streamBridges:
                [
                    new IRStreamBridgeDescriptor("Main", "Publish", "live", "Consume", "input", "/stream/Publish/live")
                ]), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("export async function Consume(input: Stream<string>): Promise<void>", index.Content);
        Assert.Contains("const __inputStreamBridgeUrl = `${junexisServerUrl}/stream/Publish/live`;", index.Content);
        Assert.Contains("void bindStreamBridgeConsumer<string>(__inputStreamBridgeUrl, async payload => {", index.Content);
        Assert.Contains("consumeConsumeItem(payload);", index.Content);
        Assert.Contains("function consumeConsumeItem(item: string): void {", index.Content);
    }

    [Fact]
    public void Go_StreamNetworkingBridge_EmitsSnapshotHelpers()
    {
        var gen = new GoCodeGenerator();
        var sourceTask = ServerTask(
            "Publish",
            outputType: new IRCommType(IRCommKind.Stream, new IRNamedType("Text")),
            body: [new IRSendStmt(new IRLiteralExpr(new IRTextValue("hello")), null)]);
        var targetTask = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Stream, new IRNamedType("Text")), null)]);
        var content = gen.Generate(DocWith(
                [sourceTask, targetTask],
                pipelines:
                [
                    new IRPipeline("Main", [new IRUseConn("Publish", "live", "Consume", "input")])
                ],
                streamBridges:
                [
                    new IRStreamBridgeDescriptor("Main", "Publish", "live", "Consume", "input", "/stream/Publish/live")
                ]), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("type StreamBridgeEnvelope[T any] struct {", content);
        Assert.Contains("func encodeStreamBridgeEnvelope[T any](payload T) ([]byte, error) {", content);
        Assert.Contains("func registerStreamBridgeDataEndpoint[T any](path string, latest func() (T, bool)) {", content);
        Assert.Contains("registerStreamBridgeDataEndpoint(\"/stream/Publish/live\", func() (string, bool) { return __latestResult, __hasLatestResult })", content);
        Assert.Contains("publishResult := func(value string) {", content);
        Assert.Contains("publishResult(\"hello\")", content);
        Assert.Contains("startNetworkBridgeServer(*httpPort)", content);
    }

    [Fact]
    public void TS_StreamNetworkingBridge_EmitsSnapshotHelpers()
    {
        var gen = new TypeScriptCodeGenerator();
        var sourceTask = ServerTask("Publish", outputType: new IRCommType(IRCommKind.Stream, new IRNamedType("Text")));
        var targetTask = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Stream, new IRNamedType("Text")), null)]);
        var content = gen.Generate(DocWith(
                [sourceTask, targetTask],
                streamBridges:
                [
                    new IRStreamBridgeDescriptor("Main", "Publish", "live", "Consume", "input", "/stream/Publish/live")
                ]), Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("type StreamBridgeEnvelope<T> = {", content);
        Assert.Contains("function parseStreamBridgeEnvelope<T>(data: string): StreamBridgeEnvelope<T> {", content);
        Assert.Contains("async function pollStreamBridge<T>(url: string): Promise<StreamBridgeEnvelope<T> | undefined> {", content);
        Assert.Contains("async function bindStreamBridgeConsumer<T>(pollUrl: string, handlePayload: (payload: T) => Promise<void>): Promise<void> {", content);
        Assert.Contains("const envelope = await pollStreamBridge<T>(pollUrl);", content);
    }
}
