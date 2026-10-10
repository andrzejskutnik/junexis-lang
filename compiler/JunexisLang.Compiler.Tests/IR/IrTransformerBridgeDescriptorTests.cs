using JunexisLang.Compiler.Tests.TestHelpers;

namespace JunexisLang.Compiler.Tests.IR;

/// <summary>Testy transformacji deskryptorow bridge w pipeline do IR.</summary>
public sealed partial class IrTransformerTests
{
    [Fact]
    public async Task Pipeline_ServerQueueToReportConsumer_TransformsToExplicitQueueBridgeDescriptor()
    {
        var ir = await TransformAsync("""
            task Publish() @server -> queue<Int> { }
            task Consume(input: queue<Int>) @report { }

            pipeline Main {
                use Publish -> Consume
            }
            """);

        BridgeDescriptorAssertions.AssertQueueBridge(
            ir,
            "Main",
            "Publish",
            null,
            "Consume",
            null,
            "/queue/Publish",
            "/queue/Publish/ack");
    }

    [Fact]
    public async Task Pipeline_ServerQueueToReportConsumer_WithPorts_TransformsQueueBridgeDescriptorPorts()
    {
        var ir = await TransformAsync("""
            task Publish() @server -> queue<Int> { }
            task Consume(input: queue<Int>) @report { }

            pipeline Main {
                use Publish.outbox -> Consume.input
            }
            """);

        BridgeDescriptorAssertions.AssertQueueBridge(
            ir,
            "Main",
            "Publish",
            "outbox",
            "Consume",
            "input",
            "/queue/Publish/outbox",
            "/queue/Publish/outbox/ack");
    }

    [Fact]
    public async Task Pipeline_ServerStreamToReportConsumer_WithPorts_TransformsToExplicitStreamBridgeDescriptor()
    {
        var ir = await TransformAsync("""
            task Publish() @server -> stream<Int> { }
            task Consume(input: stream<Int>) @report { }

            pipeline Main {
                use Publish.live -> Consume.input
            }
            """);

        BridgeDescriptorAssertions.AssertStreamBridge(
            ir,
            "Main",
            "Publish",
            "live",
            "Consume",
            "input",
            "/stream/Publish/live");
    }

    [Fact]
    public async Task Pipeline_ServerChanToReportConsumer_WithPorts_TransformsToExplicitChanBridgeDescriptor()
    {
        var ir = await TransformAsync("""
            task Publish() @server -> chan<Int> { }
            task Consume(input: chan<Int>) @report { }

            pipeline Main {
                use Publish.live -> Consume.input
            }
            """);

        BridgeDescriptorAssertions.AssertChanBridge(
            ir,
            "Main",
            "Publish",
            "live",
            "Consume",
            "input",
            "/chan/Publish/live");
    }
}