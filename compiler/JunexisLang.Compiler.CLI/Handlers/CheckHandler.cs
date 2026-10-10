using System.Diagnostics;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class CheckHandler
{
    internal static async Task<int> HandleAsync(string? inputPath, bool json)
    {
        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(inputPath);
        }
        catch (CliInputException ex)
        {
            if (json)
            {
                var errorDiag = new Diagnostic("", DiagnosticLevel.Error, "CLI", ex.Message);
                DiagnosticFormatter.WriteJson("check", false, 0, inputPath ?? Environment.CurrentDirectory, [errorDiag]);
            }
            else
            {
                await Console.Error.WriteLineAsync($"error: {ex.Message}");
            }
            return 1;
        }

        var input = resolved.SourceFile;
        var sw = Stopwatch.StartNew();
        var result = await CliProjectChecker.CheckAsync(resolved);
        var diagnostics = new List<Diagnostic>(result.Diagnostics);
        var success = result.Success;
        if (success && result.Ast is not null)
        {
            var structural = StructuralAnalysisStage.Analyze(result.Ast);
            diagnostics.AddRange(structural.Diagnostics);
            success = structural.Success;
        }
        sw.Stop();

        if (json)
            DiagnosticFormatter.WriteJson("check", success, sw.ElapsedMilliseconds, input.FullName, diagnostics);
        else
            DiagnosticFormatter.Write(diagnostics);

        return success ? 0 : 1;
    }
}
