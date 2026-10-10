using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record RevocationEntry(
    string SchemaVersion,
    string Kind,
    RevocationTarget Target,
    string Reason,
    string RevokedAt);

internal sealed record RevocationTarget(
    string Type,
    string Id,
    string? Version,
    long? Revision);

internal sealed class RegistryRevocationStore
{
    private readonly List<RevocationEntry> revocations = [];

    internal static RegistryRevocationStore Load(string? trustStorePath, string? searchDirectory = null)
    {
        var store = new RegistryRevocationStore();
        store.LoadFromDirectories(trustStorePath, searchDirectory);
        return store;
    }

    internal void CheckPackageRevoked(string id, string version)
    {
        var match = revocations.FirstOrDefault(r =>
            string.Equals(r.Target.Type, "package", StringComparison.Ordinal) &&
            string.Equals(r.Target.Id, id, StringComparison.Ordinal) &&
            string.Equals(r.Target.Version, version, StringComparison.Ordinal));

        if (match is not null)
        {
            throw new InvalidDataException(
                $"Package '{id}@{version}' has been revoked (kind: {match.Kind}, reason: '{match.Reason}'). Project configuration and lockfiles were preserved without changes.");
        }
    }

    internal void CheckProviderRevoked(string id, string version)
    {
        var match = revocations.FirstOrDefault(r =>
            string.Equals(r.Target.Type, "provider", StringComparison.Ordinal) &&
            string.Equals(r.Target.Id, id, StringComparison.Ordinal) &&
            string.Equals(r.Target.Version, version, StringComparison.Ordinal));

        if (match is not null)
        {
            throw new InvalidDataException(
                $"Provider '{id}@{version}' has been revoked (kind: {match.Kind}, reason: '{match.Reason}').");
        }
    }

    internal void CheckSnapshotRevisionRevoked(string registryId, long revision)
    {
        var match = revocations.FirstOrDefault(r =>
            string.Equals(r.Target.Type, "registry-snapshot-revision", StringComparison.Ordinal) &&
            string.Equals(r.Target.Id, registryId, StringComparison.Ordinal) &&
            r.Target.Revision == revision);

        if (match is not null)
        {
            throw new InvalidDataException(
                $"Registry snapshot revision {revision} for '{registryId}' has been revoked (kind: {match.Kind}, reason: '{match.Reason}').");
        }
    }

    internal void LoadFromDirectories(string? trustStorePath, string? searchDirectory)
    {
        var directoriesToSearch = new List<string>();
        if (!string.IsNullOrWhiteSpace(searchDirectory) && Directory.Exists(searchDirectory))
        {
            directoriesToSearch.Add(searchDirectory);
            var parent1 = Path.GetDirectoryName(searchDirectory);
            if (!string.IsNullOrWhiteSpace(parent1) && Directory.Exists(parent1))
            {
                directoriesToSearch.Add(parent1);
                var parent2 = Path.GetDirectoryName(parent1);
                if (!string.IsNullOrWhiteSpace(parent2) && Directory.Exists(parent2))
                    directoriesToSearch.Add(parent2);
            }
        }

        if (!string.IsNullOrWhiteSpace(trustStorePath) && File.Exists(trustStorePath))
        {
            var trustDir = Path.GetDirectoryName(Path.GetFullPath(trustStorePath));
            if (!string.IsNullOrWhiteSpace(trustDir) && Directory.Exists(trustDir))
            {
                directoriesToSearch.Add(trustDir);
            }
        }

        var expandedDirs = new List<string>();
        foreach (var dir in directoriesToSearch.Distinct(StringComparer.Ordinal))
        {
            if (Directory.Exists(dir))
            {
                expandedDirs.Add(dir);
                var subRev = Path.Combine(dir, "revocations");
                if (Directory.Exists(subRev)) expandedDirs.Add(subRev);
                var subJunexis = Path.Combine(dir, ".junexis");
                if (Directory.Exists(subJunexis))
                {
                    expandedDirs.Add(subJunexis);
                    var subJunexisRev = Path.Combine(subJunexis, "revocations");
                    if (Directory.Exists(subJunexisRev)) expandedDirs.Add(subJunexisRev);
                }
            }
        }

        foreach (var dir in expandedDirs.Distinct(StringComparer.Ordinal))
        {
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                TryLoadRevocationFile(file, trustStorePath);
            }
        }
    }

    private void TryLoadRevocationFile(string file, string? trustStorePath)
    {
        try
        {
            var text = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("schemaVersion", out var schema) &&
                schema.GetString() == "junexis.registry-revocation/0.1")
            {
                if (!string.IsNullOrWhiteSpace(trustStorePath) && File.Exists(trustStorePath))
                {
                    VerifySignature(root, trustStorePath);
                }

                var kind = root.GetProperty("kind").GetString()!;
                var target = root.GetProperty("target");
                var type = target.GetProperty("type").GetString()!;
                var id = target.GetProperty("id").GetString()!;
                string? version = target.TryGetProperty("version", out var v) ? v.GetString() : null;
                long? revision = target.TryGetProperty("revision", out var r) ? r.GetInt64() : null;
                var reason = root.GetProperty("reason").GetString()!;
                var revokedAt = root.GetProperty("revokedAt").GetString()!;

                revocations.Add(new RevocationEntry(
                    "junexis.registry-revocation/0.1",
                    kind,
                    new RevocationTarget(type, id, version, revision),
                    reason,
                    revokedAt));
            }
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            // Ignore non-revocation JSON files or invalid files
        }
    }

    private static void VerifySignature(JsonElement root, string trustStorePath)
    {
        var keyId = root.GetProperty("keyId").GetString();
        var kind = root.GetProperty("kind").GetString();
        using var trustDoc = JsonDocument.Parse(File.ReadAllText(trustStorePath));
        var trust = trustDoc.RootElement;
        var delegation = trust.GetProperty("delegations").EnumerateArray().SingleOrDefault(item =>
            item.GetProperty("role").GetString() == "revocation" &&
            item.GetProperty("keyId").GetString() == keyId);

        if (delegation.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"Revocation record is signed by untrusted keyId '{keyId}'.");

        var kinds = delegation.GetProperty("scope").GetProperty("kinds").EnumerateArray()
            .Select(k => k.GetString()!).ToArray();
        if (!kinds.Contains(kind, StringComparer.Ordinal))
            throw new InvalidDataException($"Revocation record kind '{kind}' is outside delegation scope.");

        var publicKey = Convert.FromBase64String(delegation.GetProperty("publicKeySpki").GetString()!);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);

        var signatureBytes = Convert.FromBase64String(root.GetProperty("signature").GetString()!);
        var node = JsonNode.Parse(root.GetRawText())!.AsObject();
        node.Remove("signature");
        var payload = Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));

        if (!ecdsa.VerifyData(payload, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new InvalidDataException("Revocation record signature is invalid.");
    }
}
