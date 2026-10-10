using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.CLI.Project;

internal static class CliProjectChecker
{
    internal static async Task<CompilerResult> CheckAsync(ResolvedCliInput input)
    {
        var compiler = new JunexisCompiler();
        if (input.ModuleCatalog is null)
            return await compiler.CheckAsync(new CheckOptions(input.SourceFile.FullName));

        var resolution = await ProjectImportResolver.ResolveAsync(input.ModuleCatalog);
        if (!resolution.Success)
            return CompilerResult.Fail(resolution.Diagnostics);

        var binding = ProjectSymbolBinder.Bind(resolution.Graph);
        if (!binding.Success)
            return CompilerResult.Fail(resolution.Diagnostics.Concat(binding.Diagnostics).ToArray());

        var entry = resolution.Graph.Modules.SingleOrDefault(document =>
            document.Module.SourceFile.FullName.Equals(
                input.SourceFile.FullName,
                StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return CompilerResult.Fail(
            [
                new Diagnostic(
                    "E0004",
                    DiagnosticLevel.Error,
                    "Import",
                    "Project entry module disappeared from the resolved module graph.",
                    input.SourceFile.FullName)
            ]);
        }

        var diagnostics = resolution.Diagnostics.Concat(binding.Diagnostics).ToList();
        foreach (var document in resolution.Graph.Modules)
        {
            var scope = binding.Scopes[document.Module.Identity.Value].Values;
            diagnostics.AddRange(compiler.CheckAst(document.Program, scope).Diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Level == DiagnosticLevel.Error))
            return CompilerResult.Fail(diagnostics);

        var composed = ProjectProgramComposer.Compose(
            resolution.Graph,
            binding,
            input.SourceFile.FullName);
        return CompilerResult.CheckOk(composed, diagnostics);
    }
}
