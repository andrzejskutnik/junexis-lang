namespace JunexisLang.Compiler.IR;

/// <summary>
/// Describes the channel runtime capabilities that generated code can rely on.
/// </summary>
[Flags]
public enum ChanRuntimeCapabilities
{
    /// <summary>
    /// No channel runtime capabilities are available.
    /// </summary>
    None = 0,

    /// <summary>
    /// The runtime supports sending values into a channel.
    /// </summary>
    Send = 1 << 0,

    /// <summary>
    /// The runtime supports consuming values from a channel.
    /// </summary>
    Consume = 1 << 1,
}

/// <summary>
/// Describes the emitted send shape for channel writes in generated code.
/// </summary>
public enum ChanSendStyle
{
    /// <summary>
    /// Send is represented as a method call, for example <c>channel.send(value)</c>.
    /// </summary>
    MethodCall,

    /// <summary>
    /// Send is represented as the Go channel operator form, for example <c>channel &lt;- value</c>.
    /// </summary>
    OperatorArrow,

    /// <summary>
    /// Send is represented as appending to a buffered adapter, for example <c>buffer.push(value)</c>.
    /// </summary>
    BufferPush,
}

/// <summary>
/// Represents the semantic channel runtime contract shared by compiler stages and generators.
/// </summary>
/// <param name="SendMethodName">Method used to send a channel value when the contract uses method-call syntax.</param>
/// <param name="SendStyle">Shape used to emit a channel send operation.</param>
/// <param name="Capabilities">Set of runtime capabilities guaranteed by the contract.</param>
/// <param name="Delivery">Delivery guarantee exposed by the channel.</param>
/// <param name="Backpressure">Producer-side backpressure behavior.</param>
/// <param name="Retention">How sent values are retained.</param>
/// <param name="Ordering">Ordering guarantee for delivered values.</param>
public sealed record ChanRuntimeContract(
    string? SendMethodName,
    ChanSendStyle SendStyle,
    ChanRuntimeCapabilities Capabilities,
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
    public bool Supports(ChanRuntimeCapabilities capability)
        => (Capabilities & capability) == capability;

    /// <summary>
    /// Renders a channel send invocation for generated code.
    /// </summary>
    /// <param name="handleExpression">Channel handle expression.</param>
    /// <param name="valueExpression">Expression representing the value to send.</param>
    /// <returns>Language-shaped send invocation string.</returns>
    public string RenderSendInvocation(string handleExpression, string valueExpression)
        => SendStyle switch
        {
            ChanSendStyle.MethodCall => $"{handleExpression}.{SendMethodName}({valueExpression})",
            ChanSendStyle.OperatorArrow => $"{handleExpression} <- {valueExpression}",
            ChanSendStyle.BufferPush => $"{handleExpression}.push({valueExpression})",
            _ => throw new NotSupportedException($"Unsupported chan send style: {SendStyle}"),
        };
}
