using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.CLI.Output;

/// <summary>Manifest samodzielnej paczki Laboratorium Junexis.</summary>
internal sealed record LaboratoryPackageManifest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("laboratoryId")] string LaboratoryId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("runtimeIdentifier")] string RuntimeIdentifier,
    [property: JsonPropertyName("entrypoint")] string Entrypoint,
    [property: JsonPropertyName("panelPath")] string PanelPath,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("files")] IReadOnlyDictionary<string, string> Files)
{
    internal const string CurrentVersion = "junexis.laboratory-package/0.1";
}

/// <summary>Wynik zweryfikowania paczki laboratorium.</summary>
internal sealed record LaboratoryPackageInspection(
    string Path,
    LaboratoryPackageManifest Manifest,
    int VerifiedFileCount);

/// <summary>Tworzy, weryfikuje i bezpiecznie rozpakowuje paczki laboratoriów.</summary>
internal static class LaboratoryPackageContract
{
    private const string ManifestPath = "laboratory-package.json";
    private const long MaximumArchiveBytes = 256L * 1024 * 1024;
    private const int MaximumEntries = 32;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>Tworzy paczkę z natywnym serwerem i dowodowym System Bundle.</summary>
    internal static async Task<LaboratoryPackageInspection> CreateAsync(
        string destination,
        string laboratoryJson,
        string readme,
        string executable,
        string systemBundle,
        string laboratoryId,
        string name,
        string version,
        string runtimeIdentifier,
        string buildId,
        string bundleId,
        CancellationToken cancellationToken = default)
    {
        var executableName = ExpectedExecutableName(runtimeIdentifier);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"bin/{executableName}"] = executable,
            ["laboratory.json"] = laboratoryJson,
            ["README.md"] = readme,
            ["evidence/system-bundle.zip"] = systemBundle
        };
        var payload = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, source) in sources)
        {
            ValidateRelativePath(path);
            payload[path] = await File.ReadAllBytesAsync(source, cancellationToken)
                .ConfigureAwait(false);
        }
        var hashes = payload.ToDictionary(
            item => item.Key,
            item => Hash(item.Value),
            StringComparer.Ordinal);
        var manifest = new LaboratoryPackageManifest(
            LaboratoryPackageManifest.CurrentVersion,
            laboratoryId,
            name,
            version,
            runtimeIdentifier,
            $"bin/{executableName}",
            "/laboratory/",
            buildId,
            bundleId,
            new SortedDictionary<string, string>(hashes, StringComparer.Ordinal));
        payload[ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

        var destinationPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporary = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                             81920, useAsync: true))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var (path, content) in payload)
                    {
                        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        await using var output = entry.Open();
                        await output.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                    }
                }
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return await VerifyAsync(destinationPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Weryfikuje strukturę i sumy wszystkich plików paczki.</summary>
    internal static async Task<LaboratoryPackageInspection> VerifyAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("Laboratory package was not found.", fullPath);
        if (info.Length > MaximumArchiveBytes) throw new InvalidDataException("Laboratory package is too large.");

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        await using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using (var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (archive.Entries.Count is < 5 or > MaximumEntries)
                throw new InvalidDataException("Laboratory package has an invalid entry count.");
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateRelativePath(entry.FullName);
                if (entries.ContainsKey(entry.FullName))
                    throw new InvalidDataException($"Duplicate package entry '{entry.FullName}'.");
                total += entry.Length;
                if (total > MaximumArchiveBytes)
                    throw new InvalidDataException("Laboratory package expands beyond its safety limit.");
                await using var source = entry.Open();
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                entries[entry.FullName] = buffer.ToArray();
            }
        }

        if (!entries.TryGetValue(ManifestPath, out var manifestBytes))
            throw new InvalidDataException("Laboratory package manifest is missing.");
        var manifest = JsonSerializer.Deserialize<LaboratoryPackageManifest>(manifestBytes)
            ?? throw new InvalidDataException("Laboratory package manifest is empty.");
        ValidateManifest(manifest, entries);
        return new LaboratoryPackageInspection(fullPath, manifest, manifest.Files.Count);
    }

    /// <summary>Rozpakowuje wcześniej zweryfikowaną paczkę do pustego katalogu.</summary>
    internal static async Task ExtractVerifiedAsync(
        string package,
        string destination,
        CancellationToken cancellationToken = default)
    {
        _ = await VerifyAsync(package, cancellationToken).ConfigureAwait(false);
        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        await using var input = File.OpenRead(Path.GetFullPath(package));
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            ValidateRelativePath(entry.FullName);
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Laboratory package entry escapes extraction root.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateManifest(
        LaboratoryPackageManifest manifest,
        IReadOnlyDictionary<string, byte[]> entries)
    {
        if (manifest.SchemaVersion != LaboratoryPackageManifest.CurrentVersion
            || string.IsNullOrWhiteSpace(manifest.LaboratoryId)
            || string.IsNullOrWhiteSpace(manifest.Name)
            || string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.RuntimeIdentifier)
            || string.IsNullOrWhiteSpace(manifest.Entrypoint)
            || string.IsNullOrWhiteSpace(manifest.PanelPath)
            || string.IsNullOrWhiteSpace(manifest.BuildId)
            || string.IsNullOrWhiteSpace(manifest.BundleId)
            || manifest.Files is null
            || manifest.BuildId.Length != 64
            || manifest.BundleId.Length != 64
            || manifest.BuildId.Any(character => !Uri.IsHexDigit(character))
            || manifest.BundleId.Any(character => !Uri.IsHexDigit(character))
            || !manifest.PanelPath.StartsWith("/", StringComparison.Ordinal)
            || manifest.Files.Count != entries.Count - 1)
            throw new InvalidDataException("Laboratory package manifest is invalid or unsupported.");
        ValidateRelativePath(manifest.Entrypoint);
        if (!manifest.Entrypoint.Equals(
                $"bin/{ExpectedExecutableName(manifest.RuntimeIdentifier)}",
                StringComparison.Ordinal))
            throw new InvalidDataException("Laboratory package entrypoint does not match its runtime identifier.");
        if (!manifest.Files.ContainsKey(manifest.Entrypoint))
            throw new InvalidDataException("Laboratory package entrypoint is not declared.");
        var payloadPaths = entries.Keys.Where(path => path != ManifestPath).Order(StringComparer.Ordinal).ToArray();
        if (!payloadPaths.SequenceEqual(manifest.Files.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Laboratory package manifest does not cover its exact payload.");
        foreach (var path in payloadPaths)
        {
            var expected = manifest.Files[path];
            if (string.IsNullOrWhiteSpace(expected)
                || expected.Length != 64
                || !Hash(entries[path]).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Laboratory package integrity failed for '{path}'.");
        }
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path)
            || path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe laboratory package path '{path}'.");
    }

    private static string ExpectedExecutableName(string runtimeIdentifier) => runtimeIdentifier switch
    {
        "win-x64" => "junexis-laboratory.exe",
        "linux-x64" or "osx-arm64" => "junexis-laboratory",
        _ => throw new InvalidDataException($"Nieobsługiwany RID paczki Laboratorium: '{runtimeIdentifier}'.")
    };

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
