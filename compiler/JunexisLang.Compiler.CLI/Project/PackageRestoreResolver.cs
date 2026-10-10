using System.IO.Compression;
using System.Security.Cryptography;

namespace JunexisLang.Compiler.CLI.Project;

internal static class PackageRestoreResolver
{
    private const string IndexSchema = "junexis.package-index/0.1";
    private const string ManifestSchema = "junexis.package/0.1";
    private sealed record PackageTransactionJournal(string Phase, bool HadCache, bool HadLock);

    internal static async Task<IReadOnlyList<PackageLockEntry>> RestoreAsync(
        JunexisProject project,
        bool offline,
        IReadOnlyCollection<string>? effectiveTargets = null,
        IPackageSource? packageSource = null,
        CancellationToken cancellationToken = default)
    {
        await using var transactionLock = AcquireTransactionLock(project);
        return await RestoreLockedAsync(project, offline, effectiveTargets, packageSource, cancellationToken);
    }

    private static async Task<IReadOnlyList<PackageLockEntry>> RestoreLockedAsync(
        JunexisProject project,
        bool offline,
        IReadOnlyCollection<string>? effectiveTargets,
        IPackageSource? packageSource,
        CancellationToken cancellationToken)
    {
        await RecoverTransactionAsync(project);
        var targets = effectiveTargets ?? project.Manifest.Targets;
        var requested = project.Manifest.Packages.Select(PackageReference.Parse).ToArray();
        if (requested.Length == 0)
        {
            await PackagesLockFile.WriteAsync(project.RootDirectory, []);
            return [];
        }

        if (offline)
            return await RestoreOfflineAsync(project, requested, targets);

        packageSource ??= CreateLocalSource(project);
        var index = await packageSource.LoadIndexAsync(cancellationToken);
        PackageContractValidator.Validate(index);

        var revocationStore = RegistryRevocationStore.Load(
            Environment.GetEnvironmentVariable("JUNEXIS_REGISTRY_TRUST_STORE"),
            project.RootDirectory.FullName);

        var entries = new Dictionary<string, PackageIndexEntry>(StringComparer.Ordinal);
        foreach (var entry in index.Packages)
        {
            var key = PackageKey(entry.Id, entry.Version);
            if (!entries.TryAdd(key, entry))
                throw new InvalidDataException($"Duplicate package index entry: {entry.Id}@{entry.Version}");
        }

        var stagingRoot = GetPackagesRoot(project) + $".{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(stagingRoot);
        try
        {
            var resolved = new Dictionary<string, PackageLockEntry>(StringComparer.Ordinal);
            var selectedVersions = new Dictionary<string, string>(StringComparer.Ordinal);
            var pending = new Queue<PackageReference>(requested.OrderBy(item => item.Id, StringComparer.Ordinal));
            while (pending.TryDequeue(out var reference))
            {
                revocationStore.CheckPackageRevoked(reference.Id, reference.Version);
                SelectVersion(selectedVersions, reference);
                var key = PackageKey(reference.Id, reference.Version);
                if (resolved.ContainsKey(key))
                    continue;
                if (!entries.TryGetValue(key, out var indexEntry))
                    throw new InvalidDataException($"Package not found in index: {reference.Id}@{reference.Version}");

                var package = await MaterializeAsync(
                    project,
                    targets,
                    packageSource,
                    indexEntry,
                    stagingRoot,
                    cancellationToken);
                resolved.Add(key, package);
                foreach (var dependency in indexEntry.Dependencies ?? package.Dependencies)
                    pending.Enqueue(new PackageReference(dependency.Id, dependency.Version));
            }

            var result = resolved.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            if (packageSource is RegistrySnapshotPackageSource registrySource)
                await RegistryOfflineSnapshot.PersistAsync(stagingRoot, registrySource, result);
            await PromoteAsync(project, stagingRoot, result);
            return result;
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<PackageLockEntry>> RestoreOfflineAsync(
        JunexisProject project,
        IReadOnlyList<PackageReference> requested,
        IReadOnlyCollection<string> effectiveTargets)
    {
        var revocationStore = RegistryRevocationStore.Load(
            Environment.GetEnvironmentVariable("JUNEXIS_REGISTRY_TRUST_STORE"),
            project.RootDirectory.FullName);
        var document = await PackagesLockFile.LoadAsync(project.RootDirectory);
        var locked = document.Packages.ToDictionary(
            package => PackageKey(package.Id, package.Version),
            StringComparer.Ordinal);
        var selectedVersions = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<PackageReference>(requested.OrderBy(item => item.Id, StringComparer.Ordinal));
        while (pending.TryDequeue(out var reference))
        {
            revocationStore.CheckPackageRevoked(reference.Id, reference.Version);
            SelectVersion(selectedVersions, reference);
            var key = PackageKey(reference.Id, reference.Version);
            if (!visited.Add(key))
                continue;
            if (!locked.TryGetValue(key, out var package))
                throw new InvalidDataException(
                    $"Package lockfile does not contain requested package: {reference.Id}@{reference.Version}");
            var manifest = await ValidateCachedPackageAsync(project, package, effectiveTargets);
            if (!DependenciesEqual(package.Dependencies, manifest.Dependencies))
                throw new InvalidDataException($"Cached package dependencies differ from lockfile: {key}");
            foreach (var dependency in package.Dependencies)
                pending.Enqueue(new PackageReference(dependency.Id, dependency.Version));
        }

        if (visited.Count != document.Packages.Length)
            throw new InvalidDataException("Package lockfile contains packages outside the requested dependency graph.");
        await RegistryOfflineSnapshot.ValidateAsync(project, document.Packages);
        return document.Packages;
    }

    private static async Task<PackageLockEntry> MaterializeAsync(
        JunexisProject project,
        IReadOnlyCollection<string> effectiveTargets,
        IPackageSource packageSource,
        PackageIndexEntry entry,
        string packagesRoot,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.Combine(packagesRoot, ".source");
        Directory.CreateDirectory(sourceRoot);
        var artifactPath = Path.Combine(sourceRoot, $"{entry.ArtifactSha256}.zip");
        await using (var destination = new SizeLimitedWriteStream(new FileStream(
            artifactPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan),
            PackageContractValidator.MaximumPackageSizeBytes))
        {
            await packageSource.CopyArtifactAsync(entry, destination, cancellationToken);
        }
        var artifactLength = new FileInfo(artifactPath).Length;
        if (artifactLength > PackageContractValidator.MaximumPackageSizeBytes)
            throw new InvalidDataException($"Package artifact exceeds the size limit: {entry.Id}@{entry.Version}");
        if (entry.SizeBytes is not null && artifactLength != entry.SizeBytes)
            throw new InvalidDataException($"Package artifact size differs from Registry snapshot: {entry.Id}@{entry.Version}");
        var artifactHash = await ComputeSha256Async(artifactPath);
        if (!artifactHash.Equals(entry.ArtifactSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Package artifact SHA-256 mismatch: {entry.Id}@{entry.Version}");

        var manifest = await MaterializeValidatedArtifactAsync(
            effectiveTargets,
            entry,
            packagesRoot,
            artifactPath);
        File.Delete(artifactPath);
        Directory.Delete(sourceRoot);

        return new PackageLockEntry(
            entry.Id,
            entry.Version,
            entry.Namespace,
            $"registry:{entry.Id}@{entry.Version}",
            entry.ManifestSha256,
            entry.ArtifactSha256,
            manifest.Dependencies);
    }

    private static async Task<ApplicationPackageManifest> MaterializeValidatedArtifactAsync(
        IReadOnlyCollection<string> effectiveTargets,
        PackageIndexEntry entry,
        string packagesRoot,
        string artifactPath)
    {
        await using var artifactStream = File.OpenRead(artifactPath);
        using var archive = new ZipArchive(artifactStream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry(entry.Manifest)
            ?? throw new InvalidDataException($"Package manifest not found in artifact: {entry.Manifest}");
        var manifestBytes = await ReadEntryAsync(manifestEntry, 1024 * 1024);
        var manifestHash = ComputeSha256(manifestBytes);
        if (!manifestHash.Equals(entry.ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Package manifest SHA-256 mismatch: {entry.Id}@{entry.Version}");

        var manifest = PackageJson.Deserialize<ApplicationPackageManifest>(
            System.Text.Encoding.UTF8.GetString(manifestBytes),
            $"{entry.Artifact}!/{entry.Manifest}");
        PackageContractValidator.Validate(manifest);
        PackageContractValidator.ValidateCompatibility(manifest, effectiveTargets);
        ValidateManifest(entry, manifest);

        var cacheRoot = GetCacheRoot(packagesRoot, entry.Id, entry.Version);
        Directory.CreateDirectory(cacheRoot);
        var contentRoot = Path.Combine(cacheRoot, "content");
        Directory.CreateDirectory(contentRoot);
        if (archive.Entries.Count > PackageContractValidator.MaximumFileCount + 1)
            throw new InvalidDataException($"Package artifact contains too many entries: {entry.Id}@{entry.Version}");
        var entries = archive.Entries
            .Where(item => !string.IsNullOrEmpty(item.Name))
            .GroupBy(item => item.FullName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidDataException($"Duplicate archive entry: {group.Key}"),
                StringComparer.Ordinal);
        foreach (var archivePath in entries.Keys)
            PackageContractValidator.ValidateRelativePath(archivePath, "archive entry");
        foreach (var file in manifest.Files)
        {
            if (!entries.TryGetValue(file.Path, out var archiveEntry))
                throw new InvalidDataException($"Package file not found in artifact: {file.Path}");
            if (archiveEntry.Length != file.SizeBytes || archiveEntry.Length > PackageContractValidator.MaximumFileSizeBytes)
                throw new InvalidDataException($"Package file size differs from manifest: {file.Path}");
            var destination = ResolveContainedFile(contentRoot, file.Path, "Package file");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await ExtractEntryAsync(archiveEntry, destination, file.SizeBytes);
        }
        await File.WriteAllBytesAsync(Path.Combine(contentRoot, "junexis.package.json"), manifestBytes);
        await CopyFileWithLimitAsync(
            artifactPath,
            Path.Combine(cacheRoot, "package.zip"),
            PackageContractValidator.MaximumPackageSizeBytes);
        await ValidateContentAsync(contentRoot, manifest);
        await CreateModuleViewAsync(cacheRoot, contentRoot, manifest);
        return manifest;
    }

    private static LocalPackageSource CreateLocalSource(JunexisProject project)
    {
        var indexFile = ResolveContainedFile(
            project.RootDirectory.FullName,
            project.Manifest.PackageIndex!,
            "Package index");
        return new LocalPackageSource(indexFile);
    }

    internal static async Task<ApplicationPackageManifest> ValidateCachedPackageAsync(
        JunexisProject project,
        PackageLockEntry package,
        IReadOnlyCollection<string>? effectiveTargets = null)
    {
        var cacheRoot = GetCacheRoot(project, package.Id, package.Version);
        var artifactPath = Path.Combine(cacheRoot, "package.zip");
        if (!File.Exists(artifactPath))
            throw new InvalidDataException($"Cached package artifact not found: {package.Id}@{package.Version}");
        if (new FileInfo(artifactPath).Length > PackageContractValidator.MaximumPackageSizeBytes)
            throw new InvalidDataException($"Cached package artifact exceeds the size limit: {package.Id}@{package.Version}");
        if (!string.Equals(await ComputeSha256Async(artifactPath), package.ArtifactSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Cached package artifact SHA-256 mismatch: {package.Id}@{package.Version}");

        var manifestPath = Path.Combine(cacheRoot, "content", "junexis.package.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"Cached package manifest not found: {package.Id}@{package.Version}");
        var manifestBytes = await PackageJson.ReadBytesAsync(manifestPath);
        if (!ComputeSha256(manifestBytes).Equals(package.ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Cached package manifest SHA-256 mismatch: {package.Id}@{package.Version}");
        var manifest = PackageJson.Deserialize<ApplicationPackageManifest>(
            System.Text.Encoding.UTF8.GetString(manifestBytes),
            manifestPath);
        PackageContractValidator.Validate(manifest);
        PackageContractValidator.ValidateCompatibility(manifest, effectiveTargets ?? project.Manifest.Targets);
        if (!manifest.Id.Equals(package.Id, StringComparison.Ordinal) ||
            !manifest.Version.Equals(package.Version, StringComparison.Ordinal) ||
            !manifest.Namespace.Equals(package.Namespace, StringComparison.Ordinal))
            throw new InvalidDataException($"Cached package identity differs from lockfile: {package.Id}@{package.Version}");
        await ValidateContentAsync(Path.GetDirectoryName(manifestPath)!, manifest);
        await ValidateModuleViewAsync(cacheRoot, Path.GetDirectoryName(manifestPath)!, manifest);
        return manifest;
    }

    internal static string GetCacheRoot(JunexisProject project, string id, string version) =>
        GetCacheRoot(GetPackagesRoot(project), id, version);

    internal static string GetModulesRoot(JunexisProject project, string id, string version) =>
        Path.Combine(GetCacheRoot(project, id, version), "modules");

    private static void ValidateManifest(PackageIndexEntry entry, ApplicationPackageManifest manifest)
    {
        if (!manifest.SchemaVersion.Equals(ManifestSchema, StringComparison.Ordinal) ||
            !manifest.Id.Equals(entry.Id, StringComparison.Ordinal) ||
            !manifest.Version.Equals(entry.Version, StringComparison.Ordinal) ||
            !manifest.Namespace.Equals(entry.Namespace, StringComparison.Ordinal))
            throw new InvalidDataException($"Package manifest identity mismatch: {entry.Id}@{entry.Version}");
            if (entry.Dependencies is not null && !DependenciesEqual(entry.Dependencies, manifest.Dependencies))
                throw new InvalidDataException($"Package manifest dependencies differ from Registry snapshot: {entry.Id}@{entry.Version}");
            if (entry.Language is not null &&
                (!entry.Language.Minimum.Equals(manifest.Language.Minimum, StringComparison.Ordinal) ||
                 !entry.Language.MaximumExclusive.Equals(manifest.Language.MaximumExclusive, StringComparison.Ordinal)))
                throw new InvalidDataException($"Package manifest language range differs from Registry snapshot: {entry.Id}@{entry.Version}");
            if (entry.Targets is not null &&
                !entry.Targets.OrderBy(target => target, StringComparer.Ordinal)
                    .SequenceEqual(manifest.Targets.OrderBy(target => target, StringComparer.Ordinal)))
                throw new InvalidDataException($"Package manifest targets differ from Registry snapshot: {entry.Id}@{entry.Version}");
    }

    private static async Task ValidateContentAsync(string contentRoot, ApplicationPackageManifest manifest)
    {
        foreach (var file in manifest.Files)
        {
            var path = ResolveContainedFile(contentRoot, file.Path, "Package file");
            if (!File.Exists(path))
                throw new InvalidDataException($"Package file not found: {manifest.Id}/{file.Path}");
            var info = new FileInfo(path);
            if (info.Length != file.SizeBytes ||
                !string.Equals(await ComputeSha256Async(path), file.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Package file integrity mismatch: {manifest.Id}/{file.Path}");
        }
    }

    private static async Task CreateModuleViewAsync(
        string cacheRoot,
        string contentRoot,
        ApplicationPackageManifest manifest)
    {
        var modulesRoot = Path.Combine(cacheRoot, "modules");
        Directory.CreateDirectory(modulesRoot);
        foreach (var export in manifest.Exports)
        {
            var source = ResolveContainedFile(contentRoot, export.Path, "Package export");
            var destination = ResolveContainedFile(
                modulesRoot,
                export.Module.Replace("::", "/", StringComparison.Ordinal) + ".jnx",
                "Package module");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var sourceStream = File.OpenRead(source);
            await using var destinationStream = File.Create(destination);
            await sourceStream.CopyToAsync(destinationStream);
        }
    }

    private static async Task ValidateModuleViewAsync(
        string cacheRoot,
        string contentRoot,
        ApplicationPackageManifest manifest)
    {
        var modulesRoot = Path.Combine(cacheRoot, "modules");
        var expectedModules = manifest.Exports
            .Select(export => export.Module.Replace("::", "/", StringComparison.Ordinal) + ".jnx")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var actualModules = Directory.Exists(modulesRoot)
            ? Directory.GetFiles(modulesRoot, "*.jnx", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(modulesRoot, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : [];
        if (!expectedModules.SequenceEqual(actualModules, StringComparer.Ordinal))
            throw new InvalidDataException($"Cached package module view has unexpected files: {manifest.Id}@{manifest.Version}");
        foreach (var export in manifest.Exports)
        {
            var source = ResolveContainedFile(contentRoot, export.Path, "Package export");
            var module = ResolveContainedFile(
                modulesRoot,
                export.Module.Replace("::", "/", StringComparison.Ordinal) + ".jnx",
                "Package module");
            if (!File.Exists(module) ||
                !string.Equals(await ComputeSha256Async(source), await ComputeSha256Async(module), StringComparison.Ordinal))
                throw new InvalidDataException($"Cached package module view is invalid: {export.Module}");
        }
    }

    private static async Task ExtractEntryAsync(ZipArchiveEntry entry, string destination, long expectedSize)
    {
        await using var source = entry.Open();
        await using var target = File.Create(destination);
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            written = checked(written + read);
            if (written > expectedSize || written > PackageContractValidator.MaximumFileSizeBytes)
                throw new InvalidDataException($"Package archive entry exceeds its declared size: {entry.FullName}");
            await target.WriteAsync(buffer.AsMemory(0, read));
        }
        if (written != expectedSize)
            throw new InvalidDataException($"Package archive entry size differs from manifest: {entry.FullName}");
    }

    private static async Task CopyFileWithLimitAsync(string source, string destination, long maximumSize)
    {
        await using var input = File.OpenRead(source);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            written = checked(written + read);
            if (written > maximumSize)
                throw new InvalidDataException($"Package artifact exceeds the size limit: {source}");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private static async Task PromoteAsync(
        JunexisProject project,
        string stagingRoot,
        IReadOnlyList<PackageLockEntry> packages)
    {
        var packagesRoot = GetPackagesRoot(project);
        var backupRoot = GetBackupRoot(project);
        var lockPath = Path.Combine(project.RootDirectory.FullName, PackagesLockFile.FileName);
        var lockBackupPath = GetLockBackupPath(project);
        var journalPath = GetJournalPath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(packagesRoot)!);
        var hadExistingCache = Directory.Exists(packagesRoot);
        var hadExistingLock = File.Exists(lockPath);
        if (Directory.Exists(backupRoot))
            Directory.Delete(backupRoot, recursive: true);
        File.Delete(lockBackupPath);
        if (hadExistingLock)
            File.Copy(lockPath, lockBackupPath);
        await WriteJournalAsync(
            journalPath,
            new PackageTransactionJournal("prepared", hadExistingCache, hadExistingLock));
        try
        {
            if (hadExistingCache)
                Directory.Move(packagesRoot, backupRoot);
            Directory.Move(stagingRoot, packagesRoot);
            await PackagesLockFile.WriteAsync(project.RootDirectory, packages);
            await WriteJournalAsync(
                journalPath,
                new PackageTransactionJournal("committed", hadExistingCache, hadExistingLock));
        }
        catch
        {
            await RecoverTransactionAsync(project);
            throw;
        }
        await RecoverTransactionAsync(project);
    }

    private static async Task RecoverTransactionAsync(JunexisProject project)
    {
        var journalPath = GetJournalPath(project);
        if (!File.Exists(journalPath))
            return;

        var journal = PackageJson.Deserialize<PackageTransactionJournal>(
            await PackageJson.ReadTextAsync(journalPath),
            journalPath);
        if (journal.Phase is not ("prepared" or "committed"))
            throw new InvalidDataException($"Invalid package transaction phase: {journal.Phase}");

        var packagesRoot = GetPackagesRoot(project);
        var backupRoot = GetBackupRoot(project);
        var lockPath = Path.Combine(project.RootDirectory.FullName, PackagesLockFile.FileName);
        var lockBackupPath = GetLockBackupPath(project);
        if (journal.Phase == "prepared")
        {
            if (journal.HadCache)
            {
                if (Directory.Exists(backupRoot))
                {
                    if (Directory.Exists(packagesRoot))
                        Directory.Delete(packagesRoot, recursive: true);
                    Directory.Move(backupRoot, packagesRoot);
                }
            }
            else if (Directory.Exists(packagesRoot))
            {
                Directory.Delete(packagesRoot, recursive: true);
            }
            if (journal.HadLock && File.Exists(lockBackupPath))
                File.Copy(lockBackupPath, lockPath, overwrite: true);
            else if (!journal.HadLock)
                File.Delete(lockPath);
        }

        if (Directory.Exists(backupRoot))
            Directory.Delete(backupRoot, recursive: true);
        File.Delete(lockBackupPath);
        File.Delete(journalPath);
    }

    private static async Task WriteJournalAsync(string path, PackageTransactionJournal journal)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            temporary,
            System.Text.Json.JsonSerializer.Serialize(journal, PackageJson.Options) + "\n");
        File.Move(temporary, path, overwrite: true);
    }

    internal static string GetPackagesRoot(JunexisProject project) =>
        Path.Combine(project.RootDirectory.FullName, ".junexis", "cache", "packages");

    private static string GetBackupRoot(JunexisProject project) =>
        Path.Combine(project.RootDirectory.FullName, ".junexis", "cache", "packages.backup");

    private static string GetLockBackupPath(JunexisProject project) =>
        Path.Combine(project.RootDirectory.FullName, ".junexis", "cache", "packages.lock.backup");

    private static string GetJournalPath(JunexisProject project) =>
        Path.Combine(project.RootDirectory.FullName, ".junexis", "cache", "packages.transaction.json");

    private static FileStream AcquireTransactionLock(JunexisProject project)
    {
        var cacheRoot = Path.Combine(project.RootDirectory.FullName, ".junexis", "cache");
        Directory.CreateDirectory(cacheRoot);
        var path = Path.Combine(cacheRoot, "packages.transaction.lock");
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidDataException(
                "Another package restore, check or build is already using this project cache.",
                ex);
        }
    }

    internal static string GetCacheRoot(string packagesRoot, string id, string version)
    {
        _ = PackageReference.Parse($"{id}@{version}");
        return ResolveContainedFile(packagesRoot, $"{id}/{version}", "Package cache");
    }

    private static void SelectVersion(IDictionary<string, string> selectedVersions, PackageReference reference)
    {
        if (selectedVersions.TryGetValue(reference.Id, out var selectedVersion) &&
            !selectedVersion.Equals(reference.Version, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Conflicting exact versions for package '{reference.Id}': '{selectedVersion}' and '{reference.Version}'.");
        selectedVersions[reference.Id] = reference.Version;
    }

    private static bool DependenciesEqual(
        IReadOnlyList<PackageDependency> locked,
        IReadOnlyList<PackageDependency> manifest) =>
        locked.OrderBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version, StringComparer.Ordinal)
            .SequenceEqual(manifest.OrderBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version, StringComparer.Ordinal));

    internal static string ResolveContainedFile(string root, string relativePath, string description)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"{description} path must be relative: {relativePath}");
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException($"{description} path escapes its root: {relativePath}");
        return resolved;
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, int maximumSize)
    {
        if (entry.Length > maximumSize)
            throw new InvalidDataException($"Package archive entry exceeds its size limit: {entry.FullName}");
        await using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > maximumSize)
                throw new InvalidDataException($"Package archive entry exceeds its size limit: {entry.FullName}");
            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }
        return buffer.ToArray();
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string PackageKey(string id, string version) => $"{id}@{version}";
}
