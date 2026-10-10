using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record SystemBundleSignature(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("signatureFormat")] string SignatureFormat,
    [property: JsonPropertyName("signedPayload")] string SignedPayload,
    [property: JsonPropertyName("signer")] string Signer,
    [property: JsonPropertyName("publicKeySpki")] string PublicKeySpki,
    [property: JsonPropertyName("publicKeyFingerprintSha256")] string PublicKeyFingerprintSha256,
    [property: JsonPropertyName("signature")] string Signature)
{
    internal const string CurrentVersion = "junexis.system-bundle-signature/0.1";
    internal const string CurrentAlgorithm = "ECDSA-P256-SHA256";
    internal const string CurrentSignatureFormat = "IEEE-P1363";
    internal const string CurrentSignedPayload = "manifest.json";
}

internal sealed record SystemBundleAuthenticity(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("policy")] string Policy,
    [property: JsonPropertyName("policySatisfied")] bool PolicySatisfied,
    [property: JsonPropertyName("signer")] string? Signer,
    [property: JsonPropertyName("publicKeyFingerprintSha256")] string? PublicKeyFingerprintSha256,
    [property: JsonPropertyName("signaturePath")] string? SignaturePath,
    [property: JsonPropertyName("summary")] string Summary);

internal static class SystemBundleAuthenticityContract
{
    private const int MaximumSignatureBytes = 1024 * 1024;
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static string DefaultPath(string bundlePath) =>
        Path.GetFullPath(bundlePath) + ".signature.json";

    internal static async Task<(string Path, SystemBundleSignature Signature)> SignAsync(
        string bundlePath,
        string privateKeyPath,
        string signer,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signer) || signer.Length > 200)
            throw new ArgumentException("System Bundle signer name must contain 1-200 characters.");
        var inspection = await SystemBundleContract.VerifyAsync(bundlePath, cancellationToken)
            .ConfigureAwait(false);
        var manifest = await ReadManifestAsync(inspection.Path, cancellationToken)
            .ConfigureAwait(false);
        var keyFile = new FileInfo(Path.GetFullPath(privateKeyPath));
        if (!keyFile.Exists)
            throw new FileNotFoundException("System Bundle signing key was not found.", keyFile.FullName);
        if (keyFile.Length is <= 0 or > MaximumSignatureBytes)
            throw new InvalidDataException("System Bundle signing key exceeds its safety limit.");

        using var key = ECDsa.Create();
        key.ImportFromPem(await File.ReadAllTextAsync(keyFile.FullName, cancellationToken)
            .ConfigureAwait(false));
        if (key.KeySize != 256)
            throw new InvalidDataException("System Bundle signing requires an ECDSA P-256 private key.");
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var signatureBytes = key.SignData(
            manifest,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var signature = new SystemBundleSignature(
            SystemBundleSignature.CurrentVersion,
            inspection.BundleId,
            inspection.Descriptor.BuildId,
            SystemBundleSignature.CurrentAlgorithm,
            SystemBundleSignature.CurrentSignatureFormat,
            SystemBundleSignature.CurrentSignedPayload,
            signer.Trim(),
            Convert.ToBase64String(publicKey),
            Hash(publicKey),
            Convert.ToBase64String(signatureBytes));
        var path = string.IsNullOrWhiteSpace(outputPath)
            ? DefaultPath(inspection.Path)
            : Path.GetFullPath(outputPath);
        await WriteAtomicAsync(path, signature, cancellationToken).ConfigureAwait(false);
        return (path, signature);
    }

    internal static async Task<SystemBundleAuthenticity> InspectAsync(
        string bundlePath,
        string? signaturePath = null,
        string policy = "permissive",
        IEnumerable<string>? trustedSignerFingerprints = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPolicy = NormalizePolicy(policy);
        var inspection = await SystemBundleContract.VerifyAsync(bundlePath, cancellationToken)
            .ConfigureAwait(false);
        var path = string.IsNullOrWhiteSpace(signaturePath)
            ? DefaultPath(inspection.Path)
            : Path.GetFullPath(signaturePath);
        var trusted = NormalizeTrustedFingerprints(trustedSignerFingerprints);
        if (!File.Exists(path))
            return Result(
                "unsigned", normalizedPolicy, null, null, path,
                "No detached System Bundle signature is present.");

        try
        {
            var file = new FileInfo(path);
            if (file.Length is <= 0 or > MaximumSignatureBytes)
                throw new InvalidDataException("System Bundle signature exceeds its safety limit.");
            var signature = JsonSerializer.Deserialize<SystemBundleSignature>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                JsonOptions)
                ?? throw new InvalidDataException("System Bundle signature is empty.");
            Validate(signature, inspection);
            var publicKey = Convert.FromBase64String(signature.PublicKeySpki);
            var fingerprint = Hash(publicKey);
            if (!fingerprint.Equals(
                    signature.PublicKeyFingerprintSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("System Bundle signer fingerprint does not match its public key.");
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length || verifier.KeySize != 256)
                throw new InvalidDataException("System Bundle signature does not contain an ECDSA P-256 public key.");
            var manifest = await ReadManifestAsync(inspection.Path, cancellationToken)
                .ConfigureAwait(false);
            var signatureBytes = Convert.FromBase64String(signature.Signature);
            if (!verifier.VerifyData(
                    manifest,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("System Bundle signature verification failed.");
            var status = trusted.Contains(fingerprint) ? "trusted" : "verified";
            return Result(
                status,
                normalizedPolicy,
                signature.Signer,
                fingerprint,
                path,
                status == "trusted"
                    ? "Signature is valid and its signer fingerprint is explicitly trusted."
                    : "Signature is valid; its signer is not in the explicit trust set.");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException
                or CryptographicException
                or FormatException
                or ArgumentException)
        {
            return Result(
                "invalid", normalizedPolicy, null, null, path,
                $"System Bundle signature is invalid: {ex.Message}");
        }
    }

    internal static string NormalizePolicy(string? policy) =>
        (policy ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "permissive" => "permissive",
            "signed" => "signed",
            "trusted" => "trusted",
            _ => throw new ArgumentException(
                "System Bundle authenticity policy must be permissive, signed, or trusted.")
        };

    private static SystemBundleAuthenticity Result(
        string status,
        string policy,
        string? signer,
        string? fingerprint,
        string? path,
        string summary)
    {
        var satisfied = status != "invalid" && policy switch
        {
            "permissive" => true,
            "signed" => status is "verified" or "trusted",
            "trusted" => status == "trusted",
            _ => false
        };
        return new SystemBundleAuthenticity(
            status,
            policy,
            satisfied,
            signer,
            fingerprint,
            path,
            summary);
    }

    private static HashSet<string> NormalizeTrustedFingerprints(
        IEnumerable<string>? fingerprints)
    {
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in fingerprints ?? [])
        {
            var fingerprint = value.Trim().ToLowerInvariant();
            ValidateHash(fingerprint, "trusted signer fingerprint");
            trusted.Add(fingerprint);
        }
        return trusted;
    }

    private static void Validate(
        SystemBundleSignature signature,
        SystemBundleInspection inspection)
    {
        if (signature.SchemaVersion != SystemBundleSignature.CurrentVersion
            || signature.Algorithm != SystemBundleSignature.CurrentAlgorithm
            || signature.SignatureFormat != SystemBundleSignature.CurrentSignatureFormat
            || signature.SignedPayload != SystemBundleSignature.CurrentSignedPayload
            || !string.Equals(signature.BundleId, inspection.BundleId, StringComparison.Ordinal)
            || !string.Equals(signature.BuildId, inspection.Descriptor.BuildId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(signature.Signer)
            || signature.Signer.Length > 200)
            throw new InvalidDataException(
                "System Bundle signature contract or signed identity is invalid.");
        ValidateHash(signature.BundleId, "signature bundleId");
        ValidateHash(signature.BuildId, "signature buildId");
        ValidateHash(signature.PublicKeyFingerprintSha256, "signature public key fingerprint");
    }

    private static async Task<byte[]> ReadManifestAsync(
        string bundlePath,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(bundlePath);
        var matches = archive.Entries
            .Where(entry => entry.FullName.Equals(
                SystemBundleSignature.CurrentSignedPayload,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || matches[0].Length is <= 0 or > MaximumManifestBytes)
            throw new InvalidDataException("System Bundle signed manifest is missing or too large.");
        await using var stream = matches[0].Open();
        using var buffer = new MemoryStream((int)matches[0].Length);
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static async Task WriteAtomicAsync(
        string path,
        SystemBundleSignature signature,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("System Bundle signature path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(signature, JsonOptions);
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static void ValidateHash(string? value, string property)
    {
        if (value is null
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"System Bundle {property} must be a 64-character SHA-256.");
    }
}
