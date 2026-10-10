namespace JunexisLang.Compiler.CodeGen;

using JunexisLang.Compiler.IR;

internal sealed record QueueRuntimeBinding(
    string HandleType,
    string? FactoryExpression,
    QueueRuntimeContract Contract)
{
    public string EnqueueMethodName => Contract.EnqueueMethodName;

    public string ReceiveMethodName => Contract.ReceiveMethodName;

    public bool ReceiveReturnsMessageId => Contract.ReceiveReturnsMessageId;

    public string AckMethodName => Contract.AckMethodName;

    public string NackMethodName => Contract.NackMethodName;

    public QueueRuntimeCapabilities Capabilities => Contract.Capabilities;

    public bool Supports(QueueRuntimeCapabilities capability)
        => Contract.Supports(capability);

    public string RenderEnqueueInvocation(string handleName, string valueExpression)
        => Contract.RenderEnqueueInvocation(handleName, valueExpression);

    public string RenderReceiveInvocation(string handleName)
        => Contract.RenderReceiveInvocation(handleName);

    public string RenderAckInvocation(string handleName, string messageIdExpression)
        => Contract.RenderAckInvocation(handleName, messageIdExpression);

    public string RenderNackInvocation(string handleName, string messageIdExpression, string reasonExpression)
        => Contract.RenderNackInvocation(handleName, messageIdExpression, reasonExpression);
}

internal static class QueueRuntimeContracts
{
    public static QueueRuntimeBinding ForCpp(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToCpp(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Queue;
        return new QueueRuntimeBinding(
            HandleType: $"junexis::Queue<{elementTypeName}>",
            FactoryExpression: null,
            Contract: new QueueRuntimeContract(
                EnqueueMethodName: "enqueue",
                ReceiveMethodName: "delivery",
                ReceiveReturnsMessageId: true,
                AckMethodName: "ack",
                NackMethodName: "nack",
                Capabilities: QueueRuntimeCapabilities.ProducerEnqueue | QueueRuntimeCapabilities.ConsumerReceive | QueueRuntimeCapabilities.Ack | QueueRuntimeCapabilities.Nack,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }

    public static QueueRuntimeBinding ForGo(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToGo(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Queue;
        return new QueueRuntimeBinding(
            HandleType: $"*Queue[{elementTypeName}]",
            FactoryExpression: $"NewQueue[{elementTypeName}]()",
            Contract: new QueueRuntimeContract(
                EnqueueMethodName: "Enqueue",
                ReceiveMethodName: "Recv",
                ReceiveReturnsMessageId: true,
                AckMethodName: "Ack",
                NackMethodName: "Nack",
                Capabilities: QueueRuntimeCapabilities.ProducerEnqueue | QueueRuntimeCapabilities.ConsumerReceive | QueueRuntimeCapabilities.Ack | QueueRuntimeCapabilities.Nack,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }

    public static QueueRuntimeBinding ForTypeScript(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToTypeScript(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Queue;
        return new QueueRuntimeBinding(
            HandleType: $"Queue<{elementTypeName}>",
            FactoryExpression: null,
            Contract: new QueueRuntimeContract(
                EnqueueMethodName: "enqueue",
                ReceiveMethodName: "recv",
                ReceiveReturnsMessageId: true,
                AckMethodName: "ack",
                NackMethodName: "nack",
                Capabilities: QueueRuntimeCapabilities.ProducerEnqueue | QueueRuntimeCapabilities.ConsumerReceive | QueueRuntimeCapabilities.Ack | QueueRuntimeCapabilities.Nack,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }
}
