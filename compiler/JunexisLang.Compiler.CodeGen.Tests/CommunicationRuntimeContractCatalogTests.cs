namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Tests the Junexis 0.2 communication runtime contract shared by all code generators.
/// </summary>
public sealed class CommunicationRuntimeContractCatalogTests
{
    [Fact]
    public void Catalog_DefinesCanonicalJunexis02Semantics()
    {
        AssertCanonical(
            CommunicationRuntimeContractCatalog.Chan,
            IRCommKind.Chan,
            CommunicationDeliverySemantics.Backpressured,
            CommunicationBackpressureSemantics.ProducerWaits,
            CommunicationRetentionSemantics.UntilConsumed,
            CommunicationOrderingSemantics.Fifo);

        AssertCanonical(
            CommunicationRuntimeContractCatalog.Queue,
            IRCommKind.Queue,
            CommunicationDeliverySemantics.AtLeastOnce,
            CommunicationBackpressureSemantics.ProducerContinues,
            CommunicationRetentionSemantics.UntilAck,
            CommunicationOrderingSemantics.Fifo);

        AssertCanonical(
            CommunicationRuntimeContractCatalog.Stream,
            IRCommKind.Stream,
            CommunicationDeliverySemantics.LatestWins,
            CommunicationBackpressureSemantics.ProducerContinues,
            CommunicationRetentionSemantics.LatestOnly,
            CommunicationOrderingSemantics.LatestOnly);
    }

    [Fact]
    public void PlatformBindings_PreserveCanonicalSemantics()
    {
        var type = new IRNamedType("Int");

        AssertSameSemantics(CommunicationRuntimeContractCatalog.Chan, ChanRuntimeContracts.ForGo(type).Contract);
        AssertSameSemantics(CommunicationRuntimeContractCatalog.Chan, ChanRuntimeContracts.ForTypeScript(type).Contract);
        AssertSameSemantics(CommunicationRuntimeContractCatalog.Chan, ChanRuntimeContracts.ForCpp(type).Contract);

        AssertSameSemantics(CommunicationRuntimeContractCatalog.Queue, QueueRuntimeContracts.ForGo(type).Contract);
        AssertSameSemantics(CommunicationRuntimeContractCatalog.Queue, QueueRuntimeContracts.ForTypeScript(type).Contract);
        AssertSameSemantics(CommunicationRuntimeContractCatalog.Queue, QueueRuntimeContracts.ForCpp(type).Contract);

        AssertSameSemantics(CommunicationRuntimeContractCatalog.Stream, StreamRuntimeContracts.ForGo(type).Contract);
        AssertSameSemantics(CommunicationRuntimeContractCatalog.Stream, StreamRuntimeContracts.ForTypeScript(type).Contract);
        AssertSameSemantics(CommunicationRuntimeContractCatalog.Stream, StreamRuntimeContracts.ForCpp(type).Contract);
    }

    [Fact]
    public void PlatformBindings_ExposeExplicitApiNameMapping()
    {
        var type = new IRNamedType("Int");

        Assert.Equal(ChanSendStyle.OperatorArrow, ChanRuntimeContracts.ForGo(type).Contract.SendStyle);
        Assert.Equal("send", ChanRuntimeContracts.ForCpp(type).Contract.SendMethodName);
        Assert.Equal(ChanSendStyle.MethodCall, ChanRuntimeContracts.ForTypeScript(type).Contract.SendStyle);

        Assert.Equal("Enqueue", QueueRuntimeContracts.ForGo(type).EnqueueMethodName);
        Assert.Equal("Recv", QueueRuntimeContracts.ForGo(type).ReceiveMethodName);
        Assert.Equal("Ack", QueueRuntimeContracts.ForGo(type).AckMethodName);
        Assert.Equal("Nack", QueueRuntimeContracts.ForGo(type).NackMethodName);

        Assert.Equal("enqueue", QueueRuntimeContracts.ForTypeScript(type).EnqueueMethodName);
        Assert.Equal("recv", QueueRuntimeContracts.ForTypeScript(type).ReceiveMethodName);
        Assert.Equal("ack", QueueRuntimeContracts.ForTypeScript(type).AckMethodName);
        Assert.Equal("nack", QueueRuntimeContracts.ForTypeScript(type).NackMethodName);

        Assert.Equal("enqueue", QueueRuntimeContracts.ForCpp(type).EnqueueMethodName);
        Assert.Equal("delivery", QueueRuntimeContracts.ForCpp(type).ReceiveMethodName);
        Assert.Equal("ack", QueueRuntimeContracts.ForCpp(type).AckMethodName);
        Assert.Equal("nack", QueueRuntimeContracts.ForCpp(type).NackMethodName);

        Assert.Equal("Publish", StreamRuntimeContracts.ForGo(type).PublishMethodName);
        Assert.Equal("publish", StreamRuntimeContracts.ForTypeScript(type).PublishMethodName);
        Assert.Equal("publish", StreamRuntimeContracts.ForCpp(type).PublishMethodName);
    }

    private static void AssertCanonical(
        CommunicationRuntimeSemantics actual,
        IRCommKind kind,
        CommunicationDeliverySemantics delivery,
        CommunicationBackpressureSemantics backpressure,
        CommunicationRetentionSemantics retention,
        CommunicationOrderingSemantics ordering)
    {
        Assert.Equal(kind, actual.Kind);
        Assert.Equal(delivery, actual.Delivery);
        Assert.Equal(backpressure, actual.Backpressure);
        Assert.Equal(retention, actual.Retention);
        Assert.Equal(ordering, actual.Ordering);
        Assert.Equal(actual, CommunicationRuntimeContractCatalog.For(kind));
    }

    private static void AssertSameSemantics(CommunicationRuntimeSemantics expected, ChanRuntimeContract actual)
    {
        Assert.Equal(expected.Delivery, actual.Delivery);
        Assert.Equal(expected.Backpressure, actual.Backpressure);
        Assert.Equal(expected.Retention, actual.Retention);
        Assert.Equal(expected.Ordering, actual.Ordering);
    }

    private static void AssertSameSemantics(CommunicationRuntimeSemantics expected, QueueRuntimeContract actual)
    {
        Assert.Equal(expected.Delivery, actual.Delivery);
        Assert.Equal(expected.Backpressure, actual.Backpressure);
        Assert.Equal(expected.Retention, actual.Retention);
        Assert.Equal(expected.Ordering, actual.Ordering);
    }

    private static void AssertSameSemantics(CommunicationRuntimeSemantics expected, StreamRuntimeContract actual)
    {
        Assert.Equal(expected.Delivery, actual.Delivery);
        Assert.Equal(expected.Backpressure, actual.Backpressure);
        Assert.Equal(expected.Retention, actual.Retention);
        Assert.Equal(expected.Ordering, actual.Ordering);
    }
}
