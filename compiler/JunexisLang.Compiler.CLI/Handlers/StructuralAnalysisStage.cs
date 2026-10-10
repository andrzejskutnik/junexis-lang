using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.Topology;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class StructuralAnalysisStage
{
    internal static StructuralAnalysisResult Analyze(ProgramNode program) =>
        StructuralAnalysisPipeline.Analyze(program);
}
