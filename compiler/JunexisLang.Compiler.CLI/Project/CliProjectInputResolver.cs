using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.CLI.Project;

internal static class CliProjectInputResolver
{
    internal static async Task<ResolvedCliInput> ResolveAsync(
        string? inputPath,
        DirectoryInfo? explicitOutDir = null,
        string[]? explicitTargets = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return await ResolveProjectAsync(new DirectoryInfo(Environment.CurrentDirectory), explicitOutDir, explicitTargets);

        var fullPath = Path.GetFullPath(inputPath);
        if (Directory.Exists(fullPath))
            return await ResolveProjectAsync(new DirectoryInfo(fullPath), explicitOutDir, explicitTargets);

        var inputFile = new FileInfo(fullPath);
        if (!inputFile.Exists)
            throw new CliInputException($"file not found: {inputFile.FullName}");

        var isolatedOutDir = explicitOutDir ?? GetIsolatedFileModeOutDirectory(inputFile);

        return new ResolvedCliInput(
            inputFile,
            isolatedOutDir,
            explicitTargets is { Length: > 0 } ? explicitTargets : null,
            Project: null,
            ModuleCatalog: null,
            ProviderPackageResolution.Empty);
    }

    private static DirectoryInfo GetIsolatedFileModeOutDirectory(FileInfo inputFile)
    {
        var inputDirectory = inputFile.Directory?.FullName ?? Environment.CurrentDirectory;
        var exampleName = Path.GetFileNameWithoutExtension(inputFile.Name);
        return new DirectoryInfo(Path.Combine(inputDirectory, ".junexis", "out", exampleName));
    }

    private static async Task<ResolvedCliInput> ResolveProjectAsync(
        DirectoryInfo startDirectory,
        DirectoryInfo? explicitOutDir,
        string[]? explicitTargets)
    {
        JunexisProject? project;
        try
        {
            project = await JunexisProjectManifest.DiscoverAsync(startDirectory);
        }
        catch (InvalidDataException ex)
        {
            throw new CliInputException(ex.Message);
        }

        if (project is null)
            throw new CliInputException(
                $"project manifest '{JunexisProjectManifest.FileName}' not found from directory: {startDirectory.FullName}");

        var entry = project.EntryFile;
        if (!entry.Exists)
            throw new CliInputException($"project entry file not found: {entry.FullName}");

        var targets = explicitTargets is { Length: > 0 }
            ? explicitTargets
            : project.Manifest.Targets;

        ProjectModuleCatalog catalog;
        try
        {
            catalog = await project.DiscoverModulesAsync(targets);
        }
        catch (InvalidDataException ex)
        {
            throw new CliInputException(ex.Message);
        }

        if (!catalog.Modules.Any(module =>
                module.SourceFile.FullName.Equals(entry.FullName, StringComparison.OrdinalIgnoreCase)))
            throw new CliInputException($"project entry file is outside configured source roots: {entry.FullName}");

        ProviderPackageResolution providerPackages;
        try
        {
            providerPackages = await ProviderPackageResolver.ResolveAsync(project);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
        {
            throw new CliInputException(ex.Message);
        }

        return new ResolvedCliInput(
            entry,
            explicitOutDir ?? project.OutDirectory,
            targets,
            project,
            catalog,
            providerPackages);
    }
}
