namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy kontraktów runtime dla generatora C++.
/// </summary>
public sealed class CppRuntimeContractTests
{
    [Fact]
    public void CppChanContract_ExposesSharedSendShape()
    {
        var binding = ChanRuntimeContracts.ForCpp(new IRNamedType("Text"));

        Assert.Equal("junexis::Channel<std::string>", binding.HandleType);
        Assert.Null(binding.FactoryExpression);
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
    public void CppStreamContract_ExposesSharedPublishShape()
    {
        var binding = StreamRuntimeContracts.ForCpp(new IRNamedType("Int"));

        Assert.Equal("junexis::Stream<int32_t>", binding.HandleType);
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
        Assert.Equal("stream.publish(value)", binding.RenderPublishInvocation("stream", "value"));
        Assert.Equal("stream.subscribe()", binding.RenderSubscribeInvocation("stream"));
    }

    [Fact]
    public void CppQueueContract_ExposesSharedQueueShape()
    {
        var binding = QueueRuntimeContracts.ForCpp(new IRNamedType("Text"));

        Assert.Equal("junexis::Queue<std::string>", binding.HandleType);
        Assert.Null(binding.FactoryExpression);
        Assert.Equal("enqueue", binding.EnqueueMethodName);
        Assert.Equal("delivery", binding.ReceiveMethodName);
        Assert.True(binding.ReceiveReturnsMessageId);
        Assert.Equal("ack", binding.AckMethodName);
        Assert.Equal("nack", binding.NackMethodName);
        Assert.True(binding.Supports(QueueRuntimeCapabilities.ProducerEnqueue));
        Assert.True(binding.Supports(QueueRuntimeCapabilities.ConsumerReceive));
        Assert.True(binding.Supports(QueueRuntimeCapabilities.Ack));
        Assert.True(binding.Supports(QueueRuntimeCapabilities.Nack));
        Assert.Equal(CommunicationDeliverySemantics.AtLeastOnce, binding.Contract.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerContinues, binding.Contract.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.UntilAck, binding.Contract.Retention);
        Assert.Equal(CommunicationOrderingSemantics.Fifo, binding.Contract.Ordering);
        Assert.Equal("queue.enqueue(value)", binding.RenderEnqueueInvocation("queue", "value"));
        Assert.Equal("queue.delivery()", binding.RenderReceiveInvocation("queue"));
        Assert.Equal("queue.ack(messageId)", binding.RenderAckInvocation("queue", "messageId"));
        Assert.Equal("queue.nack(messageId, reason)", binding.RenderNackInvocation("queue", "messageId", "reason"));
    }
}
