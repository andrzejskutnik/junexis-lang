using System.Text.Json;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record PackagesLockDocument(string SchemaVersion, PackageLockEntry[] Packages);

internal sealed record PackageLockEntry(
    string Id,
    string Version,
    string Namespace,
    string Source,
    string ManifestSha256,
    string ArtifactSha256,
    PackageDependency[] Dependencies);

internal static class PackagesLockFile
{
    internal const string FileName = "junexis.packages.lock";

    internal static async Task<PackagesLockDocument> LoadAsync(DirectoryInfo projectRoot)
    {
        var path = Path.Combine(projectRoot.FullName, FileName);
        if (!File.Exists(path))
            throw new InvalidDataException($"Package lockfile not found: {path}. Run 'junexis restore'.");

        var document = PackageJson.Deserialize<PackagesLockDocument>(await PackageJson.ReadTextAsync(path), path);
        PackageContractValidator.Validate(document);
        return document;
    }

    internal static async Task WriteAsync(DirectoryInfo projectRoot, IEnumerable<PackageLockEntry> packages)
    {
        var document = new PackagesLockDocument(
            "junexis.packages.lock/0.1",
            packages.OrderBy(package => package.Id, StringComparer.Ordinal)
                .ThenBy(package => package.Version, StringComparer.Ordinal)
                .Select(package => package with
                {
                    Dependencies = package.Dependencies
                        .OrderBy(dependency => dependency.Id, StringComparer.Ordinal)
                        .ThenBy(dependency => dependency.Version, StringComparer.Ordinal)
                        .ToArray()
                })
                .ToArray());
        var content = JsonSerializer.Serialize(document, PackageJson.Options) + "\n";
        var destination = Path.Combine(projectRoot.FullName, FileName);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, content);
        File.Move(temporary, destination, overwrite: true);
    }
}