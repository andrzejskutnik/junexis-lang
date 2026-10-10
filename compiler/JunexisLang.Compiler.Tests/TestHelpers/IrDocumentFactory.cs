namespace JunexisLang.Compiler.Tests.TestHelpers;

using JunexisLang.Compiler.IR;

internal static class IrDocumentFactory
{
    internal static IRDocument CreatePipelineDocument(
        IReadOnlyList<string> taskNames,
        IReadOnlyList<(string From, string To)> conns,
        string pipelineName = "P")
    {
        var tasks = taskNames
            .Select(n => new IRTask(n, [], [], null, [], null, [], []))
            .ToArray();

        var irConns = conns
            .Select(c => (IRPipelineConn)new IRUseConn(c.From, null, c.To, null))
            .ToArray();

        var pipeline = new IRPipeline(pipelineName, irConns);

        return new IRDocument(
            "test.jnx",
            [], [], [], [],
            tasks,
            [pipeline],
            [], [], [], [], [], [], [], [], [], [], [], [], []);
    }
}
