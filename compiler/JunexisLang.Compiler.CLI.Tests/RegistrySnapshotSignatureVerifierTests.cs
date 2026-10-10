using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class RegistrySnapshotSignatureVerifierTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"junexis-m18-4-2-{Guid.NewGuid():N}");
    private readonly ECDsa roleKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly DateTimeOffset now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verify_AcceptsTrustedRegistryRoleSignature()
    {
        var verifier = CreateVerifier("registry.junexis.dev");
        var json = CreateSignature("registry.junexis.dev", revision: 7, now.AddMinutes(-1), now.AddMinutes(29));

        var result = verifier.Verify(new string('a', 64), json);

        Assert.Equal(7, result.Revision);
        Assert.Equal("registry.junexis.dev", result.RegistryId);
    }

    [Fact]
    public void Verify_RejectsRegistryOutsideDelegatedScope()
    {
        var verifier = CreateVerifier("registry.other.example");
        var json = CreateSignature("registry.junexis.dev", revision: 7, now.AddMinutes(-1), now.AddMinutes(29));

        Assert.Throws<InvalidDataException>(() => verifier.Verify(new string('a', 64), json));
    }

    [Fact]
    public void Verify_RejectsExpiredSignature()
    {
        var verifier = CreateVerifier("registry.junexis.dev");
        var json = CreateSignature("registry.junexis.dev", revision: 7, now.AddMinutes(-30), now.AddMinutes(-1));

        Assert.Throws<InvalidDataException>(() => verifier.Verify(new string('a', 64), json));
    }

    [Fact]
    public void Verify_RejectsRevisionBelowPersistedHighestRevision()
    {
        var verifier = CreateVerifier("registry.junexis.dev", previousRevision: 8);
        var json = CreateSignature("registry.junexis.dev", revision: 7, now.AddMinutes(-1), now.AddMinutes(29));

        Assert.Throws<InvalidDataException>(() => verifier.Verify(new string('a', 64), json));
    }

    [Fact]
    public void Verify_RejectsSignatureProducedByForeignKey()
    {
        var verifier = CreateVerifier("registry.junexis.dev");
        using var foreignKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = CreateSignature(
            "registry.junexis.dev",
            revision: 7,
            now.AddMinutes(-1),
            now.AddMinutes(29),
            signingKey: foreignKey);

        Assert.Throws<InvalidDataException>(() => verifier.Verify(new string('a', 64), json));
    }

    [Fact]
    public void Verify_RejectsTamperedSignedRevision()
    {
        var verifier = CreateVerifier("registry.junexis.dev");
        var json = CreateSignature("registry.junexis.dev", 7, now.AddMinutes(-1), now.AddMinutes(29));
        var tampered = json.Replace("\"revision\":7", "\"revision\":8", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => verifier.Verify(new string('a', 64), tampered));
    }

    [Fact]
    public void Verify_PreviouslyAuthenticatedSignatureWorksOfflineFromLocalState()
    {
        var (verifier, trustPath, previousPath) = CreateVerifierWithPaths("registry.junexis.dev");
        var json = CreateSignature("registry.junexis.dev", 7, now.AddMinutes(-1), now.AddMinutes(29));
        var verified = verifier.Verify(new string('a', 64), json);
        File.WriteAllText(previousPath, verified.Json);

        var offlineVerifier = new RegistrySnapshotSignatureVerifier(
            trustPath,
            previousPath,
            new FixedTimeProvider(now));

        Assert.Equal(7, offlineVerifier.Verify(new string('a', 64), File.ReadAllText(previousPath)).Revision);
    }

    private RegistrySnapshotSignatureVerifier CreateVerifier(string scopeRegistryId, long? previousRevision = null)
    {
        var (verifier, _, _) = CreateVerifierWithPaths(scopeRegistryId, previousRevision);
        return verifier;
    }

    private (RegistrySnapshotSignatureVerifier Verifier, string TrustPath, string PreviousPath) CreateVerifierWithPaths(
        string scopeRegistryId,
        long? previousRevision = null)
    {
        Directory.CreateDirectory(root);
        var spki = roleKey.ExportSubjectPublicKeyInfo();
        var keyId = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(spki));
        var trustPath = Path.Combine(root, $"trust-{Guid.NewGuid():N}.json");
        File.WriteAllText(trustPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.trust-store/0.1",
            delegations = new[]
            {
                new
                {
                    role = "registry-snapshot",
                    keyId,
                    algorithm = "ecdsa-p256-sha256-der",
                    publicKeySpki = Convert.ToBase64String(spki),
                    notBefore = now.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    notAfter = now.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    scope = new { registryIds = new[] { scopeRegistryId } }
                }
            }
        }));
        var previousPath = Path.Combine(root, $"previous-{Guid.NewGuid():N}.json");
        if (previousRevision is not null)
            File.WriteAllText(previousPath, JsonSerializer.Serialize(new { revision = previousRevision.Value }));
        return (
            new RegistrySnapshotSignatureVerifier(trustPath, previousPath, new FixedTimeProvider(now)),
            trustPath,
            previousPath);
    }

    private string CreateSignature(
        string registryId,
        long revision,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        ECDsa? signingKey = null)
    {
        signingKey ??= roleKey;
        var snapshotId = new string('a', 64);
        var unsigned = new
        {
            schemaVersion = "junexis.registry-snapshot-signature/0.1",
            registryId,
            snapshotId,
            revision,
            minimumAcceptedRevision = revision,
            issuedAt = issuedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            expiresAt = expiresAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            algorithm = "ecdsa-p256-sha256-der"
        };
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(unsigned, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var signature = signingKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var keyId = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(roleKey.ExportSubjectPublicKeyInfo()));
        return JsonSerializer.Serialize(new
        {
            unsigned.schemaVersion,
            unsigned.registryId,
            unsigned.snapshotId,
            unsigned.revision,
            unsigned.minimumAcceptedRevision,
            unsigned.issuedAt,
            unsigned.expiresAt,
            unsigned.algorithm,
            keyId,
            signature = Convert.ToBase64String(signature)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public void Dispose()
    {
        roleKey.Dispose();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
