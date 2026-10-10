namespace JunexisLang.Compiler.Tests.Topology;

using System.Collections.Generic;
using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Tests.TestHelpers;

/// <summary>Wspolne helpery dla testow topologii.</summary>
public sealed partial class TopologyTests
{
    // ─── Helpery ─────────────────────────────────────────────────────────────

    /// <summary>Buduje minimalny IRDocument z listą tasków i jednym pipeline.</summary>
    private static IRDocument MakeDoc(
        IReadOnlyList<string> taskNames,
        IReadOnlyList<(string From, string To)> conns,
        string pipelineName = "P")
        => IrDocumentFactory.CreatePipelineDocument(taskNames, conns, pipelineName);
}
