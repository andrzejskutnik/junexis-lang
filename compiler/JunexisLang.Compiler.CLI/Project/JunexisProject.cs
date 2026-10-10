using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed record JunexisProject(
    DirectoryInfo RootDirectory,
    FileInfo ManifestFile,
    JunexisProjectManifest Manifest)
{
    internal FileInfo EntryFile =>
        new(Path.GetFullPath(Path.Combine(RootDirectory.FullName, Manifest.Entry)));

    internal DirectoryInfo OutDirectory =>
        new(Path.GetFullPath(Path.Combine(RootDirectory.FullName, Manifest.OutDir)));

    internal async Task<ProjectModuleCatalog> DiscoverModulesAsync(IReadOnlyCollection<string>? effectiveTargets = null)
    {
        var projectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootDirectory.FullName));
        var roots = Manifest.SourceRoots.Select(path =>
        {
            var resolved = Path.GetFullPath(Path.Combine(projectRoot, path));
            if (!resolved.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Project source root escapes the project: {path}");
            return new ModuleSourceRoot(new DirectoryInfo(resolved));
        }).ToList();
        roots.AddRange(Manifest.LocalLibraries.Select(LocalLibraryReference.Parse).Select(library =>
            new ModuleSourceRoot(
                new DirectoryInfo(Path.GetFullPath(Path.Combine(RootDirectory.FullName, library.SourcePath))),
                new ModuleIdentity(library.Namespace))));
        if (Manifest.Packages.Length > 0)
        {
            var lockedPackages = await PackageRestoreResolver.RestoreAsync(this, offline: true, effectiveTargets);
            foreach (var package in lockedPackages)
            {
                roots.Add(new ModuleSourceRoot(
                    new DirectoryInfo(PackageRestoreResolver.GetModulesRoot(this, package.Id, package.Version))));
            }
        }
        return ProjectModuleCatalog.Discover(roots);
    }
}
