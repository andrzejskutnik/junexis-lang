namespace JunexisLang.Compiler.IR;

/// <summary>
/// Delivery guarantee exposed by a Junexis communication runtime contract.
/// </summary>
public enum CommunicationDeliverySemantics
{
    /// <summary>Delivery semantics are not specified.</summary>
    Unspecified,

    /// <summary>Producer and consumer synchronize through bounded delivery/backpressure.</summary>
    Backpressured,

    /// <summary>Messages stay pending until explicitly acknowledged.</summary>
    AtLeastOnce,

    /// <summary>Only the latest value is retained for observers.</summary>
    LatestWins
}

/// <summary>
/// Producer-side backpressure behavior.
/// </summary>
public enum CommunicationBackpressureSemantics
{
    /// <summary>Backpressure behavior is not specified.</summary>
    Unspecified,

    /// <summary>Producer may block or await when the communication buffer cannot accept more values.</summary>
    ProducerWaits,

    /// <summary>Producer does not wait for a specific consumer to receive the value.</summary>
    ProducerContinues
}

/// <summary>
/// How values are retained by the communication primitive.
/// </summary>
public enum CommunicationRetentionSemantics
{
    /// <summary>Retention behavior is not specified.</summary>
    Unspecified,

    /// <summary>Values are retained until consumed by the receiver.</summary>
    UntilConsumed,

    /// <summary>Values are retained until ACK removes them; NACK keeps or requeues them.</summary>
    UntilAck,

    /// <summary>Only the most recent value is retained.</summary>
    LatestOnly
}

/// <summary>
/// Ordering guarantee for delivered values.
/// </summary>
public enum CommunicationOrderingSemantics
{
    /// <summary>Ordering is not specified.</summary>
    Unspecified,

    /// <summary>Values are observed in FIFO order.</summary>
    Fifo,

    /// <summary>Only the latest value matters, so historical FIFO ordering is not retained.</summary>
    LatestOnly
}
