using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class CleanHandler
{
    internal static async Task<int> HandleAsync(string? projectPath, bool dryRun)
    {
        JunexisProject project;
        try
        {
            var startDirectory = string.IsNullOrWhiteSpace(projectPath)
                ? new DirectoryInfo(Environment.CurrentDirectory)
                : new DirectoryInfo(Path.GetFullPath(projectPath));

            if (!startDirectory.Exists)
            {
                await Console.Error.WriteLineAsync($"error: project directory not found: {startDirectory.FullName}");
                return 1;
            }

            project = await JunexisProjectManifest.DiscoverAsync(startDirectory)
                ?? throw new CliInputException(
                    $"project manifest '{JunexisProjectManifest.FileName}' not found from directory: {startDirectory.FullName}");
        }
        catch (CliInputException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        var targets = GetCleanTargets(project).ToList();
        foreach (var target in targets)
        {
            if (!IsSafeProjectCleanTarget(project.RootDirectory, target))
            {
                await Console.Error.WriteLineAsync($"error: refusing to clean path outside project workspace: {target.FullName}");
                return 1;
            }
        }

        foreach (var target in targets)
        {
            if (target.Exists)
            {
                if (dryRun)
                    Console.WriteLine($"would remove {target.FullName}");
                else
                {
                    target.Delete(recursive: true);
                    Console.WriteLine($"removed {target.FullName}");
                }
            }
            else if (dryRun)
            {
                Console.WriteLine($"not present {target.FullName}");
            }
        }

        return 0;
    }

    private static IEnumerable<DirectoryInfo> GetCleanTargets(JunexisProject project)
    {
        yield return project.OutDirectory;
        yield return new DirectoryInfo(Path.Combine(project.RootDirectory.FullName, ".junexis", "build"));
        yield return new DirectoryInfo(Path.Combine(project.RootDirectory.FullName, ".junexis", "diagnostics"));
        yield return new DirectoryInfo(Path.Combine(project.RootDirectory.FullName, ".junexis", "state"));
    }

    private static bool IsSafeProjectCleanTarget(DirectoryInfo projectRoot, DirectoryInfo target)
    {
        var root = Path.GetFullPath(projectRoot.FullName)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(target.FullName)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
               !path.Equals(root, StringComparison.OrdinalIgnoreCase);
    }
}
