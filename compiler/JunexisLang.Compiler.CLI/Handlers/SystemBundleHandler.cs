using System.Security.Cryptography;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class SystemBundleHandler
{
    internal static async Task<int> CreateAsync(
        string? project,
        FileInfo? output)
    {
        try
        {
            var result = await SystemBundleContract.CreateAsync(project, output)
                .ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"System Bundle 0.1: {result.Path}");
            await Console.Out.WriteLineAsync($"Bundle ID: {result.BundleId}");
            await Console.Out.WriteLineAsync(
                $"Build ID: {result.Descriptor.BuildId} · " +
                $"{result.VerifiedFileCount} checksummed payload file(s)");
            return 0;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
    }

    internal static async Task<int> SignAsync(
        FileInfo bundle,
        FileInfo privateKey,
        string signer,
        FileInfo? output)
    {
        try
        {
            var result = await SystemBundleAuthenticityContract.SignAsync(
                bundle.FullName,
                privateKey.FullName,
                signer,
                output?.FullName).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"System Bundle signature: {result.Path}");
            await Console.Out.WriteLineAsync($"Signer: {result.Signature.Signer}");
            await Console.Out.WriteLineAsync(
                $"Fingerprint: {result.Signature.PublicKeyFingerprintSha256}");
            await Console.Out.WriteLineAsync($"Bundle ID: {result.Signature.BundleId}");
            return 0;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or CryptographicException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
    }

    internal static async Task<int> VerifyAsync(
        FileInfo bundle,
        FileInfo? signature,
        string authenticityPolicy,
        string[]? trustedSigners)
    {
        try
        {
            var result = await SystemBundleContract.VerifyAsync(bundle.FullName)
                .ConfigureAwait(false);
            var authenticity = await SystemBundleAuthenticityContract.InspectAsync(
                bundle.FullName,
                signature?.FullName,
                authenticityPolicy,
                trustedSigners).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"System Bundle verified: {result.Path}");
            await Console.Out.WriteLineAsync($"Schema: {result.Descriptor.SchemaVersion}");
            await Console.Out.WriteLineAsync($"Bundle ID: {result.BundleId}");
            await Console.Out.WriteLineAsync(
                $"Project: {result.Descriptor.ProjectName} · " +
                $"Build: {result.Descriptor.BuildId} · " +
                $"{result.VerifiedFileCount} verified payload file(s)");
            await Console.Out.WriteLineAsync(
                $"Authenticity: {authenticity.Status.ToUpperInvariant()} · " +
                $"policy {authenticity.Policy} · " +
                (authenticity.PolicySatisfied ? "accepted" : "blocked"));
            if (authenticity.Signer is not null)
                await Console.Out.WriteLineAsync(
                    $"Signer: {authenticity.Signer} · {authenticity.PublicKeyFingerprintSha256}");
            await Console.Out.WriteLineAsync(authenticity.Summary);
            return authenticity.PolicySatisfied ? 0 : 1;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or CryptographicException
                or NotSupportedException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
    }
}
