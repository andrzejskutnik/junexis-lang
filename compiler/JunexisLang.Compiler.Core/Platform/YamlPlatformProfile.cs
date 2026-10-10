using YamlDotNet.Serialization;

namespace JunexisLang.Compiler.Core.Platform;

/// <summary>
/// Root DTO for YAML platform profile deserialization.
/// </summary>
internal sealed class YamlPlatformProfile
{
    /// <summary>
    /// Profile schema version.
    /// </summary>
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Platform identifier.
    /// </summary>
    [YamlMember(Alias = "platform")]
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Profile variant.
    /// </summary>
    [YamlMember(Alias = "variant")]
    public string Variant { get; set; } = string.Empty;

    /// <summary>
    /// Optional parent profile name.
    /// </summary>
    [YamlMember(Alias = "extends")]
    public string? Extends { get; set; }

    /// <summary>
    /// Optional list of explicitly overridden properties.
    /// </summary>
    [YamlMember(Alias = "overrides")]
    public List<string>? Overrides { get; set; }

    /// <summary>
    /// Metadata section.
    /// </summary>
    [YamlMember(Alias = "metadata")]
    public YamlMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Capabilities section.
    /// </summary>
    [YamlMember(Alias = "capabilities")]
    public YamlCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Limits section.
    /// </summary>
    [YamlMember(Alias = "limits")]
    public YamlLimits Limits { get; set; } = new();

    /// <summary>
    /// Hardware metadata section.
    /// </summary>
    [YamlMember(Alias = "hardware")]
    public YamlHardware Hardware { get; set; } = new();

    /// <summary>
    /// Constraint list.
    /// </summary>
    [YamlMember(Alias = "constraints")]
    public List<YamlConstraint> Constraints { get; set; } = new();

    /// <summary>
    /// Recommendation section.
    /// </summary>
    [YamlMember(Alias = "recommended")]
    public YamlRecommended Recommended { get; set; } = new();
}

/// <summary>
/// YAML metadata block.
/// </summary>
internal sealed class YamlMetadata
{
    /// <summary>
    /// Human-readable description.
    /// </summary>
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Manufacturer name.
    /// </summary>
    [YamlMember(Alias = "manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>
    /// Architecture label.
    /// </summary>
    [YamlMember(Alias = "architecture")]
    public string Architecture { get; set; } = string.Empty;

    /// <summary>
    /// Frequency label.
    /// </summary>
    [YamlMember(Alias = "frequency")]
    public string Frequency { get; set; } = string.Empty;

    /// <summary>
    /// Release year.
    /// </summary>
    [YamlMember(Alias = "releaseYear")]
    public int ReleaseYear { get; set; }

    /// <summary>
    /// Optional documentation URI.
    /// </summary>
    [YamlMember(Alias = "documentation")]
    public string Documentation { get; set; } = string.Empty;
}

/// <summary>
/// YAML capability flags.
/// </summary>
internal sealed class YamlCapabilities
{
    /// <summary>
    /// Threading support.
    /// </summary>
    [YamlMember(Alias = "threading")]
    public bool? Threading { get; set; }

    /// <summary>
    /// File I/O support.
    /// </summary>
    [YamlMember(Alias = "fileIO")]
    public bool? FileIO { get; set; }

    /// <summary>
    /// Networking support.
    /// </summary>
    [YamlMember(Alias = "networking")]
    public bool? Networking { get; set; }

    /// <summary>
    /// Async support.
    /// </summary>
    [YamlMember(Alias = "async")]
    public bool? Async { get; set; }

    /// <summary>
    /// Dynamic allocation support.
    /// </summary>
    [YamlMember(Alias = "dynamicAllocation")]
    public bool? DynamicAllocation { get; set; }

    /// <summary>
    /// Real-time clock support.
    /// </summary>
    [YamlMember(Alias = "realTimeClock")]
    public bool? RealTimeClock { get; set; }

    /// <summary>
    /// Hardware interrupt support.
    /// </summary>
    [YamlMember(Alias = "hardwareInterrupts")]
    public bool? HardwareInterrupts { get; set; }
}

/// <summary>
/// YAML resource limits.
/// </summary>
internal sealed class YamlLimits
{
    /// <summary>
    /// Maximum memory in bytes.
    /// </summary>
    [YamlMember(Alias = "maxMemoryBytes")]
    public long MaxMemoryBytes { get; set; }

    /// <summary>
    /// Maximum concurrent tasks.
    /// </summary>
    [YamlMember(Alias = "maxConcurrentTasks")]
    public int MaxConcurrentTasks { get; set; }
}

/// <summary>
/// YAML hardware metadata block.
/// </summary>
internal sealed class YamlHardware
{
    /// <summary>
    /// RAM description.
    /// </summary>
    [YamlMember(Alias = "ram")]
    public string Ram { get; set; } = string.Empty;

    /// <summary>
    /// Flash description.
    /// </summary>
    [YamlMember(Alias = "flash")]
    public string Flash { get; set; } = string.Empty;

    /// <summary>
    /// GPIO count.
    /// </summary>
    [YamlMember(Alias = "gpio")]
    public int Gpio { get; set; }

    /// <summary>
    /// SPI peripheral count.
    /// </summary>
    [YamlMember(Alias = "spi")]
    public int Spi { get; set; }

    /// <summary>
    /// I2C peripheral count.
    /// </summary>
    [YamlMember(Alias = "i2c")]
    public int I2c { get; set; }

    /// <summary>
    /// UART peripheral count.
    /// </summary>
    [YamlMember(Alias = "uart")]
    public int Uart { get; set; }

    /// <summary>
    /// WiFi description.
    /// </summary>
    [YamlMember(Alias = "wifi")]
    public string Wifi { get; set; } = string.Empty;

    /// <summary>
    /// Bluetooth description.
    /// </summary>
    [YamlMember(Alias = "bluetooth")]
    public string Bluetooth { get; set; } = string.Empty;
}

/// <summary>
/// YAML constraint block.
/// </summary>
internal sealed class YamlConstraint
{
    /// <summary>
    /// Constraint type.
    /// </summary>
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Severity label.
    /// </summary>
    [YamlMember(Alias = "severity")]
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable message.
    /// </summary>
    [YamlMember(Alias = "message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Constraint code.
    /// </summary>
    [YamlMember(Alias = "code")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// YAML recommendation block.
/// </summary>
internal sealed class YamlRecommended
{
    /// <summary>
    /// Recommended use cases.
    /// </summary>
    [YamlMember(Alias = "useCases")]
    public List<string> UseCases { get; set; } = new();

    /// <summary>
    /// Not recommended use cases.
    /// </summary>
    [YamlMember(Alias = "notRecommended")]
    public List<string> NotRecommended { get; set; } = new();
}