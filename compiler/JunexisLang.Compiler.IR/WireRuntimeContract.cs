namespace JunexisLang.Compiler.IR;

/// <summary>Backend-independent contract for values crossing a Junexis target boundary.</summary>
public sealed record WireRuntimeContract(
    string Version,
    string Encoding,
    string MediaType,
    string VersionHeader,
    string VersionField,
    string PayloadField);

/// <summary>Canonical wire contracts understood by the semantic and platform lowerings.</summary>
public static class WireRuntimeContractCatalog
{
    /// <summary>The versioned JSON envelope used by bootstrap target bridges.</summary>
    public static WireRuntimeContract Json { get; } = new(
        Version: "junexis-wire/0.2",
        Encoding: "json",
        MediaType: "application/json",
        VersionHeader: "X-Junexis-Wire-Version",
        VersionField: "version",
        PayloadField: "payload");
}
