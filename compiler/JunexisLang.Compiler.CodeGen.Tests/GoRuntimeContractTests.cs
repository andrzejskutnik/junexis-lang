namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy kontraktów runtime dla generatora Go.
/// </summary>
public sealed class GoRuntimeContractTests
{
    [Fact]
    public void GoChanContract_ExposesSharedSendShape()
    {
        var binding = ChanRuntimeContracts.ForGo(new IRNamedType("Int"));

        Assert.Equal("chan int64", binding.HandleType);
        Assert.Equal("make(chan int64)", binding.FactoryExpression);
        Assert.False(binding.RequiresBufferedOutputAdapter);
        Assert.True(binding.Supports(ChanRuntimeCapabilities.Send));
        Assert.True(binding.Supports(ChanRuntimeCapabilities.Consume));
        Assert.Equal(CommunicationDeliverySemantics.Backpressured, binding.Contract.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerWaits, binding.Contract.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.UntilConsumed, binding.Contract.Retention);
        Assert.Equal(CommunicationOrderingSemantics.Fifo, binding.Contract.Ordering);
        Assert.Equal("result <- value", binding.RenderSendInvocation("result", "value"));
    }

    [Fact]
    public void GoStreamContract_ExposesSharedPublishShape()
    {
        var binding = StreamRuntimeContracts.ForGo(new IRNamedType("Int"));

        Assert.Equal("*Stream[int64]", binding.HandleType);
        Assert.Equal("NewStream[int64]()", binding.FactoryExpression);
        Assert.Equal("Publish", binding.Contract.PublishMethodName);
        Assert.Equal("Subscribe", binding.Contract.SubscribeMethodName);
        Assert.True(binding.Supports(StreamRuntimeCapabilities.Publish));
        Assert.True(binding.Supports(StreamRuntimeCapabilities.Consume));
        Assert.True(binding.Supports(StreamRuntimeCapabilities.Subscribe));
        Assert.Equal(CommunicationDeliverySemantics.LatestWins, binding.Contract.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerContinues, binding.Contract.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.LatestOnly, binding.Contract.Retention);
        Assert.Equal(CommunicationOrderingSemantics.LatestOnly, binding.Contract.Ordering);
        Assert.Equal("result.Publish(value)", binding.RenderPublishInvocation("result", "value"));
        Assert.Equal("result.Subscribe()", binding.RenderSubscribeInvocation("result"));
    }

    [Fact]
    public void GoQueueContract_ExposesProducerOnlyQueueShape()
    {
        var contract = QueueRuntimeContracts.ForGo(new IRNamedType("Int"));

        Assert.Equal("*Queue[int64]", contract.HandleType);
        Assert.Equal("NewQueue[int64]()", contract.FactoryExpression);
        Assert.Equal("Enqueue", contract.Contract.EnqueueMethodName);
        Assert.Equal("Recv", contract.Contract.ReceiveMethodName);
        Assert.Equal("Enqueue", contract.EnqueueMethodName);
        Assert.Equal("Recv", contract.ReceiveMethodName);
        Assert.True(contract.ReceiveReturnsMessageId);
        Assert.Equal("Ack", contract.AckMethodName);
        Assert.Equal("Nack", contract.NackMethodName);
        Assert.True(contract.Supports(QueueRuntimeCapabilities.ProducerEnqueue));
        Assert.True(contract.Supports(QueueRuntimeCapabilities.ConsumerReceive));
        Assert.True(contract.Supports(QueueRuntimeCapabilities.Ack));
        Assert.True(contract.Supports(QueueRuntimeCapabilities.Nack));
        Assert.Equal(CommunicationDeliverySemantics.AtLeastOnce, contract.Contract.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerContinues, contract.Contract.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.UntilAck, contract.Contract.Retention);
        Assert.Equal(CommunicationOrderingSemantics.Fifo, contract.Contract.Ordering);
        Assert.Equal("result.Enqueue(value)", contract.RenderEnqueueInvocation("result", "value"));
        Assert.Equal("input.Recv()", contract.RenderReceiveInvocation("input"));
        Assert.Equal("input.Ack(messageId)", contract.RenderAckInvocation("input", "messageId"));
        Assert.Equal("input.Nack(messageId, err.Error())", contract.RenderNackInvocation("input", "messageId", "err.Error()"));
    }
}
