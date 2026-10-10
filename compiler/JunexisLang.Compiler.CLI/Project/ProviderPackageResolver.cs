using System.Security.Cryptography;
using System.Text;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record ResolvedProviderFile(string Path, string Sha256);

internal sealed record ResolvedProviderPackage(
    DirectoryInfo PackageDirectory,
    FileInfo ManifestFile,
    string Source,
    ProviderPackageManifest Manifest,
    string ManifestSha256,
    string PackageSha256,
    IReadOnlyList<ResolvedProviderFile> Files);

internal sealed record ResolvedStorageBinding(
    StorageBindingManifest Binding,
    ResolvedProviderPackage Package,
    IReadOnlyList<ResolvedBindingConfiguration> Configuration);

internal sealed record ResolvedBindingConfiguration(
    string Name,
    string Source,
    string? Reference,
    string? Value);

internal sealed record ProviderPackageResolution(
    IReadOnlyList<ResolvedProviderPackage> Packages,
    IReadOnlyList<ResolvedStorageBinding> Bindings)
{
    internal static ProviderPackageResolution Empty { get; } = new([], []);
}

internal static class ProviderPackageResolver
{
    internal static async Task<ProviderPackageResolution> ResolveAsync(JunexisProject project)
    {
        if (project.Manifest.ProviderPackages.Length == 0 && project.Manifest.StorageBindings.Length == 0)
            return ProviderPackageResolution.Empty;

        var packages = new List<ResolvedProviderPackage>();
        if (project.Manifest.ProviderPackages.Length > 0)
        {
            foreach (var relativePath in project.Manifest.ProviderPackages.OrderBy(path => path, StringComparer.Ordinal))
            {
                var packagePath = ResolveLocalPackagePath(project.RootDirectory, relativePath);
                packages.Add(await ResolvePackageAsync(
                    packagePath,
                    relativePath.Replace('\\', '/')));
            }
        }
        else
        {
            foreach (var requested in project.Manifest.StorageBindings
                         .Select(binding => (binding.Provider, binding.ProviderVersion))
                         .Distinct()
                         .OrderBy(item => item.Provider, StringComparer.Ordinal))
            {
                var installed = await InstalledProviderRegistry.ResolveAsync(
                    requested.Provider,
                    requested.ProviderVersion,
                    project.Manifest.ProviderSdkVersion!);
                packages.Add(await ResolvePackageAsync(
                    installed.Directory,
                    $"profile:{requested.Provider}@{requested.ProviderVersion}/sdk:{project.Manifest.ProviderSdkVersion}"));
            }
        }

        var duplicateProvider = packages.GroupBy(package => package.Manifest.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProvider is not null)
            throw new InvalidDataException($"Duplicate provider package id '{duplicateProvider.Key}'.");

        var bindings = new List<ResolvedStorageBinding>();
        foreach (var binding in project.Manifest.StorageBindings.OrderBy(binding => binding.Name, StringComparer.Ordinal))
        {
            var package = packages.SingleOrDefault(candidate =>
                candidate.Manifest.Id.Equals(binding.Provider, StringComparison.Ordinal));
            if (package is null)
                throw new InvalidDataException($"Storage binding '{binding.Name}' references unknown provider '{binding.Provider}'.");
            if (!package.Manifest.Version.Equals(binding.ProviderVersion, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Storage binding '{binding.Name}' requires provider '{binding.Provider}' version " +
                    $"'{binding.ProviderVersion}', but the local package is '{package.Manifest.Version}'.");

            bindings.Add(new ResolvedStorageBinding(
                binding,
                package,
                ResolveConfiguration(binding, package.Manifest)));
        }

        return new ProviderPackageResolution(packages, bindings);
    }

    private static DirectoryInfo ResolveLocalPackagePath(DirectoryInfo projectRoot, string relativePath)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot.FullName));
        var packagePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!packagePath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Provider package escapes the project: {relativePath}");
        var packageDirectory = new DirectoryInfo(packagePath);
        if (!packageDirectory.Exists)
            throw new InvalidDataException($"Provider package directory not found: {relativePath}");
        return packageDirectory;
    }

    private static async Task<ResolvedProviderPackage> ResolvePackageAsync(
        DirectoryInfo packageDirectory,
        string source)
    {
        var packagePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory.FullName));
        var jsonManifest = new FileInfo(Path.Combine(packagePath, ProviderPackageManifest.JsonFileName));
        var tomlManifest = new FileInfo(Path.Combine(packagePath, ProviderPackageManifest.FileName));
        var manifestFile = jsonManifest.Exists ? jsonManifest : tomlManifest;
        var manifest = await ProviderPackageManifest.LoadAsync(manifestFile);
        var filePaths = manifest.Files
            .Append(manifest.Runtime.Entrypoint)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var files = new List<ResolvedProviderFile>();
        foreach (var declaredPath in filePaths)
        {
            var normalized = declaredPath.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(declaredPath) || Path.IsPathRooted(normalized))
                throw new InvalidDataException($"Provider '{manifest.Id}' declares an invalid file path: {declaredPath}");
            var fullPath = Path.GetFullPath(Path.Combine(packagePath, normalized));
            if (!fullPath.StartsWith(packagePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Provider '{manifest.Id}' file escapes its package: {declaredPath}");
            if (!File.Exists(fullPath))
                throw new InvalidDataException($"Provider '{manifest.Id}' declared file not found: {declaredPath}");
            files.Add(new ResolvedProviderFile(
                declaredPath.Replace('\\', '/'),
                ComputeSha256(await File.ReadAllBytesAsync(fullPath))));
        }

        var manifestSha256 = ComputeSha256(await File.ReadAllBytesAsync(manifestFile.FullName));
        var packageIdentity = new StringBuilder()
            .Append(manifestFile.Name).Append('\0').Append(manifestSha256).Append('\n');
        foreach (var file in files)
            packageIdentity.Append(file.Path).Append('\0').Append(file.Sha256).Append('\n');
        var packageSha256 = ComputeSha256(Encoding.UTF8.GetBytes(packageIdentity.ToString()));

        return new ResolvedProviderPackage(
            packageDirectory,
            manifestFile,
            source,
            manifest,
            manifestSha256,
            packageSha256,
            files);
    }

    private static IReadOnlyList<ResolvedBindingConfiguration> ResolveConfiguration(
        StorageBindingManifest binding,
        ProviderPackageManifest provider)
    {
        var known = provider.Configuration.Required
            .Concat(provider.Configuration.Optional)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in binding.Configuration.Keys)
        {
            if (!known.Contains(key))
                throw new InvalidDataException(
                    $"Storage binding '{binding.Name}' configures unknown key '{key}' for provider '{provider.Id}'.");
        }
        foreach (var required in provider.Configuration.Required)
        {
            if (!binding.Configuration.ContainsKey(required))
                throw new InvalidDataException(
                    $"Storage binding '{binding.Name}' is missing required provider configuration '{required}'.");
        }

        var secret = provider.Configuration.Secret.ToHashSet(StringComparer.Ordinal);
        var resolved = new List<ResolvedBindingConfiguration>();
        foreach (var pair in binding.Configuration.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var separator = pair.Value.IndexOf(':');
            var candidateSource = separator > 0 ? pair.Value[..separator] : string.Empty;
            var hasExplicitSource = candidateSource is "secret" or "env" or "literal";
            var source = hasExplicitSource ? candidateSource : "literal";
            var payload = hasExplicitSource ? pair.Value[(separator + 1)..] : pair.Value;
            if (string.IsNullOrWhiteSpace(payload))
                throw new InvalidDataException(
                    $"Storage binding '{binding.Name}' configuration '{pair.Key}' has an empty value or reference.");
            if (secret.Contains(pair.Key) && source == "literal")
                throw new InvalidDataException(
                    $"Storage binding '{binding.Name}' configuration '{pair.Key}' is secret and must use secret: or env:.");

            resolved.Add(new ResolvedBindingConfiguration(
                pair.Key,
                source,
                source == "literal" ? null : payload,
                source == "literal" ? payload : null));
        }
        return resolved;
    }

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
