using System.Text.RegularExpressions;
using System.Text.Json;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record ProviderPackageManifest(
    string ManifestSchemaVersion,
    string Id,
    string Version,
    string StorageContract,
    string RuntimeAbi,
    string ResourceKind,
    string DatabaseCompatibility,
    string[] Capabilities,
    string[] Files,
    ProviderRuntimeManifest Runtime,
    ProviderDriverManifest Driver,
    ProviderConfigurationManifest Configuration)
{
    internal const string FileName = "junexis.provider.toml";
    internal const string SchemaVersion = "junexis.provider/0.1";
    internal const string JsonFileName = "junexis.provider.json";
    internal const string JsonSchemaVersion = "junexis.provider/0.2";

    internal static async Task<ProviderPackageManifest> LoadAsync(FileInfo manifestFile)
    {
        if (!manifestFile.Exists)
            throw new FileNotFoundException($"Junexis provider manifest not found: {manifestFile.FullName}", manifestFile.FullName);

        var source = await File.ReadAllTextAsync(manifestFile.FullName);
        var parsed = manifestFile.Name.Equals(JsonFileName, StringComparison.Ordinal)
            ? ParseJson(source, manifestFile.FullName)
            : Parse(source, manifestFile.FullName);
        Validate(parsed, manifestFile.FullName);
        return parsed;
    }

    private static ProviderPackageManifest ParseJson(string source, string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(source);
            var root = document.RootElement;
            RequireJson(root, "schemaVersion", JsonSchemaVersion, manifestPath);
            var distribution = root.GetProperty("distribution");
            var runtime = distribution.GetProperty("runtime");
            var driver = distribution.GetProperty("driver");
            var configuration = distribution.GetProperty("configuration");
            return new ProviderPackageManifest(
                JsonSchemaVersion,
                JsonString(root, "id", manifestPath),
                JsonString(root, "version", manifestPath),
                JsonString(distribution, "storageContract", manifestPath),
                JsonString(root, "abiVersion", manifestPath),
                JsonString(root, "resourceKind", manifestPath),
                JsonString(distribution, "databaseCompatibility", manifestPath),
                JsonStrings(root, "capabilities", manifestPath),
                JsonStrings(distribution, "files", manifestPath),
                new ProviderRuntimeManifest(
                    JsonString(runtime, "kind", manifestPath),
                    JsonString(runtime, "entrypoint", manifestPath)),
                new ProviderDriverManifest(
                    JsonString(driver, "id", manifestPath),
                    JsonString(driver, "version", manifestPath)),
                new ProviderConfigurationManifest(
                    JsonStrings(configuration, "required", manifestPath),
                    JsonStrings(configuration, "optional", manifestPath),
                    JsonStrings(configuration, "secret", manifestPath)));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"Invalid provider JSON manifest: {manifestPath}", exception);
        }
    }

    private static ProviderPackageManifest Parse(string source, string manifestPath)
    {
        var sections = new Dictionary<string, ProviderTomlSection>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = new ProviderTomlSection()
        };
        var sectionName = string.Empty;
        var lineNumber = 0;
        foreach (var rawLine in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            lineNumber++;
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                sectionName = line[1..^1].Trim();
                if (sectionName is not ("runtime" or "driver" or "configuration"))
                    throw new InvalidDataException($"Unsupported provider TOML section '[{sectionName}]' at {manifestPath}:{lineNumber}");
                if (!sections.TryAdd(sectionName, new ProviderTomlSection()))
                    throw new InvalidDataException($"Duplicate provider TOML section '[{sectionName}]' at {manifestPath}:{lineNumber}");
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
                throw new InvalidDataException($"Invalid TOML assignment at {manifestPath}:{lineNumber}");
            var key = line[..equals].Trim();
            var rawValue = line[(equals + 1)..].Trim();
            var section = sections[sectionName];
            if (rawValue.StartsWith("[", StringComparison.Ordinal) && rawValue.EndsWith("]", StringComparison.Ordinal))
            {
                if (!section.Arrays.TryAdd(key, ParseStringArray(rawValue, manifestPath, lineNumber)))
                    throw new InvalidDataException($"Duplicate provider key '{key}' at {manifestPath}:{lineNumber}");
            }
            else if (!section.Values.TryAdd(key, ParseString(rawValue, manifestPath, lineNumber)))
            {
                throw new InvalidDataException($"Duplicate provider key '{key}' at {manifestPath}:{lineNumber}");
            }
        }

        var root = sections[string.Empty];
        var schema = Required(root.Values, "schemaVersion", manifestPath);
        if (!schema.Equals(SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported provider schema '{schema}': {manifestPath}");
        var runtime = RequiredSection(sections, "runtime", manifestPath);
        var driver = RequiredSection(sections, "driver", manifestPath);
        var configuration = RequiredSection(sections, "configuration", manifestPath);
        EnsureOnly(root.Values.Keys, ["schemaVersion", "id", "version", "storageContract", "runtimeAbi", "resourceKind", "databaseCompatibility"], string.Empty, manifestPath);
        EnsureOnly(root.Arrays.Keys, ["capabilities", "files"], string.Empty, manifestPath);
        EnsureOnly(runtime.Values.Keys, ["kind", "entrypoint"], "runtime", manifestPath);
        EnsureOnly(runtime.Arrays.Keys, [], "runtime", manifestPath);
        EnsureOnly(driver.Values.Keys, ["id", "version"], "driver", manifestPath);
        EnsureOnly(driver.Arrays.Keys, [], "driver", manifestPath);
        EnsureOnly(configuration.Values.Keys, [], "configuration", manifestPath);
        EnsureOnly(configuration.Arrays.Keys, ["required", "optional", "secret"], "configuration", manifestPath);

        return new ProviderPackageManifest(
            SchemaVersion,
            Required(root.Values, "id", manifestPath),
            Required(root.Values, "version", manifestPath),
            Required(root.Values, "storageContract", manifestPath),
            Required(root.Values, "runtimeAbi", manifestPath),
            Required(root.Values, "resourceKind", manifestPath),
            Required(root.Values, "databaseCompatibility", manifestPath),
            Required(root.Arrays, "capabilities", manifestPath),
            Required(root.Arrays, "files", manifestPath),
            new ProviderRuntimeManifest(
                Required(runtime.Values, "kind", manifestPath),
                Required(runtime.Values, "entrypoint", manifestPath)),
            new ProviderDriverManifest(
                Required(driver.Values, "id", manifestPath),
                Required(driver.Values, "version", manifestPath)),
            new ProviderConfigurationManifest(
                Required(configuration.Arrays, "required", manifestPath),
                Required(configuration.Arrays, "optional", manifestPath),
                Required(configuration.Arrays, "secret", manifestPath)));
    }

    private static string JsonString(JsonElement element, string property, string manifestPath)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Provider JSON manifest is missing '{property}': {manifestPath}");
        return value.GetString()!;
    }

    private static string[] JsonStrings(JsonElement element, string property, string manifestPath)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Provider JSON manifest is missing '{property}': {manifestPath}");
        return value.EnumerateArray().Select(item => item.GetString()
            ?? throw new InvalidDataException($"Provider JSON '{property}' contains a non-string: {manifestPath}"))
            .ToArray();
    }

    private static void RequireJson(JsonElement element, string property, string expected, string manifestPath)
    {
        var actual = JsonString(element, property, manifestPath);
        if (!actual.Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported provider schema '{actual}': {manifestPath}");
    }

    private static void Validate(ProviderPackageManifest manifest, string manifestPath)
    {
        if (!Regex.IsMatch(manifest.Id, "^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant))
            throw new InvalidDataException($"Provider id must use lowercase letters, digits, dots or hyphens: {manifestPath}");
        if (!Regex.IsMatch(manifest.Version, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant))
            throw new InvalidDataException($"Provider version must be semantic: {manifestPath}");
        if (!manifest.StorageContract.Equals("junexis.storage/0.1", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported storage contract '{manifest.StorageContract}': {manifestPath}");
        if (!manifest.RuntimeAbi.Equals("junexis.storage.runtime/0.1", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported storage runtime ABI '{manifest.RuntimeAbi}': {manifestPath}");
        if (string.IsNullOrWhiteSpace(manifest.ResourceKind) ||
            string.IsNullOrWhiteSpace(manifest.DatabaseCompatibility) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.Kind) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.Entrypoint) ||
            string.IsNullOrWhiteSpace(manifest.Driver.Id) ||
            string.IsNullOrWhiteSpace(manifest.Driver.Version))
            throw new InvalidDataException($"Provider manifest contains an empty required value: {manifestPath}");
        if (manifest.Capabilities.Length == 0 || manifest.Capabilities.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Provider must declare capabilities: {manifestPath}");
        if (manifest.Files.Any(string.IsNullOrWhiteSpace) ||
            manifest.Configuration.Required.Concat(manifest.Configuration.Optional).Concat(manifest.Configuration.Secret)
                .Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Provider manifest contains an empty list value: {manifestPath}");
        EnsureDistinct(manifest.Capabilities, "capability", manifestPath);
        EnsureDistinct(manifest.Files, "file", manifestPath);

        var knownConfiguration = manifest.Configuration.Required
            .Concat(manifest.Configuration.Optional)
            .ToArray();
        EnsureDistinct(knownConfiguration, "configuration key", manifestPath);
        EnsureDistinct(manifest.Configuration.Secret, "secret configuration key", manifestPath);
        foreach (var secret in manifest.Configuration.Secret)
        {
            if (!knownConfiguration.Contains(secret, StringComparer.Ordinal))
                throw new InvalidDataException($"Secret configuration key '{secret}' is not required or optional: {manifestPath}");
        }
    }

    private static void EnsureDistinct(IEnumerable<string> values, string label, string manifestPath)
    {
        var duplicates = values.GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null)
            throw new InvalidDataException($"Duplicate provider {label} '{duplicates.Key}': {manifestPath}");
    }

    private static void EnsureOnly(
        IEnumerable<string> actual,
        IReadOnlyCollection<string> allowed,
        string section,
        string manifestPath)
    {
        var unknown = actual.FirstOrDefault(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase));
        if (unknown is not null)
        {
            var location = section.Length == 0 ? "root" : $"[{section}]";
            throw new InvalidDataException($"Unknown provider key '{unknown}' in {location}: {manifestPath}");
        }
    }

    private static ProviderTomlSection RequiredSection(
        IReadOnlyDictionary<string, ProviderTomlSection> sections,
        string name,
        string manifestPath)
    {
        if (!sections.TryGetValue(name, out var section))
            throw new InvalidDataException($"Provider manifest is missing '[{name}]': {manifestPath}");
        return section;
    }

    private static T Required<T>(IReadOnlyDictionary<string, T> values, string key, string manifestPath)
    {
        if (!values.TryGetValue(key, out var value))
            throw new InvalidDataException($"Provider manifest is missing '{key}': {manifestPath}");
        return value;
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"' && (index == 0 || line[index - 1] != '\\'))
                inString = !inString;
            else if (character == '#' && !inString)
                return line[..index];
        }
        return line;
    }

    private static string ParseString(string value, string manifestPath, int lineNumber)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            throw new InvalidDataException($"Expected TOML string at {manifestPath}:{lineNumber}");
        return value[1..^1]
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string[] ParseStringArray(string value, string manifestPath, int lineNumber)
    {
        var body = value[1..^1].Trim();
        if (body.Length == 0)
            return [];
        return body.Split(',')
            .Select(item => ParseString(item.Trim(), manifestPath, lineNumber))
            .ToArray();
    }

    private sealed class ProviderTomlSection
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, string[]> Arrays { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record ProviderRuntimeManifest(string Kind, string Entrypoint);

internal sealed record ProviderDriverManifest(string Id, string Version);

internal sealed record ProviderConfigurationManifest(string[] Required, string[] Optional, string[] Secret);
