namespace JunexisLang.Compiler.CodeGen;

using System.Text.RegularExpressions;
using JunexisLang.Compiler.IR;

/// <summary>Poziom instrumentacji runtime wybrany przez blok Narrative.</summary>
public enum TraceEmissionPolicy
{
    /// <summary>Artefakt nie emituje zdarzeń runtime.</summary>
    Off,

    /// <summary>Artefakt emituje granice tasków i komunikacji.</summary>
    Minimal,

    /// <summary>Artefakt emituje pełne dostępne dowody komunikacji.</summary>
    Full,
}

/// <summary>
/// Kanoniczne nazwy protokołu telemetrycznego współdzielone przez generatory
/// artefaktów C++, Go i TypeScript.
/// </summary>
public static partial class TraceRuntimeContract
{
    /// <summary>Wersja modelu zdarzeń przyjmowanego przez TraceCollector.</summary>
    public const string EventModel = "junexis.tracecollector.v1";

    /// <summary>Zmienna środowiskowa z adresem bazowym TraceCollectora.</summary>
    public const string TraceUrlEnvironmentVariable = "JUNEXIS_TRACE_URL";

    /// <summary>Opcjonalna zmienna środowiskowa wiążąca zdarzenia z buildem.</summary>
    public const string BuildIdEnvironmentVariable = "JUNEXIS_BUILD_ID";

    /// <summary>Opcjonalna zmienna środowiskowa wiążąca zdarzenia z pakietem System Bundle.</summary>
    public const string BundleIdEnvironmentVariable = "JUNEXIS_BUNDLE_ID";

    /// <summary>Ścieżka endpointu ingestującego zdarzenia.</summary>
    public const string EventsPath = "/events";

    /// <summary>Ścieżka endpointu kontrolującego debug gate.</summary>
    public const string DebugSessionPath = "/debug/session";

    /// <summary>Zdarzenie wysłania komunikatu.</summary>
    public const string CommunicationSend = "COMM_SEND";

    /// <summary>Zdarzenie odebrania komunikatu.</summary>
    public const string CommunicationReceive = "COMM_RECV";

    /// <summary>Atrybut stabilnego kontekstu Narrative.</summary>
    public const string NarrativeIdAttribute = "narrative_id";

    /// <summary>Atrybut korelujący wysłanie i odebranie payloadu.</summary>
    public const string PayloadHashAttribute = "payloadHash";

    /// <summary>Atrybut logicznego typu payloadu lub zdarzenia.</summary>
    public const string PayloadTypeAttribute = "type";

    /// <summary>Atrybut identyfikujący build artefaktu.</summary>
    public const string BuildIdAttribute = "build_id";

    /// <summary>Atrybut identyfikujący zweryfikowany System Bundle.</summary>
    public const string BundleIdAttribute = "bundle_id";

    /// <summary>
    /// Rozstrzyga politykę pierwszej Narrative, zachowując historyczny tryb
    /// minimalny, gdy blok nie deklaruje jawnej polityki.
    /// </summary>
    /// <param name="narratives">Narrative z dokumentu Structural IR.</param>
    /// <returns>Polityka instrumentacji dla generowanego artefaktu.</returns>
    public static TraceEmissionPolicy ResolvePolicy(IReadOnlyList<IRNarrative> narratives)
    {
        if (narratives.Count == 0)
            return TraceEmissionPolicy.Off;

        var match = TracePolicyRegex().Match(narratives[0].RawBody);
        if (!match.Success)
            return TraceEmissionPolicy.Minimal;

        return match.Groups["value"].Value.ToLowerInvariant() switch
        {
            "none" or "off" => TraceEmissionPolicy.Off,
            "full" => TraceEmissionPolicy.Full,
            _ => TraceEmissionPolicy.Minimal,
        };
    }

    [GeneratedRegex(
        @"\btrace\s*:?\s*(?<value>full|causal|minimal|none|off)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TracePolicyRegex();
}
