using System.Globalization;
using JunexisLang.Compiler.Core.Platform;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CodeGen;

/// <summary>
/// Shared helpers for resolving platform profiles during code generation.
/// </summary>
internal static class CodeGenPlatformProfiles
{
    /// <summary>
    /// Resolves the effective profile for a given IR platform annotation.
    /// </summary>
    /// <param name="platform">IR platform annotation.</param>
    /// <returns>The resolved capabilities profile.</returns>
    internal static PlatformCapabilities Resolve(IRPlatform platform)
    {
        try
        {
            return PlatformProfiles.GetProfile("@" + platform.Target, GetVariant(platform));
        }
        catch (InvalidOperationException)
        {
            return PlatformProfiles.GetProfile("@" + platform.Target);
        }
    }

    /// <summary>
    /// Formats bytes for human-readable generated metadata.
    /// </summary>
    /// <param name="bytes">Byte count.</param>
    /// <returns>Compact byte string.</returns>
    internal static string FormatBytes(long bytes)
    {
        const long kb = 1024;
        const long mb = 1024 * kb;
        const long gb = 1024 * mb;

        return bytes switch
        {
            >= gb => $"{(bytes / (double)gb).ToString("F1", CultureInfo.InvariantCulture)} GB",
            >= mb => $"{(bytes / (double)mb).ToString("F1", CultureInfo.InvariantCulture)} MB",
            >= kb => $"{(bytes / (double)kb).ToString("F1", CultureInfo.InvariantCulture)} KB",
            _ => $"{bytes} B"
        };
    }

    private static string? GetVariant(IRPlatform platform)
    {
        var positional = platform.Args.FirstOrDefault(arg => arg.Key is null);
        return positional?.Value switch
        {
            IRIdentExpr identifier => identifier.Name,
            IRLiteralExpr { Value: IRTextValue text } => text.Value,
            _ => null
        };
    }
}