namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy helperow sieciowych generowanych dla mostow kolejkowych w Go i TypeScript.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Go_QueueNetworkingBridge_EmitsAckFrameHelpers()
    {
        var gen = new GoCodeGenerator();
        var sourceTask = ServerTask("Publish", outputType: new IRCommType(IRCommKind.Queue, new IRNamedType("Text")));
        var targetTask = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Queue, new IRNamedType("Text")), null)]);
        var content = gen.Generate(DocWith(
                [sourceTask, targetTask],
                pipelines:
                [
                    new IRPipeline("Main", [new IRUseConn("Publish", "outbox", "Consume", "input")])
                ],
                queueBridges:
                [
                    new IRQueueBridgeDescriptor("Main", "Publish", "outbox", "Consume", "input", "/queue/Publish/outbox", "/queue/Publish/outbox/ack")
                ]), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("\"encoding/json\"", content);
        Assert.Contains("\t\"io\"", content);
        Assert.Contains("type QueueBridgeEnvelope[T any] struct {", content);
        Assert.Contains("type QueueAckFrame struct {", content);
        Assert.Contains("func parseQueueAckFrame(data []byte) (QueueAckFrame, error) {", content);
        Assert.Contains("func encodeQueueBridgeEnvelope[T any](messageId string, payload T) ([]byte, error) {", content);
        Assert.Contains("func applyQueueAckFrame[T any](queue *Queue[T], frame QueueAckFrame) {", content);
        Assert.Contains("func registerQueueBridgeDataEndpoint[T any](path string, queue *Queue[T]) {", content);
        Assert.Contains("func registerQueueBridgeAckEndpoint[T any](path string, queue *Queue[T]) {", content);
        Assert.Contains("queue.Nack(frame.MessageId, frame.Reason)", content);
        Assert.Contains("queue.Ack(frame.MessageId)", content);
        Assert.Contains("err := json.Unmarshal(data, &frame)", content);
    }

    [Fact]
    public void TS_QueueNetworkingBridge_EmitsAckFrameHelpers()
    {
        var gen = new TypeScriptCodeGenerator();
        var sourceTask = ServerTask("Publish", outputType: new IRCommType(IRCommKind.Queue, new IRNamedType("Text")));
        var targetTask = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Queue, new IRNamedType("Text")), null)]);
        var content = gen.Generate(DocWith(
                [sourceTask, targetTask],
                queueBridges:
                [
                    new IRQueueBridgeDescriptor("Main", "Publish", "outbox", "Consume", "input", "/queue/Publish/outbox", "/queue/Publish/outbox/ack")
                ]), Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("type QueueBridgeEnvelope<T> = {", content);
        Assert.Contains("type QueueAckFrame = {", content);
        Assert.Contains("function createQueueAckFrame(messageId: string): QueueAckFrame {", content);
        Assert.Contains("function createQueueNackFrame(messageId: string, reason: string): QueueAckFrame {", content);
        Assert.Contains("function parseQueueBridgeEnvelope<T>(data: string): QueueBridgeEnvelope<T> {", content);
        Assert.Contains("async function postQueueBridgeFrame(url: string, frame: QueueAckFrame): Promise<void> {", content);
        Assert.Contains("async function pollQueueBridge<T>(url: string): Promise<QueueBridgeEnvelope<T> | undefined> {", content);
        Assert.Contains("async function bindQueueBridgeConsumer<T>(pollUrl: string, ackUrl: string, handleEnvelope: (envelope: QueueBridgeEnvelope<T>) => Promise<void>): Promise<void> {", content);
        Assert.Contains("const envelope = await pollQueueBridge<T>(pollUrl);", content);
        Assert.Contains("await sendQueueBridgeAck(ackUrl, envelope.messageId);", content);
        Assert.Contains("await sendQueueBridgeNack(ackUrl, envelope.messageId, error instanceof Error ? error.message : 'bridge handler failed');", content);
        Assert.Contains("async function sendQueueBridgeAck(url: string, messageId: string): Promise<void> {", content);
        Assert.Contains("async function sendQueueBridgeNack(url: string, messageId: string, reason: string): Promise<void> {", content);
    }
}
