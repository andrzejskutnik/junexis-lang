using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record ResolvedCliInput(
    FileInfo SourceFile,
    DirectoryInfo? OutDirectory,
    string[]? Targets,
    JunexisProject? Project,
    ProjectModuleCatalog? ModuleCatalog,
    ProviderPackageResolution ProviderPackages);
