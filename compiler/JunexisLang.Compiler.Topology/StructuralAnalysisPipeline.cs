using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Topology;

/// <summary>
/// Wynik wspólnego etapu Structural IR i analiz topologii uruchamianego po
/// poprawnym parsowaniu oraz walidacji semantycznej.
/// </summary>
/// <param name="Success">Czy etap zakończył się bez diagnostyk poziomu error.</param>
/// <param name="Document">Dokument IR albo <see langword="null"/>, gdy lowering IR się nie powiódł.</param>
/// <param name="DistributedTopology">Plan rozproszony albo <see langword="null"/>, gdy nie mógł powstać.</param>
/// <param name="Diagnostics">Diagnostyki IR, topologii logicznej i loweringu rozproszonego.</param>
public sealed record StructuralAnalysisResult(
    bool Success,
    IRDocument? Document,
    DistributedTopologyPlan? DistributedTopology,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Wspólny pipeline analiz strukturalnych używany przez wszystkie interfejsy
/// kompilatora, w szczególności CLI oraz LSP.
/// </summary>
public static class StructuralAnalysisPipeline
{
    /// <summary>
    /// Buduje IR, analizuje logiczną topologię i wykonuje lowering topologii
    /// rozproszonej dla zweryfikowanego programu.
    /// </summary>
    /// <param name="program">Program po analizie składniowej i semantycznej.</param>
    /// <returns>Wynik pełnej analizy strukturalnej.</returns>
    public static StructuralAnalysisResult Analyze(ProgramNode program)
    {
        var irResult = new AstToIrTransformer().TransformWithDiagnostics(program);
        if (!irResult.Success || irResult.Document is null)
            return new StructuralAnalysisResult(false, null, null, irResult.Diagnostics);

        var topology = TopologyAnalyzer.Analyze(irResult.Document);
        var distributed = DistributedTopologyLowerer.Lower(irResult.Document);
        var storage = StorageSourceProjector.Project(irResult.Document);
        var diagnostics = irResult.Diagnostics
            .Concat(topology.Diagnostics)
            .Concat(distributed.Diagnostics)
            .Concat(storage.Diagnostics)
            .ToList();
        var success = !diagnostics.Any(diagnostic => diagnostic.Level == DiagnosticLevel.Error);

        return new StructuralAnalysisResult(success, irResult.Document, distributed.Plan, diagnostics);
    }
}
