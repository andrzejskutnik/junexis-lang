namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Testy mostow kolejkowych oraz routingu deskryptorow miedzy Go i TypeScript.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task ServerAndReport_QueueBridgePair_EmitCompatibleTransportHooks()
    {
        const string source = """
            task Publish() @server -> queue<Int> {
                log("tick")
            }

            task Consume(input: queue<Int>) @report {
            }

            pipeline Main {
                use Publish -> Consume
            }
            """;

        var (ir, goContent, tsContent) = await GenerateServerAndReportAsync(source);

        Assert.Contains("registerQueueBridgeDataEndpoint(\"/queue/Publish\", result)", goContent);
        Assert.Contains("registerQueueBridgeAckEndpoint(\"/queue/Publish/ack\", result)", goContent);
        Assert.Contains("startNetworkBridgeServer(*httpPort)", goContent);
        Assert.Contains("const __inputBridgeUrl = `${junexisServerUrl}/queue/Publish`;", tsContent);
        Assert.Contains("const __inputBridgeAckUrl = `${junexisServerUrl}/queue/Publish/ack`;", tsContent);
        Assert.Contains("async function bindQueueBridgeConsumer<T>(pollUrl: string, ackUrl: string, handleEnvelope: (envelope: QueueBridgeEnvelope<T>) => Promise<void>): Promise<void> {", tsContent);
        Assert.Contains("await sendQueueBridgeAck(ackUrl, envelope.messageId);", tsContent);
        Assert.Contains("await sendQueueBridgeNack(ackUrl, envelope.messageId, error instanceof Error ? error.message : 'bridge handler failed');", tsContent);
    }

    [Fact]
    public async Task ServerAndReport_QueueBridgePair_UsesPipelineDerivedDescriptor_EndToEnd()
    {
        const string source = """
            task Publish() @server -> queue<Int> {
                log("tick")
            }

            task Consume(input: queue<Int>) @report {
            }

            pipeline Main {
                use Publish.outbox -> Consume.input
            }
            """;

        var (ir, goContent, tsContent) = await GenerateServerAndReportAsync(source);
        AssertQueueBridgeDescriptor(ir, "Publish", "outbox", "Consume", "input", "/queue/Publish/outbox", "/queue/Publish/outbox/ack");

        Assert.Contains("registerQueueBridgeDataEndpoint(\"/queue/Publish/outbox\", result)", goContent);
        Assert.Contains("registerQueueBridgeAckEndpoint(\"/queue/Publish/outbox/ack\", result)", goContent);
        Assert.Contains("startNetworkBridgeServer(*httpPort)", goContent);
        Assert.Contains("const __inputBridgeUrl = `${junexisServerUrl}/queue/Publish/outbox`;", tsContent);
        Assert.Contains("const __inputBridgeAckUrl = `${junexisServerUrl}/queue/Publish/outbox/ack`;", tsContent);
        Assert.Contains("void bindQueueBridgeConsumer<number>(__inputBridgeUrl, __inputBridgeAckUrl, async envelope => {", tsContent);
    }

    [Fact]
    public async Task ServerAndReport_QueueBridgePair_UsesPipelineDerivedDescriptor_WhenMultipleQueueSourcesExist()
    {
        const string source = """
            task PublishOrders() @server -> queue<Int> {
                log("orders")
            }

            task PublishAudit() @server -> queue<Int> {
                log("audit")
            }

            task Consume(input: queue<Int>) @report {
            }

            pipeline Main {
                use PublishAudit -> Consume
            }
            """;

        var ir = await TransformToIrAsync(source);
        var tsContent = new TypeScriptCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("const __inputBridgeUrl = `${junexisServerUrl}/queue/PublishAudit`;", tsContent);
        Assert.Contains("const __inputBridgeAckUrl = `${junexisServerUrl}/queue/PublishAudit/ack`;", tsContent);
        Assert.DoesNotContain("http://localhost:8080/queue/Consume", tsContent);
        Assert.DoesNotContain("http://localhost:8080/queue/PublishOrders", tsContent);
    }

    [Fact]
    public void TS_SingleQueueConsumer_PrefersDescriptorMatchingTargetPort_AndUsesSourcePortRoute()
    {
        var gen = new TypeScriptCodeGenerator();
        var sourceTask = ServerTask("Publish", outputType: new IRCommType(IRCommKind.Queue, new IRNamedType("Text")));
        var targetTask = ReportTask("Consume", @params: [new IRParam("input", new IRCommType(IRCommKind.Queue, new IRNamedType("Text")), null)]);
        var index = gen.Generate(DocWith(
                [sourceTask, targetTask],
                queueBridges:
                [
                    new IRQueueBridgeDescriptor("Main", "Publish", "ignored", "Consume", "other", "/queue/Publish/ignored", "/queue/Publish/ignored/ack"),
                    new IRQueueBridgeDescriptor("Main", "Publish", "outbox", "Consume", "input", "/queue/Publish/outbox", "/queue/Publish/outbox/ack")
                ]), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("const __inputBridgeUrl = `${junexisServerUrl}/queue/Publish/outbox`;", index.Content);
        Assert.Contains("const __inputBridgeAckUrl = `${junexisServerUrl}/queue/Publish/outbox/ack`;", index.Content);
        Assert.DoesNotContain("http://localhost:8080/queue/Publish/ignored", index.Content);
    }

    [Fact]
    public async Task Go_QueueBridgeRegistration_UsesPipelineDerivedDescriptor_WhenMultipleQueueSourcesExist()
    {
        const string source = """
            task PublishOrders() @server -> queue<Int> {
                log("orders")
            }

            task PublishAudit() @server -> queue<Int> {
                log("audit")
            }

            task Consume(input: queue<Int>) @report {
            }

            pipeline Main {
                use PublishAudit.outbox -> Consume.input
            }
            """;

        var ir = await TransformToIrAsync(source);
        AssertQueueBridgeDescriptor(ir, "PublishAudit", "outbox", "Consume", "input", "/queue/PublishAudit/outbox", "/queue/PublishAudit/outbox/ack");

        var goContent = new GoCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("registerQueueBridgeDataEndpoint(\"/queue/PublishAudit/outbox\", result)", goContent);
        Assert.Contains("registerQueueBridgeAckEndpoint(\"/queue/PublishAudit/outbox/ack\", result)", goContent);
        Assert.DoesNotContain("registerQueueBridgeDataEndpoint(\"/queue/PublishOrders\", result)", goContent);
        Assert.DoesNotContain("registerQueueBridgeAckEndpoint(\"/queue/PublishOrders/ack\", result)", goContent);
        Assert.Contains("startNetworkBridgeServer(*httpPort)", goContent);
    }

}
