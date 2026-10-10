using System.Diagnostics;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>
/// Handler for the `build` command (alias: `compile`) in the JunexisLang CLI.
/// It orchestrates the compilation process: parsing, semantic analysis, IR generation, code generation,
/// and writing the generated files to disk.
/// </summary>
internal static class CompileHandler
{
    /// <summary>
    /// Handles the `build` command by performing the full compilation process on a given .jnx source file.
    /// It checks for file existence, performs parsing and semantic analysis, generates intermediate representation (IR),
    /// and produces the final code artifacts.
    /// </summary>
    /// <param name="inputPath">The input .jnx source file, project directory, or discovered current project.</param>
    /// <param name="outDir">The output directory for generated files.</param>
    /// <param name="targets">The target platforms for code generation.</param>
    /// <param name="json">Whether to output diagnostics in JSON format.</param>
    /// <param name="outputSchema">JSON output schema version.</param>
    /// <returns>The exit code of the compilation process.</returns>
    internal static async Task<int> HandleAsync(
        string? inputPath,
        DirectoryInfo? outDir,
        string[]? targets,
        bool json,
        string outputSchema,
        string? expectedBuildId)
    {
        if (outputSchema is not ("1.0" or "2.0"))
        {
            await Console.Error.WriteLineAsync(
                $"error: unsupported output schema '{outputSchema}'; expected 1.0 or 2.0");
            return 2;
        }

        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(inputPath, outDir, targets);
        }
        catch (CliInputException ex)
        {
            if (json)
            {
                var errorDiag = new Diagnostic("", DiagnosticLevel.Error, "CLI", ex.Message);
                DiagnosticFormatter.WriteJson("build", false, 0, inputPath ?? Environment.CurrentDirectory, new[] { errorDiag });
            }
            else
            {
                await Console.Error.WriteLineAsync($"error: {ex.Message}");
            }
            return 1;
        }

        var input = resolved.SourceFile;
        var sw = Stopwatch.StartNew();
        var mode = resolved.Project is null ? "file" : "project";
        var projectRoot = resolved.Project?.RootDirectory.FullName ?? input.DirectoryName!;
        var requestedTargets = resolved.Targets is { Length: > 0 }
            ? resolved.Targets.Select(target => target.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        var targetSelectionSource = targets is { Length: > 0 }
            ? "explicit"
            : resolved.Project is not null ? "manifest" : "inferred";
        IReadOnlyList<string> applicableTargets = [];
        IReadOnlyList<string> selectedTargets = requestedTargets;

        // ── 1. Parse + semantyka ──────────────────────────────────────────
        var checkResult = await CliProjectChecker.CheckAsync(resolved);

        if (!checkResult.Success || checkResult.Ast is null)
        {
            sw.Stop();
            if (json)
                WriteJson(outputSchema, false, sw.ElapsedMilliseconds, input.FullName, checkResult.Diagnostics,
                    mode, projectRoot, input.FullName, null, selectedTargets, null,
                    requestedTargets, applicableTargets, [], targetSelectionSource);
            else
                DiagnosticFormatter.Write(checkResult.Diagnostics);
            return 1;
        }

        // ── 2. AST → IR ───────────────────────────────────────────────────
        var allDiagnostics = new List<Diagnostic>(checkResult.Diagnostics);
        var structural = StructuralAnalysisStage.Analyze(checkResult.Ast);
        allDiagnostics.AddRange(structural.Diagnostics);
        if (!structural.Success || structural.Document is null)
        {
            sw.Stop();
            if (json)
                WriteJson(outputSchema, false, sw.ElapsedMilliseconds, input.FullName, allDiagnostics,
                    mode, projectRoot, input.FullName, null, selectedTargets, null,
                    requestedTargets, applicableTargets, [], targetSelectionSource);
            else
                DiagnosticFormatter.Write(allDiagnostics);
            return 1;
        }
        var ir = structural.Document;

        if (resolved.ProviderPackages.Packages.Count > 0)
        {
            var storageContracts = IRStorageContractProjection.Project(ir)
                .Select(contract => contract.Name)
                .ToHashSet(StringComparer.Ordinal);
            var boundDeclarations = resolved.ProviderPackages.Bindings
                .Select(binding => binding.Binding.Declaration)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var binding in resolved.ProviderPackages.Bindings.Where(
                         binding => !storageContracts.Contains(binding.Binding.Declaration)))
            {
                allDiagnostics.Add(new Diagnostic(
                    "B0006",
                    DiagnosticLevel.Error,
                    "Build",
                    $"storage binding '{binding.Binding.Name}' references unknown declaration " +
                    $"'{binding.Binding.Declaration}'",
                    resolved.Project?.ManifestFile.FullName));
            }
            foreach (var declaration in storageContracts.Where(
                         declaration => !boundDeclarations.Contains(declaration)))
            {
                allDiagnostics.Add(new Diagnostic(
                    "B0007",
                    DiagnosticLevel.Error,
                    "Build",
                    $"storage declaration '{declaration}' has no project binding",
                    resolved.Project?.ManifestFile.FullName));
            }
        }

        applicableTargets = CodeGeneratorFactory.GetApplicableExecutionPlatforms(ir);
        foreach (var target in requestedTargets.Where(
                     target => !CodeGeneratorFactory.ExecutionPlatforms.Contains(target)))
        {
            allDiagnostics.Add(new Diagnostic(
                "G0002",
                DiagnosticLevel.Error,
                "CodeGen",
                $"unknown code generation target: '{target}'",
                input.FullName));
        }
        selectedTargets = requestedTargets.Length > 0 ? requestedTargets : applicableTargets;
        foreach (var target in selectedTargets.Where(
                     CodeGeneratorFactory.ExecutionPlatforms.Contains).Where(
                     target => !applicableTargets.Contains(target, StringComparer.OrdinalIgnoreCase)))
        {
            allDiagnostics.Add(new Diagnostic(
                "B0003",
                DiagnosticLevel.Error,
                "Build",
                $"target '{target}' has no applicable task in the analyzed program",
                input.FullName));
        }
        if (selectedTargets.Count == 0)
        {
            allDiagnostics.Add(new Diagnostic(
                "B0003",
                DiagnosticLevel.Error,
                "Build",
                "program has no executable task target; add @embedded, @server, or @report",
                input.FullName));
        }

        if (allDiagnostics.Any(diagnostic => diagnostic.Level == DiagnosticLevel.Error))
        {
            sw.Stop();
            if (json)
                WriteJson(outputSchema, false, sw.ElapsedMilliseconds, input.FullName, allDiagnostics,
                    mode, projectRoot, input.FullName, null, selectedTargets, null,
                    requestedTargets, applicableTargets, [], targetSelectionSource);
            else
                DiagnosticFormatter.Write(allDiagnostics);
            return 1;
        }

        var actualBuildId = AnalysisSnapshotWriter.ComputePreflightBuildId(ir, selectedTargets);
        if (expectedBuildId is not null)
        {
            if (!string.Equals(expectedBuildId, actualBuildId, StringComparison.Ordinal))
            {
                allDiagnostics.Add(new Diagnostic(
                    "B0001",
                    DiagnosticLevel.Error,
                    "Build",
                    "source or targets changed after analysis preflight; run the build again",
                    input.FullName));
                sw.Stop();
                if (json)
                    WriteJson(outputSchema, false, sw.ElapsedMilliseconds, input.FullName, allDiagnostics,
                        mode, projectRoot, input.FullName, null, selectedTargets, null,
                        requestedTargets, applicableTargets, [], targetSelectionSource);
                else
                    DiagnosticFormatter.Write(allDiagnostics);
                return 1;
            }
        }

        // ── 3. IR → kod ───────────────────────────────────────────────────
        var generators = CodeGeneratorFactory.Get(selectedTargets);
        var outDirPath = resolved.OutDirectory?.FullName ?? Path.Combine(input.DirectoryName!, "out");
        var codeOpts   = new CodeGenOptions(outDirPath);

        var allFiles = new List<GeneratedFile>();
        foreach (var generator in generators)
        {
            var generated = generator.Generate(ir, codeOpts);
            var contract = GeneratedArtifactContract.Validate(ir, generator.Platform, generated);
            foreach (var violation in contract.Violations)
                allDiagnostics.Add(new Diagnostic(
                    "G0001",
                    DiagnosticLevel.Error,
                    "CodeGen",
                    $"generated artifact contract failed [{violation.Platform}]: {violation.Message}",
                    input.FullName));
            allFiles.AddRange(generated);
        }

        var builtTargets = allFiles
            .Select(file => file.Platform)
            .Where(CodeGeneratorFactory.ExecutionPlatforms.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();

        foreach (var target in selectedTargets
                     .Where(CodeGeneratorFactory.ExecutionPlatforms.Contains)
                     .Where(target => !builtTargets.Contains(target, StringComparer.OrdinalIgnoreCase)))
        {
            allDiagnostics.Add(new Diagnostic(
                "G0001",
                DiagnosticLevel.Error,
                "CodeGen",
                $"generated artifact contract failed [{target}]: generator produced no execution artifacts",
                input.FullName));
        }

        if (allDiagnostics.Any(diagnostic => diagnostic.Level == DiagnosticLevel.Error))
        {
            sw.Stop();
            if (json)
                WriteJson(outputSchema, false, sw.ElapsedMilliseconds, input.FullName, allDiagnostics,
                    mode, projectRoot, input.FullName, outDirPath, selectedTargets, null,
                    requestedTargets, applicableTargets, builtTargets, targetSelectionSource);
            else
                DiagnosticFormatter.Write(allDiagnostics);
            return 1;
        }

        var userOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ApplyProjectUserOverrides(resolved, allFiles, userOwnedPaths, json);
        if (!ApplyReportPackageCustomization(allFiles, userOwnedPaths, out var reportPackageError))
        {
            allDiagnostics.Add(new Diagnostic(
                "B0004",
                DiagnosticLevel.Error,
                "Build",
                $"invalid user/report/package.json: {reportPackageError}",
                input.FullName));
            sw.Stop();
            if (json)
                WriteJson(outputSchema, false, sw.ElapsedMilliseconds, input.FullName, allDiagnostics,
                    mode, projectRoot, input.FullName, outDirPath, selectedTargets, null,
                    requestedTargets, applicableTargets, builtTargets, targetSelectionSource);
            else
                DiagnosticFormatter.Write(allDiagnostics);
            return 1;
        }
        var reportTaskNames = ir.Tasks
            .Where(task => task.Platforms.Any(platform => platform.Target == "report"))
            .Select(task => task.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!ApplyReportViewCustomization(allFiles, userOwnedPaths, reportTaskNames, out var reportViewError))
        {
            allDiagnostics.Add(new Diagnostic(
                "B0005",
                DiagnosticLevel.Error,
                "Build",
                $"invalid user/report/view.json: {reportViewError}",
                input.FullName));
            sw.Stop();
            if (json)
                WriteJson(outputSchema, false, sw.ElapsedMilliseconds, input.FullName, allDiagnostics,
                    mode, projectRoot, input.FullName, outDirPath, selectedTargets, null,
                    requestedTargets, applicableTargets, builtTargets, targetSelectionSource);
            else
                DiagnosticFormatter.Write(allDiagnostics);
            return 1;
        }
        ApplyReportRendererManifest(allFiles, userOwnedPaths);
        IncludeProjectUserServerGoFiles(resolved, allFiles, userOwnedPaths, json);
        IncludeProjectMigrations(resolved, allFiles, userOwnedPaths, json);
        IncludeProjectUserEmbeddedFiles(resolved, allFiles, userOwnedPaths, json);
        if (resolved.ModuleCatalog is not null && resolved.Project is not null)
            allFiles.Add(ModuleLockWriter.Generate(resolved.ModuleCatalog, resolved.Project.RootDirectory));
        if (resolved.Project is not null && resolved.ProviderPackages.Packages.Count > 0)
            allFiles.Add(ProviderLockWriter.Generate(resolved.ProviderPackages, resolved.Project.RootDirectory));
        allFiles.Add(DebugSourceMapGenerator.Generate(ir, allFiles));
        allFiles.Add(DebugManifestGenerator.Generate(ir));

        var outputArtifacts = BuildOutputContractWriter.Create(
            outDirPath,
            projectRoot,
            input.FullName,
            mode,
            requestedTargets,
            applicableTargets,
            builtTargets,
            targetSelectionSource,
            allFiles,
            userOwnedPaths,
            ir,
            actualBuildId);

        // ── 4. Zapis na dysk ──────────────────────────────────────────────
        RemoveRetiredGeneratedStorageProviders(outDirPath);
        foreach (var file in allFiles)
        {
            var fullPath = Path.Combine(outDirPath, file.FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, file.Content);

            if (!json)
                await Console.Out.WriteLineAsync($"  wrote {file.FilePath}");
        }

        await File.WriteAllTextAsync(outputArtifacts.InventoryPath, outputArtifacts.InventoryJson);
        await File.WriteAllTextAsync(outputArtifacts.BundlePath, outputArtifacts.BundleJson);
        Directory.CreateDirectory(Path.GetDirectoryName(outputArtifacts.AnalysisSnapshotPath)!);
        await File.WriteAllTextAsync(outputArtifacts.AnalysisSnapshotPath, outputArtifacts.AnalysisSnapshotJson);
        if (expectedBuildId is not null)
        {
            var preflightPath = Path.Combine(
                outDirPath, "analysis", "junexis.analysis.preflight.json");
            try
            {
                File.Delete(preflightPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                allDiagnostics.Add(new Diagnostic(
                    "CLI0001",
                    DiagnosticLevel.Warning,
                    "CLI",
                    $"could not remove temporary analysis preflight snapshot: {ex.Message}",
                    input.FullName));
            }
        }

        try
        {
            await LatestBuildReceiptContract.WriteAtomicAsync(
                outputArtifacts.LatestBuildReceiptPath,
                outputArtifacts.LatestBuildReceiptJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            allDiagnostics.Add(new Diagnostic(
                "B0002",
                DiagnosticLevel.Error,
                "Build",
                $"could not publish latest-build receipt atomically: {ex.Message}",
                input.FullName));
        }

        sw.Stop();
        var success = !allDiagnostics.Any(d => d.Level == DiagnosticLevel.Error);

        if (json)
        {
            WriteJson(outputSchema, success, sw.ElapsedMilliseconds, input.FullName, allDiagnostics,
                mode, projectRoot, input.FullName, outDirPath, builtTargets, outputArtifacts,
                requestedTargets, applicableTargets, builtTargets, targetSelectionSource);
        }
        else if (!success)
        {
            DiagnosticFormatter.Write(allDiagnostics);
        }

        return success ? 0 : 1;
    }

    private static void RemoveRetiredGeneratedStorageProviders(string outDirPath)
    {
        // M12.7 moved provider implementations to independently packaged Runtime components.
        // These exact compiler-owned files may remain after rebuilding an M11 output directory.
        string[] retiredFiles =
        [
            "server/junexis/storage/memory/provider.go",
            "server/junexis/storage/sqlite/provider.go",
            "server/junexis/storage/postgresql/provider.go"
        ];
        foreach (var relativePath in retiredFiles)
        {
            var fullPath = Path.Combine(outDirPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
    }

    private static void WriteJson(
        string outputSchema,
        bool success,
        long durationMs,
        string inputFile,
        IReadOnlyList<Diagnostic> diagnostics,
        string mode,
        string projectRoot,
        string entryFile,
        string? outputRoot,
        IReadOnlyList<string> targets,
        BuildOutputArtifacts? artifacts,
        IReadOnlyList<string> requestedTargets,
        IReadOnlyList<string> applicableTargets,
        IReadOnlyList<string> builtTargets,
        string targetSelectionSource)
    {
        if (outputSchema == CliBuildOutputEnvelope.CurrentVersion)
        {
            Console.WriteLine(BuildOutputContractWriter.BuildJson(
                success,
                durationMs,
                inputFile,
                diagnostics,
                mode,
                projectRoot,
                entryFile,
                outputRoot,
                targets,
                artifacts,
                requestedTargets: requestedTargets,
                applicableTargets: applicableTargets,
                builtTargets: builtTargets,
                targetSelectionSource: targetSelectionSource));
            return;
        }

        DiagnosticFormatter.WriteJson("build", success, durationMs, inputFile, diagnostics);
    }

    private static void ApplyProjectUserOverrides(
        ResolvedCliInput resolved,
        List<GeneratedFile> files,
        ISet<string> userOwnedPaths,
        bool json)
    {
        if (resolved.Project is null)
            return;

        foreach (var overlay in ProjectOverlays)
            ApplyUserOverride(resolved.Project, files, userOwnedPaths, overlay, json);
    }

    private static void ApplyUserOverride(
        JunexisProject project,
        List<GeneratedFile> files,
        ISet<string> userOwnedPaths,
        ProjectOverlay overlay,
        bool json)
    {
        var userFile = Path.Combine(project.RootDirectory.FullName, overlay.UserPath);
        if (!File.Exists(userFile))
            return;

        var content = File.ReadAllText(userFile);
        var index = files.FindIndex(file => file.FilePath.Equals(overlay.GeneratedPath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            files[index] = files[index] with { Content = content };
        }
        else
        {
            files.Add(new GeneratedFile(overlay.Platform, overlay.GeneratedPath, content));
        }

        userOwnedPaths.Add(overlay.GeneratedPath.Replace('\\', '/'));

        if (!json)
            Console.Out.WriteLine($"  overlay {overlay.GeneratedPath} <- {overlay.UserPath}");
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

    private static bool ApplyReportPackageCustomization(
        List<GeneratedFile> files,
        IReadOnlySet<string> userOwnedPaths,
        out string error)
    {
        error = string.Empty;
        if (!userOwnedPaths.Contains("report/user-package.json"))
            return true;

        var generatedIndex = files.FindIndex(file => file.FilePath.Equals(
            "report/package.json",
            StringComparison.OrdinalIgnoreCase));
        var userIndex = files.FindIndex(file => file.FilePath.Equals(
            "report/user-package.json",
            StringComparison.OrdinalIgnoreCase));
        if (generatedIndex < 0 || userIndex < 0)
        {
            error = "report package artifacts are missing";
            return false;
        }

        if (!ReportPackageCustomization.TryMerge(
                files[generatedIndex].Content,
                files[userIndex].Content,
                out var merged,
                out error))
            return false;

        files[generatedIndex] = files[generatedIndex] with { Content = merged };
        return true;
    }

    private static bool ApplyReportViewCustomization(
        List<GeneratedFile> files,
        IReadOnlySet<string> userOwnedPaths,
        IReadOnlySet<string> reportTasks,
        out string error)
    {
        error = string.Empty;
        if (!userOwnedPaths.Contains("report/user-view.json"))
            return true;
        var source = files.Find(file => file.FilePath.Equals(
            "report/user-view.json",
            StringComparison.OrdinalIgnoreCase));
        var targetIndex = files.FindIndex(file => file.FilePath.Equals(
            "report/user-view.ts",
            StringComparison.OrdinalIgnoreCase));
        if (source is null || targetIndex < 0)
        {
            error = "report view artifacts are missing";
            return false;
        }
        if (!ReportViewCustomization.TryCompile(
                source.Content,
                reportTasks,
                out var typeScript,
                out error))
            return false;
        files[targetIndex] = files[targetIndex] with { Content = typeScript };
        return true;
    }

    private static void ApplyReportRendererManifest(
        List<GeneratedFile> files,
        IReadOnlySet<string> userOwnedPaths)
    {
        var renderer = userOwnedPaths.Contains("report/user-adapter.ts")
            ? "user"
            : userOwnedPaths.Contains("report/user-view.json") ? "declarative" : null;
        if (renderer is null)
            return;
        var index = files.FindIndex(file => file.FilePath.Equals(
            "report/junexis-report-manifest.json",
            StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;
        files[index] = files[index] with
        {
            Content = files[index].Content.Replace(
                "\"renderer\": \"automatic\"",
                $"\"renderer\": \"{renderer}\"",
                StringComparison.Ordinal)
        };
    }

    private static void IncludeProjectUserServerGoFiles(
        ResolvedCliInput resolved,
        List<GeneratedFile> files,
        ISet<string> userOwnedPaths,
        bool json)
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

            var relative = Path.GetRelativePath(userServerDirectory, sourcePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var generatedPath = $"server/user/{relative}";
            var content = File.ReadAllText(sourcePath);
            var index = files.FindIndex(file => file.FilePath.Equals(generatedPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                files[index] = files[index] with { Content = content };
            }
            else
            {
                files.Add(new GeneratedFile("server", generatedPath, content));
            }

            userOwnedPaths.Add(generatedPath);

            if (!json)
                Console.Out.WriteLine($"  include {generatedPath} <- user/server/{relative}");
        }
    }

    private static void IncludeProjectMigrations(
        ResolvedCliInput resolved,
        List<GeneratedFile> files,
        ISet<string> userOwnedPaths,
        bool json)
    {
        if (resolved.Project is null)
            return;
        if (!files.Any(file => file.FilePath.Equals(
                "server/junexis/storage/runtime/client.go",
                StringComparison.OrdinalIgnoreCase)))
            return;

        var migrationsDirectory = Path.Combine(resolved.Project.RootDirectory.FullName, "migrations");
        if (!Directory.Exists(migrationsDirectory))
            return;

        foreach (var sourcePath in Directory.EnumerateFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            var name = Path.GetFileName(sourcePath);
            var generatedPath = $"server/migrations/{name}";
            var content = File.ReadAllText(sourcePath);
            var index = files.FindIndex(file => file.FilePath.Equals(generatedPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                files[index] = files[index] with { Content = content };
            else
                files.Add(new GeneratedFile("server", generatedPath, content));

            userOwnedPaths.Add(generatedPath);
            if (!json)
                Console.Out.WriteLine($"  include {generatedPath} <- migrations/{name}");
        }
    }

    private static void IncludeProjectUserEmbeddedFiles(
        ResolvedCliInput resolved,
        List<GeneratedFile> files,
        ISet<string> userOwnedPaths,
        bool json)
    {
        if (resolved.Project is null)
            return;

        var userEmbeddedDirectory = Path.Combine(resolved.Project.RootDirectory.FullName, "user", "embedded");
        if (!Directory.Exists(userEmbeddedDirectory))
            return;

        foreach (var sourcePath in Directory.EnumerateFiles(userEmbeddedDirectory, "*", SearchOption.AllDirectories)
                     .Where(IsEmbeddedUserSource))
        {
            var relative = Path.GetRelativePath(userEmbeddedDirectory, sourcePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var generatedPath = $"embedded/user/{relative}";
            var content = File.ReadAllText(sourcePath);
            var index = files.FindIndex(file => file.FilePath.Equals(generatedPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                files[index] = files[index] with { Content = content };
            }
            else
            {
                files.Add(new GeneratedFile("embedded", generatedPath, content));
            }

            userOwnedPaths.Add(generatedPath);

            if (!json)
                Console.Out.WriteLine($"  include {generatedPath} <- user/embedded/{relative}");
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
}
