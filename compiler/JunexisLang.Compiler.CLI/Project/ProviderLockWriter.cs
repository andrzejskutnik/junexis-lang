using System.Text.Json;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Project;

internal static class ProviderLockWriter
{
    internal static GeneratedFile Generate(ProviderPackageResolution resolution, DirectoryInfo projectRoot)
    {
        var providers = resolution.Packages
            .OrderBy(package => package.Manifest.Id, StringComparer.Ordinal)
            .Select(package => new
            {
                id = package.Manifest.Id,
                version = package.Manifest.Version,
                providerSchema = package.Manifest.ManifestSchemaVersion,
                storageContract = package.Manifest.StorageContract,
                runtimeAbi = package.Manifest.RuntimeAbi,
                resourceKind = package.Manifest.ResourceKind,
                databaseCompatibility = package.Manifest.DatabaseCompatibility,
                capabilities = package.Manifest.Capabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                runtime = new
                {
                    kind = package.Manifest.Runtime.Kind,
                    entrypoint = package.Manifest.Runtime.Entrypoint
                },
                driver = new
                {
                    id = package.Manifest.Driver.Id,
                    version = package.Manifest.Driver.Version
                },
                source = package.Source,
                manifestSha256 = package.ManifestSha256,
                packageSha256 = package.PackageSha256,
                files = package.Files.Select(file => new { path = file.Path, sha256 = file.Sha256 }).ToArray()
            })
            .ToArray();
        var bindings = resolution.Bindings
            .OrderBy(binding => binding.Binding.Name, StringComparer.Ordinal)
            .Select(binding => new
            {
                name = binding.Binding.Name,
                declaration = binding.Binding.Declaration,
                provider = binding.Package.Manifest.Id,
                providerVersion = binding.Package.Manifest.Version,
                packageSha256 = binding.Package.PackageSha256,
                configuration = binding.Configuration.Select(value => new
                {
                    name = value.Name,
                    source = value.Source,
                    reference = value.Reference,
                    value = value.Value
                }).ToArray()
            })
            .ToArray();
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.providers.lock/0.1",
            providers,
            bindings
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        }) + "\n";
        return new GeneratedFile("debug", "debug/junexis.providers.lock.json", json);
    }
}
