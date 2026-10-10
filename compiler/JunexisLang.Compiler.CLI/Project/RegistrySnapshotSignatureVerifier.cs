using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record VerifiedRegistrySnapshotSignature(
    string Json,
    string RegistryId,
    string SnapshotId,
    long Revision,
    long MinimumAcceptedRevision);

internal sealed class RegistrySnapshotSignatureVerifier
{
    private readonly string trustStorePath;
    private readonly string previousSignaturePath;
    private readonly TimeProvider timeProvider;

    internal RegistrySnapshotSignatureVerifier(
        string trustStorePath,
        string previousSignaturePath,
        TimeProvider? timeProvider = null)
    {
        this.trustStorePath = Path.GetFullPath(trustStorePath);
        this.previousSignaturePath = Path.GetFullPath(previousSignaturePath);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal static RegistrySnapshotSignatureVerifier FromEnvironment(string packagesRoot)
    {
        var trustStorePath = Environment.GetEnvironmentVariable("JUNEXIS_REGISTRY_TRUST_STORE");
        if (string.IsNullOrWhiteSpace(trustStorePath))
            throw new InvalidDataException("JUNEXIS_REGISTRY_TRUST_STORE must point to junexis.trust-store/0.1 for Registry restore.");
        return new RegistrySnapshotSignatureVerifier(
            trustStorePath,
            Path.Combine(packagesRoot, RegistryOfflineSnapshot.SignatureFileName));
    }

    internal VerifiedRegistrySnapshotSignature Verify(string snapshotId, string signatureJson)
    {
        using var signatureDocument = JsonDocument.Parse(signatureJson);
        var signature = signatureDocument.RootElement;
        RequireExactProperties(signature,
        [
            "schemaVersion", "registryId", "snapshotId", "revision", "minimumAcceptedRevision",
            "issuedAt", "expiresAt", "algorithm", "keyId", "signature"
        ]);
        if (signature.GetProperty("schemaVersion").GetString() != "junexis.registry-snapshot-signature/0.1"
            || signature.GetProperty("snapshotId").GetString() != snapshotId
            || signature.GetProperty("algorithm").GetString() != "ecdsa-p256-sha256-der")
            throw new InvalidDataException("Registry snapshot signature does not match the snapshot contract.");

        var registryId = signature.GetProperty("registryId").GetString()!;
        var revision = signature.GetProperty("revision").GetInt64();
        var minimumRevision = signature.GetProperty("minimumAcceptedRevision").GetInt64();
        if (revision < 1 || minimumRevision < 1 || revision < minimumRevision)
            throw new InvalidDataException("Registry snapshot signature revision is invalid.");

        var issuedAt = ParseTimestamp(signature.GetProperty("issuedAt").GetString()!, "issuedAt");
        var expiresAt = ParseTimestamp(signature.GetProperty("expiresAt").GetString()!, "expiresAt");
        var now = timeProvider.GetUtcNow();
        if (issuedAt > now || expiresAt <= now || issuedAt >= expiresAt)
            throw new InvalidDataException("Registry snapshot signature is not currently valid.");

        using var trustDocument = JsonDocument.Parse(File.ReadAllText(trustStorePath));
        var trust = trustDocument.RootElement;
        if (trust.GetProperty("schemaVersion").GetString() != "junexis.trust-store/0.1")
            throw new InvalidDataException("Registry trust store contract is unsupported.");
        var keyId = signature.GetProperty("keyId").GetString();
        var delegation = trust.GetProperty("delegations").EnumerateArray().SingleOrDefault(item =>
            item.GetProperty("role").GetString() == "registry-snapshot"
            && item.GetProperty("keyId").GetString() == keyId);
        if (delegation.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Registry snapshot signing key is not trusted for the registry-snapshot role.");
        if (delegation.GetProperty("algorithm").GetString() != "ecdsa-p256-sha256-der")
            throw new InvalidDataException("Registry snapshot signing role uses an unsupported algorithm.");
        if (!delegation.GetProperty("scope").GetProperty("registryIds").EnumerateArray()
            .Any(item => item.GetString() == registryId))
            throw new InvalidDataException("Registry snapshot signing key scope does not include this Registry.");
        if (ParseTimestamp(delegation.GetProperty("notBefore").GetString()!, "notBefore") > now
            || ParseTimestamp(delegation.GetProperty("notAfter").GetString()!, "notAfter") <= now)
            throw new InvalidDataException("Registry snapshot signing role is not currently valid.");

        var publicKey = Convert.FromBase64String(delegation.GetProperty("publicKeySpki").GetString()!);
        var expectedKeyId = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(publicKey));
        if (!string.Equals(keyId, expectedKeyId, StringComparison.Ordinal))
            throw new InvalidDataException("Registry snapshot signing keyId does not match its public key.");
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var read);
        if (read != publicKey.Length || ecdsa.KeySize != 256)
            throw new InvalidDataException("Registry snapshot signing key is not ECDSA P-256 SPKI.");
        var signatureBytes = Convert.FromBase64String(signature.GetProperty("signature").GetString()!);
        if (!ecdsa.VerifyData(CreatePayload(signature), signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new InvalidDataException("Registry snapshot signature is invalid.");

        var highestAcceptedRevision = ReadPreviousRevision();
        if (revision < highestAcceptedRevision || minimumRevision < highestAcceptedRevision)
            throw new InvalidDataException("Registry snapshot signature attempts to replay an older revision.");

        return new VerifiedRegistrySnapshotSignature(signatureJson, registryId, snapshotId, revision, minimumRevision);
    }

    private long ReadPreviousRevision()
    {
        if (!File.Exists(previousSignaturePath))
            return 0;
        using var document = JsonDocument.Parse(File.ReadAllText(previousSignaturePath));
        return document.RootElement.GetProperty("revision").GetInt64();
    }

    private static byte[] CreatePayload(JsonElement signature)
    {
        var payload = new
        {
            schemaVersion = signature.GetProperty("schemaVersion").GetString(),
            registryId = signature.GetProperty("registryId").GetString(),
            snapshotId = signature.GetProperty("snapshotId").GetString(),
            revision = signature.GetProperty("revision").GetInt64(),
            minimumAcceptedRevision = signature.GetProperty("minimumAcceptedRevision").GetInt64(),
            issuedAt = signature.GetProperty("issuedAt").GetString(),
            expiresAt = signature.GetProperty("expiresAt").GetString(),
            algorithm = signature.GetProperty("algorithm").GetString(),
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static DateTimeOffset ParseTimestamp(string value, string field) =>
        DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : throw new InvalidDataException($"Registry snapshot signature {field} is invalid.");

    private static void RequireExactProperties(JsonElement value, string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Registry snapshot signature has an invalid shape.");
    }
}
