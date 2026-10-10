using System.Diagnostics;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>Runs the M4 artifact inventory and native toolchain acceptance gates.</summary>
internal static class ArtifactGateHandler
{
    internal static async Task<int> HandleAsync(
        string? inputPath,
        string[]? targets,
        string goExe,
        string npmExe,
        string platformIoExe)
    {
        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(inputPath, explicitTargets: targets);
        }
        catch (CliInputException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        var input = resolved.SourceFile;
        var check = await CliProjectChecker.CheckAsync(resolved);
        if (!check.Success || check.Ast is null)
        {
            DiagnosticFormatter.Write(check.Diagnostics);
            return 1;
        }

        var structural = StructuralAnalysisStage.Analyze(check.Ast);
        if (!structural.Success || structural.Document is null)
        {
            DiagnosticFormatter.Write(structural.Diagnostics);
            return 1;
        }

        var document = structural.Document;
        var selected = resolved.Targets is { Length: > 0 }
            ? resolved.Targets.Select(target => target.ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : CodeGeneratorFactory.GetApplicableExecutionPlatforms(document).ToList();
        var unsupported = selected.Where(target => target is not ("embedded" or "server" or "report")).ToList();
        if (unsupported.Count > 0)
        {
            await Console.Error.WriteLineAsync($"error G0002: unknown artifact-gate target(s): {string.Join(", ", unsupported)}");
            return 1;
        }

        var root = Path.Combine(Path.GetTempPath(), $"junexis_m4_gate_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            foreach (var target in selected)
            {
                var generator = CodeGeneratorFactory.Get([target]).Single();
                var files = generator.Generate(document, new CodeGenOptions(root));
                if (files.Count == 0)
                {
                    await Console.Error.WriteLineAsync(
                        $"error G0001 [{target}]: no applicable tasks, so no artifact was generated");
                    return 1;
                }
                var contract = GeneratedArtifactContract.Validate(document, target, files);
                if (!contract.Success)
                {
                    foreach (var violation in contract.Violations)
                        await Console.Error.WriteLineAsync($"error G0001 [{target}]: {violation.Message}");
                    return 1;
                }

                foreach (var file in files)
                {
                    var path = Path.Combine(root, file.FilePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, file.Content);
                }

                ApplyProjectUserOverrides(resolved, root, target);
                if (target == "report")
                {
                    if (!ApplyReportPackageCustomization(root, out var reportPackageError))
                    {
                        await Console.Error.WriteLineAsync(
                            $"error B0004: invalid user/report/package.json: {reportPackageError}");
                        return 1;
                    }
                    var reportTasks = document.Tasks
                        .Where(task => task.Platforms.Any(platform => platform.Target == "report"))
                        .Select(task => task.Name)
                        .ToHashSet(StringComparer.Ordinal);
                    if (!ApplyReportViewCustomization(root, reportTasks, out var reportViewError))
                    {
                        await Console.Error.WriteLineAsync(
                            $"error B0005: invalid user/report/view.json: {reportViewError}");
                        return 1;
                    }
                    ApplyReportRendererManifest(resolved, root);
                }
                if (target == "server")
                    IncludeProjectUserServerGoFiles(resolved, root);
                if (target == "embedded")
                    IncludeProjectUserEmbeddedFiles(resolved, root);

                Console.Write($"  [{target}] artifact contract OK; native toolchain ... ");
                var exitCode = target switch
                {
                    "server" => await ValidateGoAsync(root, goExe),
                    "report" => await ValidateTypeScriptAsync(root, npmExe),
                    "embedded" => await ValidateEmbeddedAsync(root, platformIoExe),
                    _ => 1
                };
                if (exitCode != 0)
                {
                    Console.WriteLine("FAIL");
                    return exitCode;
                }
                Console.WriteLine("OK");
            }

            return 0;
        }
        finally
        {
            TryCleanup(root);
        }
    }

    private static async Task<int> ValidateGoAsync(string root, string executable)
    {
        var directory = Path.Combine(root, "server");
        var tidy = await RunAsync(executable, ["mod", "tidy"], directory, configureGo: true);
        return tidy == 0 ? await RunAsync(executable, ["build", "."], directory, configureGo: true) : tidy;
    }

    private static async Task<int> ValidateTypeScriptAsync(string root, string executable)
    {
        var directory = Path.Combine(root, "report");
        var install = await RunAsync(executable, ["install", "--ignore-scripts"], directory, configureNpm: true);
        return install == 0 ? await RunAsync(executable, ["run", "build"], directory, configureNpm: true) : install;
    }

    private static Task<int> ValidateEmbeddedAsync(string root, string executable) =>
        RunAsync(executable, ["run"], Path.Combine(root, "embedded"));

    private static void ApplyProjectUserOverrides(ResolvedCliInput resolved, string gateRoot, string target)
    {
        if (resolved.Project is null)
            return;

        foreach (var overlay in ProjectOverlays.Where(overlay => overlay.Platform == target))
        {
            var userFile = Path.Combine(resolved.Project.RootDirectory.FullName, overlay.UserPath);
            if (!File.Exists(userFile))
                continue;

            var destination = Path.Combine(gateRoot, overlay.GeneratedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(userFile, destination, overwrite: true);
        }
    }

    private static readonly ProjectOverlay[] ProjectOverlays =
    [
        new("embedded", "embedded/platformio.ini", Path.Combine("user", "embedded", "platformio.ini")),
        new("embedded", "embedded/CMakeLists.txt", Path.Combine("user", "embedded", "CMakeLists.txt")),
        new("server", "server/go.mod", Path.Combine("user", "server", "go.mod")),
        new("report", "report/user-adapter.ts", Path.Combine("user", "report", "adapter.ts")),
        new("report", "report/user-package.json", Path.Combine("user", "report", "package.json")),
        new("report", "report/user-view.json", Path.Combine("user", "report", "view.json"))
    ];

    private sealed record ProjectOverlay(string Platform, string GeneratedPath, string UserPath);

    private static bool ApplyReportPackageCustomization(string gateRoot, out string error)
    {
        error = string.Empty;
        var userPath = Path.Combine(gateRoot, "report", "user-package.json");
        if (!File.Exists(userPath))
            return true;

        var generatedPath = Path.Combine(gateRoot, "report", "package.json");
        if (!ReportPackageCustomization.TryMerge(
                File.ReadAllText(generatedPath),
                File.ReadAllText(userPath),
                out var merged,
                out error))
            return false;

        File.WriteAllText(generatedPath, merged);
        return true;
    }

    private static bool ApplyReportViewCustomization(
        string gateRoot,
        IReadOnlySet<string> reportTasks,
        out string error)
    {
        error = string.Empty;
        var source = Path.Combine(gateRoot, "report", "user-view.json");
        if (!File.Exists(source))
            return true;
        if (!ReportViewCustomization.TryCompile(
                File.ReadAllText(source),
                reportTasks,
                out var typeScript,
                out error))
            return false;
        File.WriteAllText(Path.Combine(gateRoot, "report", "user-view.ts"), typeScript);
        return true;
    }

    private static void ApplyReportRendererManifest(ResolvedCliInput resolved, string gateRoot)
    {
        if (resolved.Project is null)
            return;
        var reportUserRoot = Path.Combine(resolved.Project.RootDirectory.FullName, "user", "report");
        var renderer = File.Exists(Path.Combine(reportUserRoot, "adapter.ts"))
            ? "user"
            : File.Exists(Path.Combine(reportUserRoot, "view.json")) ? "declarative" : null;
        if (renderer is null)
            return;
        var path = Path.Combine(gateRoot, "report", "junexis-report-manifest.json");
        if (!File.Exists(path))
            return;
        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace(
                "\"renderer\": \"automatic\"",
                $"\"renderer\": \"{renderer}\"",
                StringComparison.Ordinal));
    }

    private static void IncludeProjectUserServerGoFiles(ResolvedCliInput resolved, string gateRoot)
    {
        if (resolved.Project is null)
            return;

        var userServerDirectory = Path.Combine(resolved.Project.RootDirectory.FullName, "user", "server");
        if (!Directory.Exists(userServerDirectory))
            return;

        foreach (var sourcePath in Directory.EnumerateFiles(userServerDirectory, "*.go", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(sourcePath).Equals("go.mod", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(userServerDirectory, sourcePath);
            var destination = Path.Combine(gateRoot, "server", "user", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourcePath, destination, overwrite: true);
        }
    }

    private static void IncludeProjectUserEmbeddedFiles(ResolvedCliInput resolved, string gateRoot)
    {
        if (resolved.Project is null)
            return;

        var userEmbeddedDirectory = Path.Combine(resolved.Project.RootDirectory.FullName, "user", "embedded");
        if (!Directory.Exists(userEmbeddedDirectory))
            return;

        foreach (var sourcePath in Directory.EnumerateFiles(userEmbeddedDirectory, "*", SearchOption.AllDirectories)
                     .Where(IsEmbeddedUserSource))
        {
            var relative = Path.GetRelativePath(userEmbeddedDirectory, sourcePath);
            var destination = Path.Combine(gateRoot, "embedded", "user", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourcePath, destination, overwrite: true);
        }
    }

    private static bool IsEmbeddedUserSource(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".c", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".h", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        bool configureGo = false,
        bool configureNpm = false)
    {
        var resolved = ResolveExecutable(executable);
        if (resolved is null)
        {
            await Console.Error.WriteLineAsync($"toolchain executable not found: {executable}");
            return 1;
        }

        var effectiveArguments = arguments.ToList();
        if (OperatingSystem.IsWindows() &&
            Path.GetFileName(resolved).Equals("npm.cmd", StringComparison.OrdinalIgnoreCase))
        {
            var nodeDirectory = Path.GetDirectoryName(resolved)!;
            var nodeExecutable = Path.Combine(nodeDirectory, "node.exe");
            var npmCli = Path.Combine(nodeDirectory, "node_modules", "npm", "bin", "npm-cli.js");
            if (File.Exists(nodeExecutable) && File.Exists(npmCli))
            {
                resolved = nodeExecutable;
                effectiveArguments.Insert(0, npmCli);
            }
        }

        var requiresCommandInterpreter = OperatingSystem.IsWindows() &&
            Path.GetExtension(resolved) is ".cmd" or ".bat";
        var processExecutable = requiresCommandInterpreter
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : resolved;
        var info = new ProcessStartInfo(processExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (configureGo)
        {
            var cacheRoot = Path.Combine(workingDirectory, ".junexis-cache");
            var goPath = Path.Combine(cacheRoot, "gopath");
            info.Environment["GOCACHE"] = Path.Combine(cacheRoot, "go-build");
            info.Environment["GOPATH"] = goPath;
            info.Environment["GOMODCACHE"] = Path.Combine(goPath, "pkg", "mod");
            info.Environment["GOTELEMETRY"] = "off";
        }
        if (configureNpm)
            info.Environment["npm_config_cache"] = Path.Combine(
                workingDirectory, ".junexis-cache", "npm");
        if (requiresCommandInterpreter)
        {
            var command = $"\"{resolved}\" {string.Join(" ", effectiveArguments.Select(QuoteCommandArgument))}";
            info.Arguments = $"/d /c \"{command}\"";
        }
        else
        {
            foreach (var argument in effectiveArguments)
                info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await stdout + await stderr).Trim();
        if (process.ExitCode != 0 && output.Length > 0)
            await Console.Error.WriteLineAsync(output);
        return process.ExitCode;
    }

    private static string QuoteCommandArgument(string argument) =>
        argument.Any(character => char.IsWhiteSpace(character) || character is '&' or '|' or '<' or '>')
            ? $"\"{argument.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : argument;

    private static string? ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar))
            return File.Exists(executable) ? executable : null;

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(directory, executable.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? executable
                : executable + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void TryCleanup(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch { /* Toolchains can briefly retain handles on Windows. */ }
    }
}
