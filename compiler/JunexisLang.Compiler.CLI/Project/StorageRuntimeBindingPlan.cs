using System.Diagnostics;
using System.Text.Json;

namespace JunexisLang.Compiler.CLI.Project;

/// <summary>A verified storage binding ready for local launch or bundle materialization.</summary>
internal sealed record StorageRuntimeBindingPlan(
    string RuntimeHostPath,
    ResolvedStorageBinding Binding,
    IReadOnlyDictionary<string, string>? ResolvedConfiguration)
{
    internal string ProviderEntrypointPath => Path.GetFullPath(Path.Combine(
        Binding.Package.PackageDirectory.FullName,
        Binding.Package.Manifest.Runtime.Entrypoint.Replace('/', Path.DirectorySeparatorChar)));

    internal string LockPath { get; init; } = string.Empty;

    internal void ApplyTo(ProcessStartInfo startInfo)
    {
        if (ResolvedConfiguration is null)
            throw new InvalidOperationException("Storage binding configuration was not resolved for launch.");
        startInfo.Environment["JUNEXIS_STORAGE_RUNTIME_HOST"] = RuntimeHostPath;
        startInfo.Environment["JUNEXIS_STORAGE_PROVIDER_ENTRYPOINT"] = ProviderEntrypointPath;
        startInfo.Environment["JUNEXIS_STORAGE_PROVIDER"] = Binding.Package.Manifest.Id;
        startInfo.Environment["JUNEXIS_STORAGE_BINDING"] = Binding.Binding.Name;
        startInfo.Environment["JUNEXIS_STORAGE_CONFIG_JSON"] =
            JsonSerializer.Serialize(ResolvedConfiguration);
    }
}

/// <summary>Verifies the provider lock and resolves a deployment binding without logging values.</summary>
internal static class StorageRuntimeBindingPlanner
{
    internal static async Task<StorageRuntimeBindingPlan?> CreateAsync(
        ResolvedCliInput resolved,
        string? runtimeHostPath,
        bool resolveConfiguration)
    {
        if (resolved.ProviderPackages.Bindings.Count == 0)
            return null;
        if (resolved.Project is null)
            throw new InvalidDataException("Storage Runtime bindings require project mode.");
        if (resolved.ProviderPackages.Bindings.Count != 1)
            throw new InvalidDataException(
                "M12.8 launcher supports exactly one storage binding per generated server process.");

        var configuredHost = string.IsNullOrWhiteSpace(runtimeHostPath)
            ? Environment.GetEnvironmentVariable("JUNEXIS_STORAGE_RUNTIME_HOST")
            : runtimeHostPath;
        if (string.IsNullOrWhiteSpace(configuredHost))
            throw new InvalidDataException(
                "Storage Runtime host is required; use --storage-runtime-host or JUNEXIS_STORAGE_RUNTIME_HOST.");
        var host = Path.GetFullPath(configuredHost);
        var hostFile = new FileInfo(host);
        if (!hostFile.Exists || (hostFile.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Storage Runtime host must be an existing regular file.");

        // Resolve again after build so a package changed between lock creation and launch is rejected.
        var current = await ProviderPackageResolver.ResolveAsync(resolved.Project).ConfigureAwait(false);
        var expectedLock = ProviderLockWriter.Generate(current, resolved.Project.RootDirectory).Content;
        var lockPath = Path.Combine(
            resolved.OutDirectory!.FullName,
            "debug",
            "junexis.providers.lock.json");
        if (!File.Exists(lockPath))
            throw new InvalidDataException("Provider lockfile is missing; run a successful bound build first.");
        var actualLock = await File.ReadAllTextAsync(lockPath).ConfigureAwait(false);
        if (!actualLock.Equals(expectedLock, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Provider package or binding differs from junexis.providers.lock/0.1; rebuild before launch.");

        var binding = current.Bindings.Single();
        IReadOnlyDictionary<string, string>? values = null;
        if (resolveConfiguration)
            values = ResolveConfiguration(binding);
        return new StorageRuntimeBindingPlan(host, binding, values) { LockPath = lockPath };
    }

    private static IReadOnlyDictionary<string, string> ResolveConfiguration(
        ResolvedStorageBinding binding)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in binding.Configuration)
        {
            if (item.Source == "literal")
            {
                values[item.Name] = item.Value!;
                continue;
            }
            var value = Environment.GetEnvironmentVariable(item.Reference!);
            if (string.IsNullOrEmpty(value))
                throw new InvalidDataException(
                    $"Storage binding '{binding.Binding.Name}' requires {item.Source} reference '{item.Reference}'.");
            values[item.Name] = value;
        }
        return values;
    }
}
