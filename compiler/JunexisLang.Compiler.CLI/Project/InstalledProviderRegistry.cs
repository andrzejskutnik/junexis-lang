using System.Security.Cryptography;
using System.Text.Json;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record InstalledProviderPackage(DirectoryInfo Directory);

internal static class InstalledProviderRegistry
{
    private const string RegistrySchema = "junexis.provider-registry/0.1";

    internal static async Task<InstalledProviderPackage> ResolveAsync(
        string id,
        string providerVersion,
        string sdkVersion)
    {
        var root = ResolveRoot();
        var registryPath = Path.Combine(root, "registry.json");
        if (!File.Exists(registryPath))
            throw new InvalidDataException(
                $"Installed provider registry not found. Run 'junexis provider install --id {id} --sdk-version {sdkVersion}'.");

        using var registry = JsonDocument.Parse(await File.ReadAllTextAsync(registryPath));
        RequireString(registry.RootElement, "schemaVersion", RegistrySchema, registryPath);
        if (!registry.RootElement.TryGetProperty("providers", out var providers)
            || providers.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Provider registry has no 'providers' array: {registryPath}");

        var matches = providers.EnumerateArray()
            .Where(item => String(item, "id", registryPath) == id
                && String(item, "providerVersion", registryPath) == providerVersion
                && String(item, "sdkVersion", registryPath) == sdkVersion)
            .ToArray();
        if (matches.Length == 0)
            throw new InvalidDataException(
                $"Installed provider '{id}' version '{providerVersion}' for SDK '{sdkVersion}' was not found. " +
                $"Run 'junexis provider install --id {id} --sdk-version {sdkVersion}'.");
        if (matches.Length > 1)
            throw new InvalidDataException(
                $"Provider registry contains duplicate '{id}' version '{providerVersion}' for SDK '{sdkVersion}'.");

        var entry = matches[0];
        RequireString(entry, "contract", "junexis.provider/0.2", registryPath);
        RequireString(entry, "abiVersion", "junexis.storage.runtime/0.1", registryPath);
        var relative = String(entry, "path", registryPath).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException($"Provider registry contains an absolute package path: {registryPath}");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var packagePath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!packagePath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Provider registry path escapes its profile: {registryPath}");
        var directory = new DirectoryInfo(packagePath);
        if (!directory.Exists)
            throw new InvalidDataException($"Installed provider directory not found: {packagePath}");

        await VerifyReleaseAsync(directory, id, providerVersion, sdkVersion);
        return new InstalledProviderPackage(directory);
    }

    private static async Task VerifyReleaseAsync(
        DirectoryInfo directory,
        string id,
        string providerVersion,
        string sdkVersion)
    {
        var releasePath = Path.Combine(directory.FullName, "junexis.provider.release.json");
        var manifestPath = Path.Combine(directory.FullName, ProviderPackageManifest.JsonFileName);
        if (!File.Exists(releasePath) || !File.Exists(manifestPath))
            throw new InvalidDataException($"Installed provider '{id}' has no M16.8 release receipt.");
        using var release = JsonDocument.Parse(await File.ReadAllTextAsync(releasePath));
        var root = release.RootElement;
        RequireString(root, "schemaVersion", "junexis.provider.release/0.1", releasePath);
        RequireString(root, "platformVersion", sdkVersion, releasePath);
        var provider = root.GetProperty("provider");
        RequireString(provider, "id", id, releasePath);
        RequireString(provider, "version", providerVersion, releasePath);
        var sdk = root.GetProperty("sdk");
        RequireString(sdk, "packageVersion", sdkVersion, releasePath);
        RequireString(sdk, "abiVersion", "junexis.storage.runtime/0.1", releasePath);
        var expectedHash = String(provider, "manifestSha256", releasePath);
        using var stream = File.OpenRead(manifestPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actualHash.Equals(expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException($"Installed provider '{id}' manifest differs from its release receipt.");
    }

    private static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable("JUNEXIS_PROVIDER_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Junexis", "providers");
    }

    private static string String(JsonElement element, string property, string path)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Provider registry is missing '{property}': {path}");
        return value.GetString()!;
    }

    private static void RequireString(JsonElement element, string property, string expected, string path)
    {
        var actual = String(element, property, path);
        if (!actual.Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Provider registry field '{property}' must be '{expected}', but is '{actual}': {path}");
    }
}
