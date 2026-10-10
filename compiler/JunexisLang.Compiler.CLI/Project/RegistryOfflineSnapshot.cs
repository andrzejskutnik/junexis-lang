using System.Text.Json;
using System.Text.RegularExpressions;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record RegistryOfflineSnapshotDocument(
    string SchemaVersion,
    RegistryOfflineSnapshotSource SourceSnapshot,
    RegistryOfflineCachedArtifact[] CachedArtifacts);

internal sealed record RegistryOfflineSnapshotSource(string SchemaVersion, string SnapshotId);

internal sealed record RegistryOfflineCachedArtifact(string Sha256, long SizeBytes);

internal static class RegistryOfflineSnapshot
{
    internal const string RegistryFileName = "registry-snapshot.json";
    internal const string SignatureFileName = "registry-snapshot-signature.json";
    internal const string OfflineFileName = "registry-offline-snapshot.json";
    private const string OfflineSchema = "junexis.registry-offline-snapshot/0.1";
    private const string RegistrySchema = "junexis.registry-snapshot/0.1";
    private const int MaximumArtifacts = 10_000;
    private const long MaximumArtifactSize = 1024L * 1024 * 1024;
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    internal static async Task PersistAsync(
        string stagingRoot,
        RegistrySnapshotPackageSource source,
        IReadOnlyList<PackageLockEntry> packages)
    {
        var (snapshot, snapshotJson, signature) = source.GetLoadedSnapshot();
        var receipts = packages
            .Select(package =>
            {
                var archive = Path.Combine(
                    PackageRestoreResolver.GetCacheRoot(stagingRoot, package.Id, package.Version),
                    "package.zip");
                return new RegistryOfflineCachedArtifact(
                    package.ArtifactSha256,
                    new FileInfo(archive).Length);
            })
            .OrderBy(receipt => receipt.Sha256, StringComparer.Ordinal)
            .ToArray();
        var offline = new RegistryOfflineSnapshotDocument(
            OfflineSchema,
            new RegistryOfflineSnapshotSource(snapshot.SchemaVersion, snapshot.SnapshotId),
            receipts);
        Validate(offline, snapshot, packages);

        await File.WriteAllTextAsync(Path.Combine(stagingRoot, RegistryFileName), snapshotJson);
        if (signature is not null)
            await File.WriteAllTextAsync(Path.Combine(stagingRoot, SignatureFileName), signature.Json);
        await File.WriteAllTextAsync(
            Path.Combine(stagingRoot, OfflineFileName),
            JsonSerializer.Serialize(offline, PackageJson.Options) + "\n");
    }

    internal static async Task ValidateAsync(
        JunexisProject project,
        IReadOnlyList<PackageLockEntry> packages)
    {
        var packagesRoot = PackageRestoreResolver.GetPackagesRoot(project);
        var registryPath = Path.Combine(packagesRoot, RegistryFileName);
        var offlinePath = Path.Combine(packagesRoot, OfflineFileName);
        var hasRegistry = File.Exists(registryPath);
        var hasOffline = File.Exists(offlinePath);
        if (!hasRegistry && !hasOffline)
            return;
        if (!hasRegistry || !hasOffline)
            throw new InvalidDataException("Lokalny snapshot Registry jest niekompletny. Wykonaj 'junexis restore --registry <adres>'.");

        var registryJson = await PackageJson.ReadTextAsync(registryPath);
        var snapshot = PackageJson.Deserialize<RegistrySnapshotDocument>(registryJson, registryPath);
        RegistrySnapshotValidator.Validate(snapshot, registryJson);
        var offline = PackageJson.Deserialize<RegistryOfflineSnapshotDocument>(
            await PackageJson.ReadTextAsync(offlinePath),
            offlinePath);
        Validate(offline, snapshot, packages);
    }

    private static void Validate(
        RegistryOfflineSnapshotDocument offline,
        RegistrySnapshotDocument snapshot,
        IReadOnlyList<PackageLockEntry> packages)
    {
        if (offline.SchemaVersion is null || offline.SourceSnapshot is null || offline.CachedArtifacts is null)
            throw new InvalidDataException("Lokalny snapshot Registry nie zawiera wymaganych pól.");
        if (!offline.SchemaVersion.Equals(OfflineSchema, StringComparison.Ordinal))
            throw new InvalidDataException($"Nieobsługiwana wersja lokalnego snapshotu Registry: {offline.SchemaVersion}");
        if (offline.SourceSnapshot.SchemaVersion is null || offline.SourceSnapshot.SnapshotId is null ||
            !offline.SourceSnapshot.SchemaVersion.Equals(RegistrySchema, StringComparison.Ordinal) ||
            !offline.SourceSnapshot.SnapshotId.Equals(snapshot.SnapshotId, StringComparison.Ordinal))
            throw new InvalidDataException("Lokalny snapshot wskazuje inny snapshot Registry.");
        if (offline.CachedArtifacts.Length > MaximumArtifacts)
            throw new InvalidDataException("Lokalny snapshot Registry przekracza limit artefaktów.");

        var receipts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var receipt in offline.CachedArtifacts)
        {
            if (receipt is null || receipt.Sha256 is null || !Sha256Pattern.IsMatch(receipt.Sha256) ||
                receipt.SizeBytes is < 1 or > MaximumArtifactSize)
                throw new InvalidDataException("Lokalny snapshot Registry zawiera niepoprawny rachunek artefaktu.");
            if (!receipts.TryAdd(receipt.Sha256, receipt.SizeBytes))
                throw new InvalidDataException("Lokalny snapshot Registry zawiera powtórzony rachunek artefaktu.");
        }

        var sourcePackages = snapshot.Artifacts
            .OfType<RegistryPackageArtifact>()
            .ToDictionary(package => $"{package.Id}@{package.Version}", StringComparer.Ordinal);
        if (receipts.Count != packages.Count)
            throw new InvalidDataException("Lokalny snapshot Registry nie odpowiada zawartości lockfile.");
        foreach (var package in packages)
        {
            var key = $"{package.Id}@{package.Version}";
            if (!sourcePackages.TryGetValue(key, out var source) ||
                !source.Namespace.Equals(package.Namespace, StringComparison.Ordinal) ||
                !source.ManifestSha256.Equals(package.ManifestSha256, StringComparison.Ordinal) ||
                !source.Sha256.Equals(package.ArtifactSha256, StringComparison.Ordinal) ||
                !source.Source.Equals(package.Source, StringComparison.Ordinal) ||
                !DependenciesEqual(source.Dependencies, package.Dependencies))
                throw new InvalidDataException($"Pakiet lockfile jest niezgodny ze snapshotem Registry: {key}");
            if (!receipts.TryGetValue(package.ArtifactSha256, out var cachedSize) ||
                cachedSize != decimal.ToInt64(source.SizeBytes))
                throw new InvalidDataException($"Lokalny cache jest niezgodny ze snapshotem Registry: {key}");
        }
    }

    private static bool DependenciesEqual(
        IReadOnlyCollection<PackageDependency> left,
        IReadOnlyCollection<PackageDependency> right) =>
        left.OrderBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Version, StringComparer.Ordinal)
            .SequenceEqual(
                right.OrderBy(item => item.Id, StringComparer.Ordinal)
                    .ThenBy(item => item.Version, StringComparer.Ordinal));
}
