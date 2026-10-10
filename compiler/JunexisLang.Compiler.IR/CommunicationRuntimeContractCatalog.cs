namespace JunexisLang.Compiler.IR;

/// <summary>
/// Canonical Junexis 0.2 communication runtime semantics.
/// </summary>
public static class CommunicationRuntimeContractCatalog
{
    /// <summary>
    /// <c>chan&lt;T&gt;</c>: bounded FIFO communication with producer-side backpressure.
    /// Values are retained until consumed by the receiver.
    /// </summary>
    public static CommunicationRuntimeSemantics Chan { get; } = new(
        Kind: IRCommKind.Chan,
        Delivery: CommunicationDeliverySemantics.Backpressured,
        Backpressure: CommunicationBackpressureSemantics.ProducerWaits,
        Retention: CommunicationRetentionSemantics.UntilConsumed,
        Ordering: CommunicationOrderingSemantics.Fifo);

    /// <summary>
    /// <c>queue&lt;T&gt;</c>: at-least-once delivery with explicit ACK/NACK and FIFO ordering.
    /// Values are retained until acknowledged.
    /// </summary>
    public static CommunicationRuntimeSemantics Queue { get; } = new(
        Kind: IRCommKind.Queue,
        Delivery: CommunicationDeliverySemantics.AtLeastOnce,
        Backpressure: CommunicationBackpressureSemantics.ProducerContinues,
        Retention: CommunicationRetentionSemantics.UntilAck,
        Ordering: CommunicationOrderingSemantics.Fifo);

    /// <summary>
    /// <c>stream&lt;T&gt;</c>: latest-wins observation/fanout.
    /// Only the latest value is retained.
    /// </summary>
    public static CommunicationRuntimeSemantics Stream { get; } = new(
        Kind: IRCommKind.Stream,
        Delivery: CommunicationDeliverySemantics.LatestWins,
        Backpressure: CommunicationBackpressureSemantics.ProducerContinues,
        Retention: CommunicationRetentionSemantics.LatestOnly,
        Ordering: CommunicationOrderingSemantics.LatestOnly);

    /// <summary>
    /// Resolves the canonical Junexis 0.2 semantics for a communication type kind.
    /// </summary>
    public static CommunicationRuntimeSemantics For(IRCommKind kind)
        => kind switch
        {
            IRCommKind.Chan => Chan,
            IRCommKind.Queue => Queue,
            IRCommKind.Stream => Stream,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported communication runtime kind.")
        };
}

/// <summary>
/// Canonical semantic contract for a Junexis communication type.
/// Platform bindings may expose different API names, but must preserve these semantics.
/// </summary>
public sealed record CommunicationRuntimeSemantics(
    IRCommKind Kind,
    CommunicationDeliverySemantics Delivery,
    CommunicationBackpressureSemantics Backpressure,
    CommunicationRetentionSemantics Retention,
    CommunicationOrderingSemantics Ordering);
