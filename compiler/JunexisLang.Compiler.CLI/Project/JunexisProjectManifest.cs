using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record JunexisProjectManifest(
    string Name,
    string Entry,
    string[] SourceRoots,
    string[] LocalLibraries,
    string[] Packages,
    string? PackageIndex,
    string[] ProviderPackages,
    string? ProviderSdkVersion,
    StorageBindingManifest[] StorageBindings,
    string[] Targets,
    string OutDir)
{
    internal const string FileName = "junexis.project.toml";

    internal static async Task<JunexisProjectManifest> LoadAsync(FileInfo manifestFile)
    {
        if (!manifestFile.Exists)
            throw new FileNotFoundException($"Junexis project manifest not found: {manifestFile.FullName}", manifestFile.FullName);

        var manifest = Parse(await File.ReadAllTextAsync(manifestFile.FullName), manifestFile.FullName);
        Validate(manifest, manifestFile.FullName);
        return manifest;
    }

    internal static async Task<JunexisProject?> DiscoverAsync(DirectoryInfo startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory.FullName));
        while (directory is not null)
        {
            var manifestFile = new FileInfo(Path.Combine(directory.FullName, FileName));
            if (manifestFile.Exists)
            {
                var manifest = await LoadAsync(manifestFile);
                return new JunexisProject(directory, manifestFile, manifest);
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static JunexisProjectManifest Parse(string source, string manifestPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var arrays = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var storageBindings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string? section = null;

        var lineNumber = 0;
        foreach (var rawLine in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            lineNumber++;
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line[1..^1].Trim();
                if (!section.StartsWith("storage.", StringComparison.OrdinalIgnoreCase) ||
                    section.Length == "storage.".Length)
                    throw new InvalidDataException($"Unsupported TOML section '[{section}]' at {manifestPath}:{lineNumber}");
                var bindingName = section["storage.".Length..];
                if (!storageBindings.TryAdd(bindingName, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Duplicate storage binding '{bindingName}' at {manifestPath}:{lineNumber}");
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
                throw new InvalidDataException($"Invalid TOML assignment at {manifestPath}:{lineNumber}");

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Length == 0)
                throw new InvalidDataException($"Invalid empty TOML key at {manifestPath}:{lineNumber}");

            if (section is not null)
            {
                if (value.StartsWith("[", StringComparison.Ordinal))
                    throw new InvalidDataException($"Storage binding values must be TOML strings at {manifestPath}:{lineNumber}");
                var bindingName = section["storage.".Length..];
                if (!storageBindings[bindingName].TryAdd(key, ParseString(value, manifestPath, lineNumber)))
                    throw new InvalidDataException($"Duplicate storage binding key '{key}' at {manifestPath}:{lineNumber}");
            }
            else if (value.StartsWith("[", StringComparison.Ordinal) &&
                     value.EndsWith("]", StringComparison.Ordinal))
                arrays[key] = ParseStringArray(value, manifestPath, lineNumber);
            else
                values[key] = ParseString(value, manifestPath, lineNumber);
        }

        return new JunexisProjectManifest(
            GetRequired(values, "name", manifestPath),
            GetRequired(values, "entry", manifestPath),
            arrays.TryGetValue("sourceRoots", out var sourceRoots) ? sourceRoots : ["src"],
            arrays.TryGetValue("localLibraries", out var localLibraries) ? localLibraries : [],
            arrays.TryGetValue("packages", out var packages) ? packages : [],
            values.TryGetValue("packageIndex", out var packageIndex) ? packageIndex : null,
            arrays.TryGetValue("providerPackages", out var providerPackages) ? providerPackages : [],
            values.TryGetValue("providerSdkVersion", out var providerSdkVersion) ? providerSdkVersion : null,
            storageBindings.OrderBy(binding => binding.Key, StringComparer.Ordinal)
                .Select(binding => StorageBindingManifest.Parse(binding.Key, binding.Value, manifestPath))
                .ToArray(),
            arrays.TryGetValue("targets", out var targets) ? targets : Array.Empty<string>(),
            GetRequired(values, "outDir", manifestPath));
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
            return Array.Empty<string>();

        return body.Split(',')
            .Select(item => ParseString(item.Trim(), manifestPath, lineNumber))
            .ToArray();
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> values, string key, string manifestPath)
    {
        if (!values.TryGetValue(key, out var value))
            throw new InvalidDataException($"Junexis project manifest is missing '{key}': {manifestPath}");

        return value;
    }

    private static void Validate(JunexisProjectManifest manifest, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException($"Junexis project manifest has empty 'name': {manifestPath}");

        if (string.IsNullOrWhiteSpace(manifest.Entry))
            throw new InvalidDataException($"Junexis project manifest has empty 'entry': {manifestPath}");

        if (string.IsNullOrWhiteSpace(manifest.OutDir))
            throw new InvalidDataException($"Junexis project manifest has empty 'outDir': {manifestPath}");

        if (manifest.SourceRoots is null || manifest.SourceRoots.Length == 0 ||
            manifest.SourceRoots.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Junexis project manifest has invalid 'sourceRoots': {manifestPath}");

        foreach (var library in manifest.LocalLibraries ?? [])
            LocalLibraryReference.Parse(library);

        var packages = manifest.Packages ?? [];
        foreach (var package in packages)
            PackageReference.Parse(package);
        if (packages.Length > 0 && string.IsNullOrWhiteSpace(manifest.PackageIndex))
            throw new InvalidDataException(
                $"Project packages require 'packageIndex': {manifestPath}");
        if (manifest.PackageIndex is not null && Path.IsPathRooted(manifest.PackageIndex))
            throw new InvalidDataException(
                $"Project 'packageIndex' must be relative to the project: {manifestPath}");

        var providerPackages = manifest.ProviderPackages ?? [];
        var storageBindings = manifest.StorageBindings ?? [];
        foreach (var providerPackage in providerPackages)
        {
            if (string.IsNullOrWhiteSpace(providerPackage) || Path.IsPathRooted(providerPackage))
                throw new InvalidDataException($"Provider package path must be relative to the project: {providerPackage}");
        }

        if (providerPackages.Length > 0 && manifest.ProviderSdkVersion is not null)
            throw new InvalidDataException(
                $"Project must choose local 'providerPackages' or 'providerSdkVersion', not both: {manifestPath}");
        if (providerPackages.Length == 0 && storageBindings.Length > 0
            && string.IsNullOrWhiteSpace(manifest.ProviderSdkVersion))
            throw new InvalidDataException(
                $"Installed storage providers require 'providerSdkVersion': {manifestPath}");
        if (manifest.ProviderSdkVersion is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(
                manifest.ProviderSdkVersion,
                "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException(
                $"Project 'providerSdkVersion' must be semantic: {manifestPath}");

        if (manifest.Targets is null || manifest.Targets.Length == 0)
            throw new InvalidDataException($"Junexis project manifest has empty 'targets': {manifestPath}");

        var emptyTarget = manifest.Targets.Any(string.IsNullOrWhiteSpace);
        if (emptyTarget)
            throw new InvalidDataException($"Junexis project manifest contains an empty target: {manifestPath}");
    }
}

internal sealed record StorageBindingManifest(
    string Name,
    string Declaration,
    string Provider,
    string ProviderVersion,
    IReadOnlyDictionary<string, string> Configuration)
{
    internal static StorageBindingManifest Parse(
        string name,
        IReadOnlyDictionary<string, string> values,
        string manifestPath)
    {
        var declaration = GetRequired("declaration");
        var provider = GetRequired("provider");
        var providerVersion = GetRequired("providerVersion");
        var configuration = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (pair.Key.Equals("declaration", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Equals("provider", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Equals("providerVersion", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!pair.Key.StartsWith("config.", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Length == "config.".Length)
                throw new InvalidDataException(
                    $"Storage binding '{name}' has unsupported key '{pair.Key}': {manifestPath}");
            configuration.Add(pair.Key["config.".Length..], pair.Value);
        }

        return new StorageBindingManifest(name, declaration, provider, providerVersion, configuration);

        string GetRequired(string key)
        {
            if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"Storage binding '{name}' is missing '{key}': {manifestPath}");
            return value;
        }
    }
}

internal sealed record LocalLibraryReference(string Namespace, string SourcePath)
{
    internal static LocalLibraryReference Parse(string value)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1)
            throw new InvalidDataException($"Local library must use 'namespace=path': {value}");
        var libraryNamespace = value[..separator].Trim();
        var sourcePath = value[(separator + 1)..].Trim();
        _ = ModuleIdentity.FromRelativeSourcePath($"{libraryNamespace}.jnx");
        if (Path.IsPathRooted(sourcePath))
            throw new InvalidDataException($"Local library path must be relative to the project: {sourcePath}");
        return new LocalLibraryReference(libraryNamespace, sourcePath);
    }
}

internal sealed record PackageReference(string Id, string Version)
{
    private static readonly System.Text.RegularExpressions.Regex PackageIdPattern = new(
        "^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex VersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\\+([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static PackageReference Parse(string value)
    {
        var separator = value.LastIndexOf('@');
        if (separator <= 0 || separator == value.Length - 1)
            throw new InvalidDataException($"Package reference must use 'id@exact-version': {value}");

        var id = value[..separator].Trim();
        var version = value[(separator + 1)..].Trim();
        if (!PackageIdPattern.IsMatch(id) || !VersionPattern.IsMatch(version))
            throw new InvalidDataException($"Package reference must use 'id@exact-version': {value}");

        return new PackageReference(id, version);
    }
}
