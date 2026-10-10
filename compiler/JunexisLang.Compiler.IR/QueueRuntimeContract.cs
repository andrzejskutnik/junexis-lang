namespace JunexisLang.Compiler.IR;

/// <summary>
/// Describes the queue runtime capabilities that generated code can rely on.
/// </summary>
[System.Flags]
public enum QueueRuntimeCapabilities
{
    /// <summary>
    /// No queue runtime capabilities are available.
    /// </summary>
    None = 0,

    /// <summary>
    /// The runtime supports producer-side enqueue operations.
    /// </summary>
    ProducerEnqueue = 1,

    /// <summary>
    /// The runtime supports consumer-side receive operations.
    /// </summary>
    ConsumerReceive = 2,

    /// <summary>
    /// The runtime supports acknowledging delivered messages.
    /// </summary>
    Ack = 4,

    /// <summary>
    /// The runtime supports negative acknowledgement for redelivery or DLQ routing.
    /// </summary>
    Nack = 8,
}

/// <summary>
/// Represents the semantic queue runtime contract shared by compiler stages and generators.
/// </summary>
/// <param name="EnqueueMethodName">Method used to enqueue a new message.</param>
/// <param name="ReceiveMethodName">Method used to receive a queue item.</param>
/// <param name="ReceiveReturnsMessageId">Indicates whether receive returns an explicit message identifier required for ACK/NACK.</param>
/// <param name="AckMethodName">Method used to acknowledge a received message.</param>
/// <param name="NackMethodName">Method used to negatively acknowledge a received message.</param>
/// <param name="Capabilities">Set of runtime capabilities guaranteed by the contract.</param>
/// <param name="Delivery">Delivery guarantee exposed by the queue.</param>
/// <param name="Backpressure">Producer-side backpressure behavior.</param>
/// <param name="Retention">How queued values are retained.</param>
/// <param name="Ordering">Ordering guarantee for delivered values.</param>
public sealed record QueueRuntimeContract(
    string EnqueueMethodName,
    string ReceiveMethodName,
    bool ReceiveReturnsMessageId,
    string AckMethodName,
    string NackMethodName,
    QueueRuntimeCapabilities Capabilities,
    CommunicationDeliverySemantics Delivery,
    CommunicationBackpressureSemantics Backpressure,
    CommunicationRetentionSemantics Retention,
    CommunicationOrderingSemantics Ordering)
{
    /// <summary>
    /// Returns <see langword="true"/> when the contract guarantees the specified capability.
    /// </summary>
    /// <param name="capability">Capability to check.</param>
    /// <returns><see langword="true"/> if the capability is present; otherwise <see langword="false"/>.</returns>
    public bool Supports(QueueRuntimeCapabilities capability)
        => (Capabilities & capability) == capability;

    /// <summary>
    /// Renders a queue enqueue invocation for generated code.
    /// </summary>
    /// <param name="handleName">Queue handle variable name.</param>
    /// <param name="valueExpression">Expression representing the value to enqueue.</param>
    /// <returns>Language-shaped enqueue invocation string.</returns>
    public string RenderEnqueueInvocation(string handleName, string valueExpression)
        => $"{handleName}.{EnqueueMethodName}({valueExpression})";

    /// <summary>
    /// Renders a queue receive invocation for generated code.
    /// </summary>
    /// <param name="handleName">Queue handle variable name.</param>
    /// <returns>Language-shaped receive invocation string.</returns>
    public string RenderReceiveInvocation(string handleName)
        => $"{handleName}.{ReceiveMethodName}()";

    /// <summary>
    /// Renders a queue acknowledge invocation for generated code.
    /// </summary>
    /// <param name="handleName">Queue handle variable name.</param>
    /// <param name="messageIdExpression">Expression identifying the in-flight message.</param>
    /// <returns>Language-shaped acknowledge invocation string.</returns>
    public string RenderAckInvocation(string handleName, string messageIdExpression)
        => $"{handleName}.{AckMethodName}({messageIdExpression})";

    /// <summary>
    /// Renders a queue negative acknowledge invocation for generated code.
    /// </summary>
    /// <param name="handleName">Queue handle variable name.</param>
    /// <param name="messageIdExpression">Expression identifying the in-flight message.</param>
    /// <param name="reasonExpression">Expression describing the failure reason.</param>
    /// <returns>Language-shaped negative acknowledge invocation string.</returns>
    public string RenderNackInvocation(string handleName, string messageIdExpression, string reasonExpression)
        => $"{handleName}.{NackMethodName}({messageIdExpression}, {reasonExpression})";
}
