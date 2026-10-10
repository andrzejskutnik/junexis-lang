using System.Diagnostics;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>Publishes a compiler analysis snapshot before target code generation.</summary>
internal static class AnalyzeHandler
{
    internal static async Task<int> HandleAsync(
        string? inputPath,
        DirectoryInfo? outDir,
        string[]? targets,
        bool json)
    {
        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(inputPath, outDir, targets);
        }
        catch (CliInputException ex)
        {
            if (json)
            {
                var diagnostic = new Diagnostic("", DiagnosticLevel.Error, "CLI", ex.Message);
                DiagnosticFormatter.WriteJson("analyze", false, 0,
                    inputPath ?? Environment.CurrentDirectory, [diagnostic]);
            }
            else
            {
                await Console.Error.WriteLineAsync($"error: {ex.Message}");
            }
            return 1;
        }

        var input = resolved.SourceFile;
        var stopwatch = Stopwatch.StartNew();
        var check = await CliProjectChecker.CheckAsync(resolved);
        var diagnostics = new List<Diagnostic>(check.Diagnostics);
        if (!check.Success || check.Ast is null)
            return Finish(false, null);

        var structural = StructuralAnalysisStage.Analyze(check.Ast);
        diagnostics.AddRange(structural.Diagnostics);
        if (!structural.Success || structural.Document is null)
            return Finish(false, null);

        var requestedTargets = resolved.Targets is { Length: > 0 }
            ? resolved.Targets.Select(value => value.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        var applicableTargets = CodeGeneratorFactory.GetApplicableExecutionPlatforms(structural.Document);
        var selectedTargets = requestedTargets.Length > 0 ? requestedTargets : applicableTargets;
        var targetSelectionSource = targets is { Length: > 0 }
            ? "explicit"
            : resolved.Project is not null ? "manifest" : "inferred";
        foreach (var target in requestedTargets.Where(
                     target => !CodeGeneratorFactory.ExecutionPlatforms.Contains(target)))
        {
            diagnostics.Add(new Diagnostic(
                "G0002",
                DiagnosticLevel.Error,
                "CodeGen",
                $"unknown code generation target: '{target}'",
                input.FullName));
        }
        foreach (var target in selectedTargets.Where(
                     CodeGeneratorFactory.ExecutionPlatforms.Contains).Where(
                     target => !applicableTargets.Contains(target, StringComparer.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new Diagnostic(
                "B0003",
                DiagnosticLevel.Error,
                "Build",
                $"target '{target}' has no applicable task in the analyzed program",
                input.FullName));
        }
        if (selectedTargets.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "B0003",
                DiagnosticLevel.Error,
                "Build",
                "program has no executable task target; add @embedded, @server, or @report",
                input.FullName));
        }
        if (diagnostics.Any(item => item.Level == DiagnosticLevel.Error))
            return Finish(false, null);

        var outputRoot = resolved.OutDirectory?.FullName
            ?? Path.Combine(input.DirectoryName!, "out");
        var buildId = AnalysisSnapshotWriter.ComputePreflightBuildId(
            structural.Document,
            selectedTargets);
        var snapshotPath = Path.Combine(
            outputRoot, "analysis", "junexis.analysis.preflight.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(
                snapshotPath,
                AnalysisSnapshotWriter.CreateJson(structural.Document, buildId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new Diagnostic(
                "CLI0001",
                DiagnosticLevel.Error,
                "CLI",
                $"cannot write analysis preflight snapshot: {ex.Message}",
                input.FullName));
            return Finish(false, null);
        }
        return Finish(true, (buildId, snapshotPath, outputRoot, selectedTargets,
            requestedTargets, applicableTargets, targetSelectionSource));

        int Finish(
            bool success,
            (string BuildId, string SnapshotPath, string OutputRoot, IReadOnlyList<string> Targets,
                IReadOnlyList<string> RequestedTargets, IReadOnlyList<string> ApplicableTargets,
                string TargetSelectionSource)? result)
        {
            stopwatch.Stop();
            if (json)
            {
                var mode = resolved.Project is null ? "file" : "project";
                Console.WriteLine(BuildOutputContractWriter.BuildJson(
                    success,
                    stopwatch.ElapsedMilliseconds,
                    input.FullName,
                    diagnostics,
                    mode,
                    resolved.Project?.RootDirectory.FullName ?? input.DirectoryName!,
                    input.FullName,
                    result?.OutputRoot,
                    result?.Targets ?? [],
                    null,
                    result?.RequestedTargets ?? [],
                    result?.ApplicableTargets ?? [],
                    [],
                    result?.TargetSelectionSource ?? "inferred",
                    command: "analyze",
                    buildId: result?.BuildId,
                    analysisSnapshot: result?.SnapshotPath));
            }
            else
            {
                DiagnosticFormatter.Write(diagnostics);
                if (result is not null)
                    Console.WriteLine($"  wrote {result.Value.SnapshotPath}");
            }
            return success ? 0 : 1;
        }
    }
}
