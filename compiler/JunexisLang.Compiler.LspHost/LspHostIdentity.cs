using System.Reflection;
using System.Text.Json;

namespace JunexisLang.Compiler.LspHost;

internal static class LspHostIdentity
{
    internal const int DistributionSchemaVersion = 1;
    internal const int ProtocolVersion = 1;
    internal const string HostName = "JunexisLang.Compiler.LspHost";

    internal static string RenderJson()
    {
        return JsonSerializer.Serialize(new
        {
            name = HostName,
            version = ResolveVersion(),
            protocolVersion = ProtocolVersion
        });
    }

    internal static string RenderDistributionManifest(
        string runtimeIdentifier,
        string entrypoint,
        bool selfContained)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrypoint);

        if (entrypoint.Contains('/') || entrypoint.Contains('\\'))
        {
            throw new ArgumentException(
                "The distribution entrypoint must be a file name.",
                nameof(entrypoint));
        }

        return JsonSerializer.Serialize(new
        {
            schemaVersion = DistributionSchemaVersion,
            name = HostName,
            version = ResolveVersion(),
            protocolVersion = ProtocolVersion,
            runtimeIdentifier,
            entrypoint,
            readyToRun = true,
            selfContained
        });
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(LspHostIdentity).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return informationalVersion?.Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }
}
