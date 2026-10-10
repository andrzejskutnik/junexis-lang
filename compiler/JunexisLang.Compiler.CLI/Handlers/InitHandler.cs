using System.Text.RegularExpressions;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static partial class InitHandler
{
    internal static async Task<int> HandleAsync(DirectoryInfo directory, string? name, bool force)
    {
        var resolvedDirectory = new DirectoryInfo(Path.GetFullPath(directory.FullName));
        var projectDirectory = resolvedDirectory.FullName;
        var projectName = NormalizeProjectName(name ?? resolvedDirectory.Name);

        if (string.IsNullOrWhiteSpace(projectName))
        {
            await Console.Error.WriteLineAsync("error: project name cannot be empty");
            return 1;
        }

        if (Directory.Exists(projectDirectory) &&
            Directory.EnumerateFileSystemEntries(projectDirectory).Any() &&
            !force)
        {
            await Console.Error.WriteLineAsync(
                "error: target directory is not empty; use --force to create missing Junexis project files");
            return 1;
        }

        Directory.CreateDirectory(projectDirectory);

        var srcDirectory = Path.Combine(projectDirectory, "src");
        var outDirectory = Path.Combine(projectDirectory, "out");
        var junexisDirectory = Path.Combine(projectDirectory, ".junexis");

        Directory.CreateDirectory(srcDirectory);
        Directory.CreateDirectory(Path.Combine(outDirectory, "server"));
        Directory.CreateDirectory(Path.Combine(outDirectory, "embedded"));
        Directory.CreateDirectory(Path.Combine(outDirectory, "report"));
        Directory.CreateDirectory(Path.Combine(outDirectory, "deployment"));
        Directory.CreateDirectory(Path.Combine(junexisDirectory, "cache"));
        Directory.CreateDirectory(Path.Combine(junexisDirectory, "build"));
        Directory.CreateDirectory(Path.Combine(junexisDirectory, "diagnostics"));

        await WriteFileIfMissingAsync(
            Path.Combine(projectDirectory, JunexisProjectManifest.FileName),
            $$"""
            name = "{{projectName}}"
            entry = "src/main.jnx"
            sourceRoots = ["src"]
            targets = ["server"]
            outDir = "out"

            """);

        await WriteFileIfMissingAsync(
            Path.Combine(srcDirectory, "main.jnx"),
            $$"""
            // Junexis project: {{projectName}}
            // Minimalny punkt wejścia projektu.

            task Main() @server {
                log("Hello from Junexis");
            }

            pipeline MainPipeline {
                use Main;
            }
            """);

        Console.WriteLine($"Created Junexis project layout: {projectDirectory}");
        Console.WriteLine($"  manifest: {Path.Combine(projectDirectory, JunexisProjectManifest.FileName)}");
        Console.WriteLine($"  entry:    {Path.Combine(srcDirectory, "main.jnx")}");

        return 0;
    }

    private static async Task WriteFileIfMissingAsync(string path, string content)
    {
        if (File.Exists(path))
            return;

        await File.WriteAllTextAsync(path, content);
    }

    private static string NormalizeProjectName(string value)
    {
        var normalized = ProjectNameInvalidCharsRegex()
            .Replace(value.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return normalized;
    }

    [GeneratedRegex("[^a-z0-9._-]+")]
    private static partial Regex ProjectNameInvalidCharsRegex();

}
