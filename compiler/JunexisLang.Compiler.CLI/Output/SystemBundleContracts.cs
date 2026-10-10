using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record SystemBundleDescriptor(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("projectName")] string ProjectName,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("entryFile")] string EntryFile,
    [property: JsonPropertyName("targets")] IReadOnlyList<string> Targets,
    [property: JsonPropertyName("artifacts")] IReadOnlyList<SystemBundleArtifact> Artifacts)
{
    internal const string CurrentVersion = "junexis.system-bundle/0.1";
}

internal sealed record SystemBundleArtifact(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("ownership")] string Ownership,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record SystemBundleIntegrityManifest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("sha256")] IReadOnlyDictionary<string, string> Sha256)
{
    internal const string CurrentVersion = "junexis.system-bundle-integrity/0.1";
}

internal sealed record SystemBundleInspection(
    string Path,
    string BundleId,
    SystemBundleDescriptor Descriptor,
    int VerifiedFileCount);

internal static class SystemBundleContract
{
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;
    private const long MaximumEntryBytes = 64L * 1024 * 1024;
    private const int MaximumEntryCount = 4096;
    private const string DescriptorPath = "system-bundle.json";
    private const string InventoryPath = "contracts/junexis.artifacts.json";
    private const string IntegrityPath = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<SystemBundleInspection> CreateAsync(
        string? projectPath,
        FileInfo? destination,
        CancellationToken cancellationToken = default)
    {
        var resolved = await CliProjectInputResolver.ResolveAsync(projectPath)
            .ConfigureAwait(false);
        var project = resolved.Project
            ?? throw new InvalidDataException(
                "Experimental System Bundle 0.1 is project-first; a project manifest is required.");
        var projectRoot = Path.GetFullPath(project.RootDirectory.FullName);
        var receiptPath = Path.Combine(projectRoot, ".junexis", "state", "latest-build.json");
        if (!File.Exists(receiptPath))
            throw new InvalidDataException("No latest project build exists; run 'junexis build' first.");
        var receipt = LatestBuildReceiptContract.Parse(
            await File.ReadAllTextAsync(receiptPath, cancellationToken).ConfigureAwait(false));
        ValidateReceipt(receipt, project);
        var outputRoot = Path.GetFullPath(receipt.OutputRoot);

        var inventoryBytes = await ReadBoundedAsync(
            receipt.ArtifactManifest,
            "artifact inventory",
            cancellationToken).ConfigureAwait(false);
        var inventory = Deserialize<ArtifactInventory>(inventoryBytes, "artifact inventory");
        if (!string.Equals(
                inventory.SchemaVersion,
                ArtifactInventory.CurrentVersion,
                StringComparison.Ordinal)
            || !string.Equals(inventory.BuildId, receipt.BuildId, StringComparison.Ordinal)
            || inventory.Artifacts is null
            || inventory.Artifacts.Count == 0)
        {
            throw new InvalidDataException(
                "Artifact inventory is unsupported, empty, or belongs to a different build.");
        }

        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var artifacts = new List<SystemBundleArtifact>();
        var artifactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in inventory.Artifacts.OrderBy(
                     item => Normalize(item.Path),
                     StringComparer.Ordinal))
        {
            var relativePath = Normalize(artifact.Path);
            ValidateRelativePath(relativePath);
            RejectSensitivePath(relativePath);
            if (!artifactPaths.Add(relativePath))
                throw new InvalidDataException($"Duplicate artifact path '{relativePath}'.");
            ValidateHash(artifact.Sha256, $"artifact '{relativePath}'");
            var sourcePath = ResolveChild(outputRoot, relativePath);
            var source = new FileInfo(sourcePath);
            if (!source.Exists)
                throw new InvalidDataException($"Artifact '{relativePath}' is missing from the latest build.");
            if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Artifact '{relativePath}' cannot be a symbolic link.");
            var content = await ReadBoundedAsync(
                source.FullName,
                $"artifact '{relativePath}'",
                cancellationToken).ConfigureAwait(false);
            if (!Hash(content).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Artifact '{relativePath}' no longer matches the latest build inventory.");
            var bundlePath = $"artifacts/{relativePath}";
            entries[bundlePath] = content;
            artifacts.Add(new SystemBundleArtifact(
                bundlePath,
                artifact.Target,
                artifact.Kind,
                artifact.Ownership,
                artifact.Sha256.ToLowerInvariant()));
        }

        entries[InventoryPath] = inventoryBytes;
        var entryFile = Normalize(Path.GetRelativePath(projectRoot, project.EntryFile.FullName));
        ValidateRelativePath(entryFile);
        var descriptor = new SystemBundleDescriptor(
            SystemBundleDescriptor.CurrentVersion,
            project.Manifest.Name,
            receipt.BuildId,
            entryFile,
            receipt.Targets.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            artifacts);
        entries[DescriptorPath] = JsonSerializer.SerializeToUtf8Bytes(descriptor, JsonOptions);
        var hashes = entries.ToDictionary(
            item => item.Key,
            item => Hash(item.Value),
            StringComparer.Ordinal);
        var integrity = new SystemBundleIntegrityManifest(
            SystemBundleIntegrityManifest.CurrentVersion,
            new SortedDictionary<string, string>(hashes, StringComparer.Ordinal));
        var integrityBytes = JsonSerializer.SerializeToUtf8Bytes(integrity, JsonOptions);
        entries[IntegrityPath] = integrityBytes;
        if (entries.Count > MaximumEntryCount
            || entries.Sum(item => item.Value.LongLength) > MaximumArchiveBytes)
            throw new InvalidDataException(
                "System Bundle exceeds the entry-count or uncompressed-size safety limit.");

        var destinationPath = destination is null
            ? DefaultDestination(outputRoot, project.Manifest.Name, receipt.BuildId)
            : Path.GetFullPath(destination.FullName);
        await WriteAtomicZipAsync(destinationPath, entries, cancellationToken).ConfigureAwait(false);
        return new SystemBundleInspection(
            destinationPath,
            Hash(integrityBytes),
            descriptor,
            hashes.Count);
    }

    internal static async Task<SystemBundleInspection> VerifyAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("System Bundle was not found.", fullPath);
        if (file.Length > MaximumArchiveBytes)
            throw new InvalidDataException("System Bundle exceeds the 512 MiB safety limit.");

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        await using (var input = new FileStream(
                         fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         81920, useAsync: true))
        using (var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false))
        {
            if (archive.Entries.Count is < 3 or > MaximumEntryCount)
                throw new InvalidDataException(
                    $"System Bundle must contain between 3 and {MaximumEntryCount} files.");
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateRelativePath(entry.FullName);
                if (!names.Add(entry.FullName))
                    throw new InvalidDataException($"Duplicate bundle entry '{entry.FullName}'.");
                if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
                    throw new InvalidDataException($"Bundle entry '{entry.FullName}' is too large.");
                totalLength += entry.Length;
                if (totalLength > MaximumArchiveBytes)
                    throw new InvalidDataException(
                        "System Bundle exceeds the 512 MiB uncompressed safety limit.");
                await using var entryStream = entry.Open();
                using var buffer = new MemoryStream((int)entry.Length);
                await entryStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (buffer.Length != entry.Length)
                    throw new InvalidDataException(
                        $"Bundle entry '{entry.FullName}' has an inconsistent length.");
                entries[entry.FullName] = buffer.ToArray();
            }
        }

        var integrityBytes = Require(entries, IntegrityPath);
        var integrity = Deserialize<SystemBundleIntegrityManifest>(
            integrityBytes,
            "integrity manifest");
        if (!string.Equals(
                integrity.SchemaVersion,
                SystemBundleIntegrityManifest.CurrentVersion,
                StringComparison.Ordinal)
            || integrity.Sha256 is null)
            throw new InvalidDataException("Unsupported System Bundle integrity contract.");
        var payloadPaths = entries.Keys
            .Where(name => !name.Equals(IntegrityPath, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (integrity.Sha256.Count != payloadPaths.Length
            || !payloadPaths.SequenceEqual(
                integrity.Sha256.Keys.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
            throw new InvalidDataException(
                "System Bundle manifest does not describe exactly every payload file.");
        foreach (var payloadPath in payloadPaths)
        {
            if (!integrity.Sha256.TryGetValue(payloadPath, out var expected))
                throw new InvalidDataException($"Missing hash for '{payloadPath}'.");
            ValidateHash(expected, $"bundle entry '{payloadPath}'");
            if (!Hash(entries[payloadPath]).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"System Bundle integrity check failed for '{payloadPath}'.");
        }

        var descriptor = Deserialize<SystemBundleDescriptor>(
            Require(entries, DescriptorPath),
            "System Bundle descriptor");
        ValidateDescriptor(descriptor, entries);
        var inventory = Deserialize<ArtifactInventory>(
            Require(entries, InventoryPath),
            "artifact inventory");
        ValidateInventory(descriptor, inventory);
        return new SystemBundleInspection(
            fullPath,
            Hash(integrityBytes),
            descriptor,
            payloadPaths.Length);
    }

    private static void ValidateReceipt(
        LatestBuildReceipt receipt,
        JunexisProject project)
    {
        if (receipt.Mode != "project"
            || !SamePath(receipt.ProjectRoot, project.RootDirectory.FullName)
            || !SamePath(receipt.EntryFile, project.EntryFile.FullName))
            throw new InvalidDataException(
                "latest-build receipt does not belong to the selected project.");
        if (!File.Exists(receipt.ArtifactManifest))
            throw new InvalidDataException("latest-build artifact inventory is missing.");
    }

    private static void ValidateDescriptor(
        SystemBundleDescriptor descriptor,
        IReadOnlyDictionary<string, byte[]> entries)
    {
        if (!string.Equals(
                descriptor.SchemaVersion,
                SystemBundleDescriptor.CurrentVersion,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(descriptor.ProjectName)
            || descriptor.ProjectName.Length > 200
            || string.IsNullOrWhiteSpace(descriptor.BuildId)
            || descriptor.Targets is null
            || descriptor.Targets.Count == 0
            || descriptor.Targets.Count > 32
            || descriptor.Targets.Any(target =>
                string.IsNullOrWhiteSpace(target) || target.Length > 64)
            || descriptor.Targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != descriptor.Targets.Count
            || descriptor.Artifacts is null
            || descriptor.Artifacts.Count == 0
            || descriptor.Artifacts.Count > MaximumEntryCount - 3)
            throw new InvalidDataException("System Bundle descriptor is invalid or unsupported.");
        ValidateHash(descriptor.BuildId, "System Bundle buildId");
        ValidateRelativePath(descriptor.EntryFile);
        var expectedPaths = descriptor.Artifacts.Select(item => item.Path)
            .Order(StringComparer.Ordinal).ToArray();
        if (expectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != expectedPaths.Length
            || expectedPaths.Any(path =>
                !path.StartsWith("artifacts/", StringComparison.Ordinal)
                || !entries.ContainsKey(path)))
            throw new InvalidDataException(
                "System Bundle descriptor contains missing or duplicate artifacts.");
        var exactPayload = expectedPaths
            .Append(DescriptorPath)
            .Append(InventoryPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualPayload = entries.Keys
            .Where(path => !path.Equals(IntegrityPath, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!exactPayload.SequenceEqual(actualPayload, StringComparer.Ordinal))
            throw new InvalidDataException(
                "System Bundle contains payload not declared by its descriptor.");
        foreach (var artifact in descriptor.Artifacts)
        {
            ValidateRelativePath(artifact.Path);
            if (artifact.Path.Length > 2048
                || string.IsNullOrWhiteSpace(artifact.Target)
                || artifact.Target.Length > 64
                || string.IsNullOrWhiteSpace(artifact.Kind)
                || artifact.Kind.Length > 128
                || string.IsNullOrWhiteSpace(artifact.Ownership)
                || artifact.Ownership.Length > 128)
                throw new InvalidDataException(
                    $"System Bundle artifact metadata is invalid for '{artifact.Path}'.");
            ValidateHash(artifact.Sha256, $"artifact '{artifact.Path}'");
            if (!Hash(entries[artifact.Path]).Equals(
                    artifact.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"System Bundle descriptor hash failed for '{artifact.Path}'.");
        }
    }

    private static void ValidateInventory(
        SystemBundleDescriptor descriptor,
        ArtifactInventory inventory)
    {
        if (!string.Equals(inventory.SchemaVersion, ArtifactInventory.CurrentVersion, StringComparison.Ordinal)
            || !string.Equals(inventory.BuildId, descriptor.BuildId, StringComparison.Ordinal)
            || inventory.Artifacts is null
            || inventory.Artifacts.Count != descriptor.Artifacts.Count
            || inventory.Artifacts.Select(item => Normalize(item.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != inventory.Artifacts.Count)
            throw new InvalidDataException(
                "Embedded artifact inventory does not match the System Bundle descriptor.");
        var inventoryMap = inventory.Artifacts.ToDictionary(
            item => $"artifacts/{Normalize(item.Path)}",
            StringComparer.Ordinal);
        foreach (var artifact in descriptor.Artifacts)
        {
            if (!inventoryMap.TryGetValue(artifact.Path, out var source)
                || !string.Equals(source.Target, artifact.Target, StringComparison.Ordinal)
                || !string.Equals(source.Kind, artifact.Kind, StringComparison.Ordinal)
                || !string.Equals(source.Ownership, artifact.Ownership, StringComparison.Ordinal)
                || !string.Equals(source.Sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Embedded artifact inventory differs for '{artifact.Path}'.");
        }
    }

    private static async Task WriteAtomicZipAsync(
        string path,
        IReadOnlyDictionary<string, byte[]> entries,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("System Bundle destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(
                             temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, useAsync: true))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var item in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(item.Key, CompressionLevel.Optimal);
                    entry.LastWriteTime =
                        new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    await using var stream = entry.Open();
                    await stream.WriteAsync(item.Value, cancellationToken).ConfigureAwait(false);
                }
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        string label,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new InvalidDataException($"{label} is missing: {file.FullName}");
        if (file.Length > MaximumEntryBytes)
            throw new InvalidDataException($"{label} exceeds the 64 MiB entry limit.");
        return await File.ReadAllBytesAsync(file.FullName, cancellationToken).ConfigureAwait(false);
    }

    private static T Deserialize<T>(byte[] content, string label)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new InvalidDataException($"{label} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{label} contains invalid JSON.", ex);
        }
    }

    private static byte[] Require(
        IReadOnlyDictionary<string, byte[]> entries,
        string path) =>
        entries.TryGetValue(path, out var value)
            ? value
            : throw new InvalidDataException($"System Bundle is missing '{path}'.");

    private static string ResolveChild(string root, string relativePath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(
            Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Artifact path escapes the output root: '{relativePath}'.");
        return fullPath;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > 2048
            || path.Contains('\\', StringComparison.Ordinal)
            || path.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(path)
            || path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe System Bundle path '{path}'.");
    }

    private static void RejectSensitivePath(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pem", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".key", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Sensitive artifact '{path}' cannot be packaged in System Bundle 0.1.");
    }

    private static void ValidateHash(string? value, string label)
    {
        if (value is null
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"{label} must use a 64-character SHA-256.");
    }

    private static string DefaultDestination(
        string outputRoot,
        string projectName,
        string buildId)
    {
        var safeName = string.Concat(projectName.Select(
            character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')).Trim('-');
        if (safeName.Length == 0)
            safeName = "junexis-system";
        return Path.Combine(
            outputRoot,
            "system",
            $"{safeName}-{buildId[..12]}.system-bundle.zip");
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string Hash(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static string Normalize(string path) => path.Replace('\\', '/');
}
