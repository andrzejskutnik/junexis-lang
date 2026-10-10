using System.Text.RegularExpressions;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record PlatformReleaseCatalog(
    string SchemaVersion,
    string Channel,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    PlatformReleaseEntry[] Entries,
    string Algorithm,
    string KeyId,
    string Signature);

internal sealed record PlatformReleaseEntry(
    string Component,
    string Version,
    string Rid,
    string System,
    string Architecture,
    PlatformArtifact Artifact,
    PlatformSystemRequirements SystemRequirements,
    PlatformSystemSignature SystemSignature,
    string ProvenanceSha256,
    string SbomSha256,
    string MinimumSupportedVersion,
    bool Revoked);

internal sealed record PlatformArtifact(string Format, string Url, long SizeBytes, string Sha256);

internal sealed record PlatformSystemRequirements(
    string MinimumOsVersion,
    PlatformNativeDependency[] NativeDependencies);

internal sealed record PlatformNativeDependency(string Id, string Version, string Abi);

internal sealed record PlatformSystemSignature(
    string Kind,
    string State,
    string? Publisher,
    string? SignatureSha256);

internal sealed record PlatformArtifactSelection(
    string Component,
    string Version,
    string Rid,
    PlatformArtifact Artifact,
    PlatformSystemRequirements SystemRequirements,
    PlatformSystemSignature SystemSignature,
    string ProvenanceSha256,
    string SbomSha256);

internal static partial class PlatformVariantSelector
{
    private static readonly IReadOnlyDictionary<string, (string System, string Architecture, string Signature)> RidContract =
        new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
        {
            ["win-x64"] = ("windows", "x64", "authenticode"),
            ["linux-x64"] = ("linux", "x64", "linux-package"),
            ["osx-arm64"] = ("macos", "arm64", "apple-developer-id")
        };
    private static readonly IReadOnlyDictionary<string, HashSet<string>> AcceptedFormats =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["win-x64"] = new(["zip", "vsix", "nupkg"], StringComparer.Ordinal),
            ["linux-x64"] = new(["tar.gz", "vsix", "nupkg"], StringComparer.Ordinal),
            ["osx-arm64"] = new(["pkg", "tar.gz", "vsix", "nupkg"], StringComparer.Ordinal)
        };

    internal static PlatformReleaseCatalog ParseCatalog(string json, string source)
    {
        var catalog = PackageJson.Deserialize<PlatformReleaseCatalog>(json, source);
        ValidateCatalog(catalog);
        return catalog;
    }

    internal static IReadOnlyList<PlatformArtifactSelection> SelectLockedPackages(
        PackagesLockDocument lockDocument,
        PlatformReleaseCatalog catalog,
        string rid)
    {
        PackageContractValidator.Validate(lockDocument);
        ValidateCatalog(catalog);
        if (!RidContract.ContainsKey(rid))
            throw new InvalidDataException($"Unsupported platform RID: {rid}");

        var selections = new List<PlatformArtifactSelection>(lockDocument.Packages.Length);
        foreach (var package in lockDocument.Packages
                     .OrderBy(item => item.Id, StringComparer.Ordinal)
                     .ThenBy(item => item.Version, StringComparer.Ordinal))
        {
            var matches = catalog.Entries.Where(entry =>
                entry.Component.Equals(package.Id, StringComparison.Ordinal) &&
                entry.Version.Equals(package.Version, StringComparison.Ordinal) &&
                entry.Rid.Equals(rid, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
                throw new InvalidDataException(
                    $"Package '{package.Id}@{package.Version}' has no artifact for RID '{rid}'. No cross-RID fallback is allowed.");
            if (matches.Length > 1)
                throw new InvalidDataException(
                    $"Package '{package.Id}@{package.Version}' has more than one artifact for RID '{rid}'.");

            var selected = matches[0];
            if (selected.Revoked)
                throw new InvalidDataException(
                    $"Package artifact is revoked: {package.Id}@{package.Version} ({rid}).");
            if (!selected.SystemSignature.State.Equals("verified", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Package artifact is not system-verified: {package.Id}@{package.Version} ({rid}).");
            selections.Add(new PlatformArtifactSelection(
                package.Id,
                package.Version,
                rid,
                selected.Artifact,
                selected.SystemRequirements,
                selected.SystemSignature,
                selected.ProvenanceSha256,
                selected.SbomSha256));
        }
        return selections;
    }

    private static void ValidateCatalog(PlatformReleaseCatalog catalog)
    {
        if (!catalog.SchemaVersion.Equals("junexis.platform-release-catalog/0.2", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported platform release catalog: {catalog.SchemaVersion}");
        if (catalog.Channel is not ("dev" or "preview" or "stable"))
            throw new InvalidDataException($"Unsupported release channel: {catalog.Channel}");
        if (catalog.ExpiresAt <= catalog.IssuedAt)
            throw new InvalidDataException("Platform release catalog has an invalid validity window.");
        if (!catalog.Algorithm.Equals("ecdsa-p256-sha256-der", StringComparison.Ordinal) ||
            !KeyIdPattern().IsMatch(catalog.KeyId) || string.IsNullOrWhiteSpace(catalog.Signature))
            throw new InvalidDataException("Platform release catalog has an invalid signature envelope.");
        if (catalog.Entries is null || catalog.Entries.Length == 0)
            throw new InvalidDataException("Platform release catalog is empty.");

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog.Entries)
        {
            if (entry is null || entry.Artifact is null || entry.SystemRequirements is null ||
                entry.SystemSignature is null)
                throw new InvalidDataException("Platform release catalog contains an incomplete entry.");
            _ = PackageReference.Parse($"{entry.Component}@{entry.Version}");
            if (!RidContract.TryGetValue(entry.Rid, out var contract))
                throw new InvalidDataException($"Unsupported platform RID: {entry.Rid}");
            if (!entry.System.Equals(contract.System, StringComparison.Ordinal) ||
                !entry.Architecture.Equals(contract.Architecture, StringComparison.Ordinal) ||
                !entry.SystemSignature.Kind.Equals(contract.Signature, StringComparison.Ordinal))
                throw new InvalidDataException($"Platform fields do not match RID '{entry.Rid}'.");
            if (!identities.Add($"{entry.Component}@{entry.Version}|{entry.Rid}"))
                throw new InvalidDataException(
                    $"Duplicate platform artifact: {entry.Component}@{entry.Version} ({entry.Rid}).");
            if (!Uri.TryCreate(entry.Artifact.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                entry.Artifact.SizeBytes <= 0 || !Sha256Pattern().IsMatch(entry.Artifact.Sha256) ||
                !AcceptedFormats[entry.Rid].Contains(entry.Artifact.Format))
                throw new InvalidDataException($"Invalid platform artifact: {entry.Component}@{entry.Version} ({entry.Rid}).");
            if (!Sha256Pattern().IsMatch(entry.ProvenanceSha256) || !Sha256Pattern().IsMatch(entry.SbomSha256))
                throw new InvalidDataException($"Platform artifact has invalid evidence hashes: {entry.Component}@{entry.Version}.");
            if (entry.SystemSignature.State is not ("required" or "verified"))
                throw new InvalidDataException($"Platform artifact has an invalid signature state: {entry.Component}@{entry.Version} ({entry.Rid}).");
            if (entry.SystemSignature.State.Equals("verified", StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(entry.SystemSignature.Publisher) ||
                 entry.SystemSignature.SignatureSha256 is null ||
                 !Sha256Pattern().IsMatch(entry.SystemSignature.SignatureSha256)))
                throw new InvalidDataException($"Platform artifact has incomplete signature evidence: {entry.Component}@{entry.Version} ({entry.Rid}).");
            _ = PackageReference.Parse($"minimum@{entry.MinimumSupportedVersion}");
            if (string.IsNullOrWhiteSpace(entry.SystemRequirements.MinimumOsVersion) ||
                entry.SystemRequirements.NativeDependencies is null)
                throw new InvalidDataException($"Platform artifact has incomplete system requirements: {entry.Component}@{entry.Version}.");
            var nativeIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in entry.SystemRequirements.NativeDependencies)
            {
                if (dependency is null || string.IsNullOrWhiteSpace(dependency.Id) || string.IsNullOrWhiteSpace(dependency.Version) ||
                    string.IsNullOrWhiteSpace(dependency.Abi) || !nativeIdentities.Add(dependency.Id))
                    throw new InvalidDataException($"Platform artifact has invalid native dependencies: {entry.Component}@{entry.Version}.");
            }
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyIdPattern();
}
