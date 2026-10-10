using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record PackageIndexDocument(string SchemaVersion, PackageIndexEntry[] Packages);

internal sealed record PackageIndexEntry(
    string Id,
    string Version,
    string Namespace,
    string Manifest,
    string ManifestSha256,
    string Artifact,
    string ArtifactSha256,
    PackageDependency[]? Dependencies = null,
    PackageLanguageRange? Language = null,
    string[]? Targets = null,
    long? SizeBytes = null);

internal sealed record ApplicationPackageManifest(
    string SchemaVersion,
    string Id,
    string Namespace,
    string Version,
    PackageLanguageRange Language,
    string[] Targets,
    string[] Capabilities,
    PackageExport[] Exports,
    PackageDependency[] Dependencies,
    PackageFile[] Files);

internal sealed record PackageLanguageRange(string Minimum, string MaximumExclusive);

internal sealed record PackageExport(string Module, string Path);

internal sealed record PackageDependency(string Id, string Version);

internal sealed record PackageFile(string Path, long SizeBytes, string Sha256);

internal static class PackageJson
{
    internal const int MaximumDocumentSizeBytes = 4 * 1024 * 1024;

    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true
    };

    internal static T Deserialize<T>(string json, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            ValidateNoDuplicateProperties(document.RootElement, source);
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new InvalidDataException($"Package JSON is empty: {source}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid package JSON at {source}: {ex.Message}", ex);
        }
    }

    internal static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(path, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    internal static async Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"Package JSON not found: {path}", path);
        if (info.Length > MaximumDocumentSizeBytes)
            throw new InvalidDataException($"Package JSON exceeds the size limit: {path}");
        await using var input = File.OpenRead(path);
        using var output = new MemoryStream((int)Math.Min(info.Length, MaximumDocumentSizeBytes));
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaximumDocumentSizeBytes)
                throw new InvalidDataException($"Package JSON exceeds the size limit: {path}");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static void ValidateNoDuplicateProperties(JsonElement element, string source)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Duplicate JSON property '{property.Name}' at {source}.");
                ValidateNoDuplicateProperties(property.Value, source);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ValidateNoDuplicateProperties(item, source);
        }
    }
}

internal static class PackageContractValidator
{
    private const string CurrentLanguageVersion = "0.2.0-experimental";
    internal const long MaximumFileSizeBytes = 64L * 1024 * 1024;
    internal const long MaximumPackageSizeBytes = 256L * 1024 * 1024;
    internal const int MaximumFileCount = 10_000;

    private static readonly Regex ModulePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex CapabilityPattern = new(
        "^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex SourcePathPattern = new(
        "^[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*\\.jnx$",
        RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex PathSegmentPattern = new("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> WindowsDeviceNames = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    internal static void Validate(PackageIndexDocument index)
    {
        Require(index.Packages, "Package index is missing 'packages'.");
        RequireText(index.SchemaVersion, "Package index is missing 'schemaVersion'.");
        if (!index.SchemaVersion.Equals("junexis.package-index/0.1", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported package index schema: {index.SchemaVersion}");
        if (index.Packages.Length > MaximumFileCount)
            throw new InvalidDataException("Package index exceeds the entry limit.");
        foreach (var entry in index.Packages)
        {
            if (entry is null)
                throw new InvalidDataException("Package index contains a null entry.");
            RequireText(entry.Id, "Package index entry is missing 'id'.");
            RequireText(entry.Version, "Package index entry is missing 'version'.");
            RequireText(entry.Namespace, "Package index entry is missing 'namespace'.");
            RequireText(entry.Manifest, "Package index entry is missing 'manifest'.");
            RequireText(entry.ManifestSha256, "Package index entry is missing 'manifestSha256'.");
            RequireText(entry.Artifact, "Package index entry is missing 'artifact'.");
            RequireText(entry.ArtifactSha256, "Package index entry is missing 'artifactSha256'.");
            ValidateIdentity(entry.Id, entry.Version, entry.Namespace);
            ValidateHash(entry.ManifestSha256, "manifestSha256");
            ValidateHash(entry.ArtifactSha256, "artifactSha256");
            if (!entry.Manifest.Equals("junexis.package.json", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported package manifest name: {entry.Manifest}");
            ValidateRelativePath(entry.Artifact, "package artifact");
            if (!entry.Artifact.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Package artifact must be a ZIP file: {entry.Artifact}");
        }
            EnsureUnique(index.Packages.Select(item => $"{item.Id}@{item.Version}"), "package index entry");
    }

    internal static void Validate(ApplicationPackageManifest manifest)
    {
        RequireText(manifest.SchemaVersion, "Package manifest is missing 'schemaVersion'.");
        RequireText(manifest.Id, "Package manifest is missing 'id'.");
        RequireText(manifest.Namespace, "Package manifest is missing 'namespace'.");
        RequireText(manifest.Version, "Package manifest is missing 'version'.");
        Require(manifest.Language, "Package manifest is missing 'language'.");
        Require(manifest.Targets, "Package manifest is missing 'targets'.");
        Require(manifest.Capabilities, "Package manifest is missing 'capabilities'.");
        Require(manifest.Exports, "Package manifest is missing 'exports'.");
        Require(manifest.Dependencies, "Package manifest is missing 'dependencies'.");
        Require(manifest.Files, "Package manifest is missing 'files'.");
        RequireText(manifest.Language.Minimum, "Package language range is missing 'minimum'.");
        RequireText(manifest.Language.MaximumExclusive, "Package language range is missing 'maximumExclusive'.");
        ValidateTextValues(manifest.Targets, "package target");
        ValidateTextValues(manifest.Capabilities, "package capability");
        if (manifest.Exports.Any(item => item is null) ||
            manifest.Dependencies.Any(item => item is null) ||
            manifest.Files.Any(item => item is null))
            throw new InvalidDataException("Package manifest contains a null object entry.");
        foreach (var export in manifest.Exports)
        {
            RequireText(export.Module, "Package export is missing 'module'.");
            RequireText(export.Path, "Package export is missing 'path'.");
        }
        foreach (var dependency in manifest.Dependencies)
        {
            RequireText(dependency.Id, "Package dependency is missing 'id'.");
            RequireText(dependency.Version, "Package dependency is missing 'version'.");
        }
        foreach (var file in manifest.Files)
        {
            RequireText(file.Path, "Package file is missing 'path'.");
            RequireText(file.Sha256, "Package file is missing 'sha256'.");
        }
        if (!manifest.SchemaVersion.Equals("junexis.package/0.1", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported package manifest schema: {manifest.SchemaVersion}");
        ValidateIdentity(manifest.Id, manifest.Version, manifest.Namespace);
        _ = PackageReference.Parse($"language@{manifest.Language.Minimum}");
        _ = PackageReference.Parse($"language@{manifest.Language.MaximumExclusive}");
        if (manifest.Targets.Length == 0 || manifest.Targets.Any(target => target is not ("embedded" or "server" or "report")))
            throw new InvalidDataException($"Package '{manifest.Id}' declares invalid targets.");
        if (manifest.Capabilities.Any(capability => !CapabilityPattern.IsMatch(capability)))
            throw new InvalidDataException($"Package '{manifest.Id}' declares an invalid capability.");
        EnsureUnique(manifest.Targets, "package target");
        EnsureUnique(manifest.Capabilities, "package capability");
        EnsureUnique(manifest.Exports.Select(export => export.Module), "package export");
        EnsureUnique(manifest.Files.Select(file => file.Path), "package file");
        EnsureUnique(manifest.Dependencies.Select(item => $"{item.Id}@{item.Version}"), "package dependency");
        if (manifest.Files.Length == 0 || manifest.Files.Length > MaximumFileCount)
            throw new InvalidDataException($"Package '{manifest.Id}' has an invalid file count.");

        long totalSize = 0;
        foreach (var file in manifest.Files)
        {
            ValidateRelativePath(file.Path, "package file");
            ValidateHash(file.Sha256, "file sha256");
            if (file.SizeBytes <= 0 || file.SizeBytes > MaximumFileSizeBytes)
                throw new InvalidDataException($"Package file has an invalid size: {file.Path}");
            totalSize = checked(totalSize + file.SizeBytes);
        }
        if (totalSize > MaximumPackageSizeBytes)
            throw new InvalidDataException($"Package '{manifest.Id}' exceeds the unpacked size limit.");

        var files = manifest.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        if (manifest.Exports.Length == 0)
            throw new InvalidDataException($"Package '{manifest.Id}' does not export any modules.");
        foreach (var export in manifest.Exports)
        {
            if (!ModulePattern.IsMatch(export.Module))
                throw new InvalidDataException($"Invalid package export module: {export.Module}");
            ValidateRelativePath(export.Path, "package export");
            if (!SourcePathPattern.IsMatch(export.Path) || !files.Contains(export.Path))
                throw new InvalidDataException($"Package export is not a declared .jnx file: {export.Path}");
        }
        foreach (var dependency in manifest.Dependencies)
            _ = PackageReference.Parse($"{dependency.Id}@{dependency.Version}");
    }

    internal static void ValidateCompatibility(
        ApplicationPackageManifest manifest,
        IReadOnlyCollection<string> projectTargets)
    {
        if (CompareSemVer(manifest.Language.Minimum, CurrentLanguageVersion) > 0 ||
            CompareSemVer(CurrentLanguageVersion, manifest.Language.MaximumExclusive) >= 0)
            throw new InvalidDataException(
                $"Package '{manifest.Id}@{manifest.Version}' does not support language {CurrentLanguageVersion}.");
        var unsupportedTargets = projectTargets.Except(manifest.Targets, StringComparer.Ordinal).ToArray();
        if (unsupportedTargets.Length > 0)
            throw new InvalidDataException(
                $"Package '{manifest.Id}@{manifest.Version}' does not support project target(s): {string.Join(", ", unsupportedTargets)}.");
    }

    internal static void Validate(PackagesLockDocument document)
    {
        Require(document.Packages, "Package lockfile is missing 'packages'.");
        RequireText(document.SchemaVersion, "Package lockfile is missing 'schemaVersion'.");
        if (!document.SchemaVersion.Equals("junexis.packages.lock/0.1", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported package lockfile schema: {document.SchemaVersion}");
        if (document.Packages.Length > MaximumFileCount)
            throw new InvalidDataException("Package lockfile exceeds the entry limit.");
        foreach (var package in document.Packages)
        {
            if (package is null)
                throw new InvalidDataException("Package lockfile contains a null entry.");
            RequireText(package.Id, "Package lock entry is missing 'id'.");
            RequireText(package.Version, "Package lock entry is missing 'version'.");
            RequireText(package.Namespace, "Package lock entry is missing 'namespace'.");
            RequireText(package.Source, "Package lock entry is missing 'source'.");
            RequireText(package.ManifestSha256, "Package lock entry is missing 'manifestSha256'.");
            RequireText(package.ArtifactSha256, "Package lock entry is missing 'artifactSha256'.");
            Require(package.Dependencies, "Package lock entry is missing 'dependencies'.");
            if (package.Dependencies.Any(item => item is null))
                throw new InvalidDataException("Package lock entry contains a null dependency.");
            foreach (var dependency in package.Dependencies)
            {
                RequireText(dependency.Id, "Locked dependency is missing 'id'.");
                RequireText(dependency.Version, "Locked dependency is missing 'version'.");
            }
            ValidateIdentity(package.Id, package.Version, package.Namespace);
            if (!package.Source.Equals($"registry:{package.Id}@{package.Version}", StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid package lock source: {package.Source}");
            ValidateHash(package.ManifestSha256, "manifestSha256");
            ValidateHash(package.ArtifactSha256, "artifactSha256");
            EnsureUnique(package.Dependencies.Select(item => $"{item.Id}@{item.Version}"), "locked dependency");
            foreach (var dependency in package.Dependencies)
                _ = PackageReference.Parse($"{dependency.Id}@{dependency.Version}");
        }
        EnsureUnique(document.Packages.Select(item => $"{item.Id}@{item.Version}"), "package lock entry");
    }

    internal static void ValidateRelativePath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || Path.IsPathRooted(path) || path.Contains('\\'))
            throw new InvalidDataException($"Invalid {description} path: {path}");
        var segments = path.Split('/');
        if (segments.Any(segment =>
            segment.Length == 0 ||
            segment is "." or ".." ||
            !PathSegmentPattern.IsMatch(segment) ||
            segment.EndsWith('.') ||
            segment.EndsWith(' ') ||
            WindowsDeviceNames.Contains(Path.GetFileNameWithoutExtension(segment))))
            throw new InvalidDataException($"Invalid {description} path: {path}");
    }

    private static void ValidateIdentity(string id, string version, string packageNamespace)
    {
        var reference = PackageReference.Parse($"{id}@{version}");
        if (!reference.Id.Equals(id, StringComparison.Ordinal) ||
            !reference.Version.Equals(version, StringComparison.Ordinal) ||
            !ModulePattern.IsMatch(packageNamespace))
            throw new InvalidDataException($"Invalid package identity: {id}@{version}");
    }

    private static void ValidateHash(string hash, string field)
    {
        if (!HashPattern.IsMatch(hash))
            throw new InvalidDataException($"Invalid package {field}: {hash}");
    }

    private static void EnsureUnique(IEnumerable<string> values, string description)
    {
        var duplicate = values.GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Duplicate {description}: {duplicate.Key}");
    }

    private static void Require<T>(T? value, string message) where T : class
    {
        if (value is null)
            throw new InvalidDataException(message);
    }

    private static void RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException(message);
    }

    private static void ValidateTextValues(IEnumerable<string> values, string description)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Package contains an empty {description}.");
    }

    private static int CompareSemVer(string left, string right)
    {
        var leftParts = ParseSemVer(left);
        var rightParts = ParseSemVer(right);
        var coreComparison = leftParts.Core.CompareTo(rightParts.Core);
        if (coreComparison != 0)
            return coreComparison;
        if (leftParts.PreRelease is null)
            return rightParts.PreRelease is null ? 0 : 1;
        if (rightParts.PreRelease is null)
            return -1;

        var leftIdentifiers = leftParts.PreRelease.Split('.');
        var rightIdentifiers = rightParts.PreRelease.Split('.');
        for (var index = 0; index < Math.Min(leftIdentifiers.Length, rightIdentifiers.Length); index++)
        {
            var leftNumeric = int.TryParse(leftIdentifiers[index], out var leftNumber);
            var rightNumeric = int.TryParse(rightIdentifiers[index], out var rightNumber);
            var comparison = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric
                    ? -1
                    : rightNumeric
                        ? 1
                        : string.CompareOrdinal(leftIdentifiers[index], rightIdentifiers[index]);
            if (comparison != 0)
                return comparison;
        }
        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    private static (Version Core, string? PreRelease) ParseSemVer(string value)
    {
        _ = PackageReference.Parse($"version@{value}");
        var withoutBuild = value.Split('+', 2)[0];
        var separator = withoutBuild.IndexOf('-');
        var core = separator < 0 ? withoutBuild : withoutBuild[..separator];
        var preRelease = separator < 0 ? null : withoutBuild[(separator + 1)..];
        return (Version.Parse(core), preRelease);
    }
}