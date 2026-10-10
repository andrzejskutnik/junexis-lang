using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed class RegistrySnapshotPackageSource : IPackageSource
{
    private readonly INetworkPackageSourceClient client;
    private readonly Uri snapshotUri;
    private readonly Uri registryBaseUri;
    private readonly CompilerRegistryEventEmitter? events;
    private readonly string correlationId;
    private readonly RegistrySnapshotSignatureVerifier? signatureVerifier;
    private HashSet<string>? packageKeys;
    private RegistrySnapshotDocument? loadedSnapshot;
    private string? loadedSnapshotJson;
    private VerifiedRegistrySnapshotSignature? loadedSignature;

    internal RegistrySnapshotPackageSource(
        Uri snapshotUri,
        INetworkPackageSourceClient client,
        CompilerRegistryEventEmitter? events = null,
        string? correlationId = null,
        RegistrySnapshotSignatureVerifier? signatureVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotUri);
        ArgumentNullException.ThrowIfNull(client);
        if (!snapshotUri.IsAbsoluteUri)
            throw new ArgumentException("Registry snapshot URI must be absolute.", nameof(snapshotUri));
        const string snapshotSuffix = "v1/snapshot";
        if (!snapshotUri.AbsolutePath.EndsWith(snapshotSuffix, StringComparison.Ordinal))
            throw new ArgumentException("Registry snapshot URI must end with 'v1/snapshot'.", nameof(snapshotUri));

        this.snapshotUri = snapshotUri;
        registryBaseUri = new UriBuilder(snapshotUri)
        {
            Path = snapshotUri.AbsolutePath[..^snapshotSuffix.Length],
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
        this.client = client;
        this.events = events;
        this.correlationId = NormalizeCorrelationId(correlationId);
        this.signatureVerifier = signatureVerifier;
    }

    public async Task<PackageIndexDocument> LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        var operation = events is null
            ? null
            : await events.BeginAsync(correlationId, "snapshot-read", artifact: null, cancellationToken);
        RegistrySnapshotDocument snapshot;
        string json;
        try
        {
            json = await client.GetIndexAsync(snapshotUri, cancellationToken);
            snapshot = PackageJson.Deserialize<RegistrySnapshotDocument>(json, snapshotUri.ToString());
            RegistrySnapshotValidator.Validate(snapshot, json);
            if (signatureVerifier is not null)
            {
                var signatureUri = new Uri(registryBaseUri, "v1/snapshot/signature");
                var signatureJson = await client.GetIndexAsync(signatureUri, cancellationToken);
                loadedSignature = signatureVerifier.Verify(snapshot.SnapshotId, signatureJson);
            }
            if (operation is not null)
                await operation.CompleteAsync("succeeded", snapshotId: snapshot.SnapshotId);
        }
        catch (OperationCanceledException)
        {
            if (operation is not null)
                await operation.CompleteAsync("canceled");
            throw;
        }
        catch (Exception ex)
        {
            if (operation is not null)
                await operation.CompleteAsync("failed", EventErrorCode(ex));
            throw;
        }
        loadedSnapshot = snapshot;
        loadedSnapshotJson = json;

        var packages = snapshot.Artifacts
            .OfType<RegistryPackageArtifact>()
            .Select(artifact => new PackageIndexEntry(
                artifact.Id,
                artifact.Version,
                artifact.Namespace,
                "junexis.package.json",
                artifact.ManifestSha256,
                artifact.ArtifactPath,
                artifact.Sha256,
                artifact.Dependencies,
                artifact.Compatibility.Language,
                artifact.Compatibility.LanguageTargets,
                decimal.ToInt64(artifact.SizeBytes)))
            .OrderBy(artifact => artifact.Id, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Version, StringComparer.Ordinal)
            .ToArray();

        var index = new PackageIndexDocument("junexis.package-index/0.1", packages);
        PackageContractValidator.Validate(index);
        packageKeys = packages.Select(PackageKey).ToHashSet(StringComparer.Ordinal);
        return index;
    }

    public async Task CopyArtifactAsync(
        PackageIndexEntry entry,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        if (packageKeys is null || !packageKeys.Contains(PackageKey(entry)))
            throw new InvalidOperationException("Registry snapshot must be loaded before copying its artifact.");

        var contentUri = new Uri(
            registryBaseUri,
            $"v1/artifacts/package/{Uri.EscapeDataString(entry.Id)}/{Uri.EscapeDataString(entry.Version)}/content");
        var operation = events is null
            ? null
            : await events.BeginAsync(
                correlationId,
                "artifact-download",
                new CompilerRegistryEventArtifact("package", entry.Id, entry.Version, entry.ArtifactSha256),
                cancellationToken);
        try
        {
            await client.CopyArtifactAsync(contentUri, destination, cancellationToken);
            if (operation is not null)
                await operation.CompleteAsync("succeeded");
        }
        catch (OperationCanceledException)
        {
            if (operation is not null)
                await operation.CompleteAsync("canceled");
            throw;
        }
        catch (Exception ex)
        {
            if (operation is not null)
                await operation.CompleteAsync("failed", EventErrorCode(ex));
            throw;
        }
    }

    private static string NormalizeCorrelationId(string? value)
    {
        if (value is null)
            return Guid.NewGuid().ToString("D");
        if (!Guid.TryParseExact(value, "D", out var parsed))
            throw new ArgumentException("Correlation ID must be a UUID.", nameof(value));
        return parsed.ToString("D");
    }

    private static string EventErrorCode(Exception exception) => exception switch
    {
        RegistryHttpResponseException response => response.ErrorCode,
        TimeoutException => "registry-timeout",
        InvalidDataException => "invalid-contract",
        _ => "registry-unavailable"
    };

    private static string PackageKey(PackageIndexEntry entry) => $"{entry.Id}@{entry.Version}";

    internal (RegistrySnapshotDocument Snapshot, string Json, VerifiedRegistrySnapshotSignature? Signature) GetLoadedSnapshot()
    {
        if (loadedSnapshot is null || loadedSnapshotJson is null)
            throw new InvalidOperationException("Registry snapshot must be loaded before it can be persisted offline.");
        return (loadedSnapshot, loadedSnapshotJson, loadedSignature);
    }
}

internal sealed record RegistrySnapshotDocument(
    string SchemaVersion,
    string SnapshotId,
    RegistrySnapshotArtifact[] Artifacts);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "artifactType")]
[JsonDerivedType(typeof(RegistryPackageArtifact), "package")]
[JsonDerivedType(typeof(RegistryProviderArtifact), "provider")]
internal abstract record RegistrySnapshotArtifact(
    string Id,
    string Version,
    string ArtifactPath,
    string MediaType,
    decimal SizeBytes,
    string Sha256,
    string ManifestSha256);

internal sealed record RegistryPackageArtifact(
    string Id,
    string Version,
    string ArtifactPath,
    string MediaType,
    decimal SizeBytes,
    string Sha256,
    string ManifestSha256,
    string Namespace,
    PackageDependency[] Dependencies,
    RegistryPackageCompatibility Compatibility,
    string Source) : RegistrySnapshotArtifact(
        Id,
        Version,
        ArtifactPath,
        MediaType,
        SizeBytes,
        Sha256,
        ManifestSha256);

internal sealed record RegistryProviderArtifact(
    string Id,
    string Version,
    string ArtifactPath,
    string MediaType,
    decimal SizeBytes,
    string Sha256,
    string ManifestSha256,
    RegistryProviderCompatibility Compatibility,
    string Contract) : RegistrySnapshotArtifact(
        Id,
        Version,
        ArtifactPath,
        MediaType,
        SizeBytes,
        Sha256,
        ManifestSha256);

internal sealed record RegistryPackageCompatibility(
    PackageLanguageRange Language,
    string[] LanguageTargets);

internal sealed record RegistryProviderCompatibility(
    string PlatformRid,
    string[] SupportedPlatformRids,
    string SdkVersion,
    string AbiVersion,
    string TargetFramework,
    string PlatformVersion,
    string CompatibilitySet);

internal static class RegistrySnapshotValidator
{
    private const int MaximumArtifacts = 10_000;
    private const long MaximumRegistryArtifactSize = 1024L * 1024 * 1024;
    private static readonly Regex ArtifactIdPattern = new("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex MediaTypePattern = new("^[a-z0-9][a-z0-9!#$&^_.+-]*/[a-z0-9][a-z0-9!#$&^_.+-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex RidPattern = new("^[a-z0-9]+-[a-z0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex ContractPattern = new("^[a-z][a-z0-9.-]*/[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex TargetFrameworkPattern = new("^[a-z][a-z0-9]*(?:\\.[0-9]+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex CompatibilitySetPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$", RegexOptions.CultureInvariant);

    internal static void Validate(RegistrySnapshotDocument snapshot, string json)
    {
        if (snapshot.SchemaVersion is null || snapshot.SnapshotId is null || snapshot.Artifacts is null)
            throw InvalidContract("Registry snapshot is missing a required field.");
        if (!snapshot.SchemaVersion.Equals("junexis.registry-snapshot/0.1", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported Registry snapshot schema: {snapshot.SchemaVersion}");
        if (snapshot.Artifacts.Length > MaximumArtifacts)
            throw new InvalidDataException("Registry snapshot exceeds the artifact limit.");

        using var document = JsonDocument.Parse(json);
        var expectedSnapshotId = ComputeSnapshotId(document.RootElement.GetProperty("artifacts"));
        if (!snapshot.SnapshotId.Equals(expectedSnapshotId, StringComparison.Ordinal))
            throw new InvalidDataException("Registry snapshot ID does not match its canonical artifacts.");

        var identities = new HashSet<string>(StringComparer.Ordinal);
        var artifactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in snapshot.Artifacts)
        {
            if (artifact is null ||
                artifact.Id is null ||
                artifact.Version is null ||
                artifact.ArtifactPath is null ||
                artifact.MediaType is null ||
                artifact.Sha256 is null ||
                artifact.ManifestSha256 is null)
                throw InvalidContract("Registry artifact is missing a required field.");
            if (artifact.SizeBytes != decimal.Truncate(artifact.SizeBytes) ||
                artifact.SizeBytes is < 1 or > MaximumRegistryArtifactSize)
                throw new InvalidDataException($"Registry artifact size is invalid: {artifact.Id}@{artifact.Version}");
            if (!identities.Add($"{artifact.GetType().Name}:{artifact.Id}@{artifact.Version}"))
                throw new InvalidDataException($"Duplicate Registry artifact identity: {artifact.Id}@{artifact.Version}");
            if (!artifactPaths.Add(artifact.ArtifactPath))
                throw new InvalidDataException($"Registry artifact path has a case-insensitive alias: {artifact.ArtifactPath}");
            ValidateCommonArtifact(artifact);

            if (artifact is RegistryProviderArtifact provider)
            {
                ValidateProvider(provider);
                continue;
            }
            var package = (RegistryPackageArtifact)artifact;
            if (package.Namespace is null ||
                package.Dependencies is null ||
                package.Dependencies.Any(dependency => dependency is null || dependency.Id is null || dependency.Version is null) ||
                package.Compatibility is null ||
                package.Compatibility.Language is null ||
                package.Compatibility.Language.Minimum is null ||
                package.Compatibility.Language.MaximumExclusive is null ||
                package.Compatibility.LanguageTargets is null ||
                package.Compatibility.LanguageTargets.Any(target => target is null) ||
                package.Source is null)
                throw InvalidContract($"Registry package is missing a required field: {package.Id}@{package.Version}");
            if (!package.Source.Equals($"registry:{package.Id}@{package.Version}", StringComparison.Ordinal))
                throw new InvalidDataException($"Registry package source is inconsistent: {package.Id}@{package.Version}");
            if (package.SizeBytes > PackageContractValidator.MaximumPackageSizeBytes)
                throw new InvalidDataException($"Registry package exceeds the compiler size limit: {package.Id}@{package.Version}");
            if (package.Dependencies.Length > 1_000 ||
                package.Dependencies.Select(item => $"{item.Id}@{item.Version}").Distinct(StringComparer.Ordinal).Count() != package.Dependencies.Length)
                throw new InvalidDataException($"Registry package dependencies are invalid: {package.Id}@{package.Version}");
            foreach (var dependency in package.Dependencies)
                _ = PackageReference.Parse($"{dependency.Id}@{dependency.Version}");
            _ = PackageReference.Parse($"language@{package.Compatibility.Language.Minimum}");
            _ = PackageReference.Parse($"language@{package.Compatibility.Language.MaximumExclusive}");
            if (package.Compatibility.LanguageTargets.Length == 0 ||
                package.Compatibility.LanguageTargets.Any(target => target is not ("embedded" or "server" or "report")) ||
                package.Compatibility.LanguageTargets.Distinct(StringComparer.Ordinal).Count() != package.Compatibility.LanguageTargets.Length)
                throw new InvalidDataException($"Registry package language targets are invalid: {package.Id}@{package.Version}");
        }
    }

    private static void ValidateCommonArtifact(RegistrySnapshotArtifact artifact)
    {
        if (!ArtifactIdPattern.IsMatch(artifact.Id))
            throw new InvalidDataException($"Registry artifact ID is invalid: {artifact.Id}");
        _ = PackageReference.Parse($"{artifact.Id}@{artifact.Version}");
        PackageContractValidator.ValidateRelativePath(artifact.ArtifactPath, "Registry artifact");
        if (artifact.MediaType.Length > 127 || !MediaTypePattern.IsMatch(artifact.MediaType))
            throw new InvalidDataException($"Registry artifact media type is invalid: {artifact.MediaType}");
        if (!Sha256Pattern.IsMatch(artifact.Sha256) || !Sha256Pattern.IsMatch(artifact.ManifestSha256))
            throw new InvalidDataException($"Registry artifact hash is invalid: {artifact.Id}@{artifact.Version}");
    }

    private static void ValidateProvider(RegistryProviderArtifact provider)
    {
        var compatibility = provider.Compatibility;
        if (provider.Contract is null ||
            compatibility is null ||
            compatibility.PlatformRid is null ||
            compatibility.SupportedPlatformRids is null ||
            compatibility.SupportedPlatformRids.Any(rid => rid is null) ||
            compatibility.SdkVersion is null ||
            compatibility.AbiVersion is null ||
            compatibility.TargetFramework is null ||
            compatibility.PlatformVersion is null ||
            compatibility.CompatibilitySet is null)
            throw InvalidContract($"Registry provider is missing a required field: {provider.Id}@{provider.Version}");
        if (!ContractPattern.IsMatch(provider.Contract) ||
            !RidPattern.IsMatch(compatibility.PlatformRid) ||
            compatibility.SupportedPlatformRids.Length is < 1 or > 64 ||
            compatibility.SupportedPlatformRids.Any(rid => !RidPattern.IsMatch(rid)) ||
            compatibility.SupportedPlatformRids.Distinct(StringComparer.Ordinal).Count() != compatibility.SupportedPlatformRids.Length ||
            !compatibility.SupportedPlatformRids.Contains(compatibility.PlatformRid, StringComparer.Ordinal) ||
            !ContractPattern.IsMatch(compatibility.AbiVersion) ||
            !TargetFrameworkPattern.IsMatch(compatibility.TargetFramework) ||
            !CompatibilitySetPattern.IsMatch(compatibility.CompatibilitySet))
            throw new InvalidDataException($"Registry provider compatibility is invalid: {provider.Id}@{provider.Version}");
        _ = PackageReference.Parse($"sdk@{compatibility.SdkVersion}");
        _ = PackageReference.Parse($"platform@{compatibility.PlatformVersion}");
    }

    private static InvalidDataException InvalidContract(string message) => new(message);

    internal static string ComputeSnapshotId(JsonElement artifacts)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(writer, artifacts);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                var number = element.GetDecimal();
                var canonical = number == decimal.Truncate(number)
                    ? decimal.Truncate(number).ToString("0", CultureInfo.InvariantCulture)
                    : number.ToString("G29", CultureInfo.InvariantCulture);
                writer.WriteRawValue(canonical, skipInputValidation: true);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
