namespace JunexisLang.Compiler.IR;

/// <summary>
/// Describes the stream runtime capabilities that generated code can rely on.
/// </summary>
[System.Flags]
public enum StreamRuntimeCapabilities
{
    /// <summary>
    /// No stream runtime capabilities are available.
    /// </summary>
    None = 0,

    /// <summary>
    /// The runtime supports publishing values into a stream.
    /// </summary>
    Publish = 1,

    /// <summary>
    /// The runtime supports consuming the latest stream values.
    /// </summary>
    Consume = 2,

    /// <summary>
    /// The runtime supports independent subscribers with no history replay.
    /// </summary>
    Subscribe = 4,
}

/// <summary>
/// Represents the semantic stream runtime contract shared by compiler stages and generators.
/// </summary>
/// <param name="PublishMethodName">Method used to publish a new stream value.</param>
/// <param name="SubscribeMethodName">Method used to create an independent stream subscription.</param>
/// <param name="Capabilities">Set of runtime capabilities guaranteed by the contract.</param>
/// <param name="Delivery">Delivery guarantee exposed by the stream.</param>
/// <param name="Backpressure">Producer-side backpressure behavior.</param>
/// <param name="Retention">How stream values are retained.</param>
/// <param name="Ordering">Ordering guarantee for delivered values.</param>
public sealed record StreamRuntimeContract(
    string PublishMethodName,
    string SubscribeMethodName,
    StreamRuntimeCapabilities Capabilities,
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
    public bool Supports(StreamRuntimeCapabilities capability)
        => (Capabilities & capability) == capability;

    /// <summary>
    /// Renders a stream publish invocation for generated code.
    /// </summary>
    /// <param name="handleName">Stream handle variable name.</param>
    /// <param name="valueExpression">Expression representing the published value.</param>
    /// <returns>Language-shaped publish invocation string.</returns>
    public string RenderPublishInvocation(string handleName, string valueExpression)
        => $"{handleName}.{PublishMethodName}({valueExpression})";

    /// <summary>Renders creation of an independent stream subscription.</summary>
    /// <param name="handleName">Stream handle variable name.</param>
    /// <returns>Language-shaped subscription invocation string.</returns>
    public string RenderSubscribeInvocation(string handleName)
        => $"{handleName}.{SubscribeMethodName}()";
}
