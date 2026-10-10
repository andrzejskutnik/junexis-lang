namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy kontraktów runtime dla generatora TypeScript.
/// </summary>
public sealed class TypeScriptRuntimeContractTests
{
    [Fact]
    public void TypeScriptChanContract_ExposesBoundedAsyncChannelShape()
    {
        var binding = ChanRuntimeContracts.ForTypeScript(new IRNamedType("Bool"));

        Assert.Equal("Channel<boolean>", binding.HandleType);
        Assert.Equal("new Channel<boolean>(1)", binding.FactoryExpression);
        Assert.False(binding.RequiresBufferedOutputAdapter);
        Assert.True(binding.Supports(ChanRuntimeCapabilities.Send));
        Assert.True(binding.Supports(ChanRuntimeCapabilities.Consume));
        Assert.Equal(CommunicationDeliverySemantics.Backpressured, binding.Contract.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerWaits, binding.Contract.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.UntilConsumed, binding.Contract.Retention);
        Assert.Equal(CommunicationOrderingSemantics.Fifo, binding.Contract.Ordering);
        Assert.Equal("channel.send(value)", binding.RenderSendInvocation("channel", "value"));
    }

    [Fact]
    public void TypeScriptStreamContract_ExposesSharedPublishShape()
    {
        var binding = StreamRuntimeContracts.ForTypeScript(new IRNamedType("Text"));

        Assert.Equal("Stream<string>", binding.HandleType);
        Assert.Null(binding.FactoryExpression);
        Assert.Equal("publish", binding.Contract.PublishMethodName);
        Assert.Equal("subscribe", binding.Contract.SubscribeMethodName);
        Assert.True(binding.Supports(StreamRuntimeCapabilities.Publish));
        Assert.True(binding.Supports(StreamRuntimeCapabilities.Consume));
        Assert.True(binding.Supports(StreamRuntimeCapabilities.Subscribe));
        Assert.Equal(CommunicationDeliverySemantics.LatestWins, binding.Contract.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerContinues, binding.Contract.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.LatestOnly, binding.Contract.Retention);
        Assert.Equal(CommunicationOrderingSemantics.LatestOnly, binding.Contract.Ordering);
        Assert.Equal("result.publish(value)", binding.RenderPublishInvocation("result", "value"));
        Assert.Equal("result.subscribe()", binding.RenderSubscribeInvocation("result"));
    }

    [Fact]
    public void TypeScriptQueueContract_ExposesProducerOnlyQueueShape()
    {
        var contract = QueueRuntimeContracts.ForTypeScript(new IRNamedType("Text"));

        Assert.Equal("Queue<string>", contract.HandleType);
        Assert.Null(contract.FactoryExpression);
        Assert.Equal("enqueue", contract.Contract.EnqueueMethodName);
        Assert.Equal("recv", contract.Contract.ReceiveMethodName);
        Assert.Equal("enqueue", contract.EnqueueMethodName);
        Assert.Equal("recv", contract.ReceiveMethodName);
        Assert.True(contract.ReceiveReturnsMessageId);
        Assert.Equal("ack", contract.AckMethodName);
        Assert.Equal("nack", contract.NackMethodName);
        Assert.True(contract.Supports(QueueRuntimeCapabilities.ProducerEnqueue));
        Assert.True(contract.Supports(QueueRuntimeCapabilities.ConsumerReceive));
        Assert.True(contract.Supports(QueueRuntimeCapabilities.Ack));
        Assert.True(contract.Supports(QueueRuntimeCapabilities.Nack));
        Assert.Equal(CommunicationDeliverySemantics.AtLeastOnce, contract.Contract.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerContinues, contract.Contract.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.UntilAck, contract.Contract.Retention);
        Assert.Equal(CommunicationOrderingSemantics.Fifo, contract.Contract.Ordering);
        Assert.Equal("result.enqueue(value)", contract.RenderEnqueueInvocation("result", "value"));
        Assert.Equal("input.recv()", contract.RenderReceiveInvocation("input"));
        Assert.Equal("input.ack(messageId)", contract.RenderAckInvocation("input", "messageId"));
        Assert.Equal("input.nack(messageId, reason)", contract.RenderNackInvocation("input", "messageId", "reason"));
    }
}
