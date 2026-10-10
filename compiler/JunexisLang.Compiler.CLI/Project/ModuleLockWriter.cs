using System.Security.Cryptography;
using System.Text.Json;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.CLI.Project;

internal static class ModuleLockWriter
{
    internal static GeneratedFile Generate(ProjectModuleCatalog catalog, DirectoryInfo projectRoot)
    {
        var modules = catalog.Modules.Select(module => new
        {
            identity = module.Identity.Value,
            source = Path.GetRelativePath(projectRoot.FullName, module.SourceFile.FullName).Replace('\\', '/'),
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.SourceFile.FullName))).ToLowerInvariant()
        }).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.modules.lock/0.1",
            modules
        }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        return new GeneratedFile("debug", "debug/junexis.modules.lock.json", json);
    }
}
