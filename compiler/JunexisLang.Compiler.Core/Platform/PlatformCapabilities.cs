namespace JunexisLang.Compiler.Core.Platform;

/// <summary>
/// Defines the capabilities and constraints of a target platform.
/// </summary>
public sealed class PlatformCapabilities
{
    /// <summary>
    /// Platform identifier, e.g. "@embedded", "@server", "@report".
    /// </summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>
    /// Variant identifier within a platform, e.g. "esp32c3", "linux", "browser".
    /// </summary>
    public string Variant { get; init; } = string.Empty;

    /// <summary>
    /// Optional parent profile used for inheritance.
    /// </summary>
    public string? Extends { get; init; }

    /// <summary>
    /// Indicates whether the platform supports multi-threaded execution.
    /// </summary>
    public bool SupportsThreading { get; init; }

    /// <summary>
    /// Indicates whether the platform supports file system access.
    /// </summary>
    public bool SupportsFileIO { get; init; }

    /// <summary>
    /// Indicates whether the platform supports networking.
    /// </summary>
    public bool SupportsNetworking { get; init; }

    /// <summary>
    /// Indicates whether the platform supports async-style execution.
    /// </summary>
    public bool SupportsAsync { get; init; }

    /// <summary>
    /// Maximum available memory in bytes.
    /// </summary>
    public long MaxMemoryBytes { get; init; }

    /// <summary>
    /// Maximum number of concurrent logical tasks.
    /// </summary>
    public int MaxConcurrentTasks { get; init; }

    /// <summary>
    /// Indicates whether dynamic allocation is supported.
    /// </summary>
    public bool SupportsDynamicAllocation { get; init; }

    /// <summary>
    /// Indicates whether a real-time clock is available.
    /// </summary>
    public bool SupportsRealTimeClock { get; init; }

    /// <summary>
    /// Indicates whether hardware interrupts are available.
    /// </summary>
    public bool SupportsHardwareInterrupts { get; init; }

    /// <summary>
    /// Platform-specific constraints emitted as additional guidance.
    /// </summary>
    public List<PlatformConstraint> Constraints { get; init; } = new();
}

/// <summary>
/// Represents a single platform constraint or limitation.
/// </summary>
public sealed class PlatformConstraint
{
    /// <summary>
    /// Constraint type, e.g. "error", "warning", "info".
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Severity string preserved from the source profile.
    /// </summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Machine-readable constraint code.
    /// </summary>
    public string Code { get; init; } = string.Empty;
}