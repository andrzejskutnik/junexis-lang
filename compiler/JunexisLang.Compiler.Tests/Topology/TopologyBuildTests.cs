namespace JunexisLang.Compiler.Tests.Topology;

using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Tests.TestHelpers;
using JunexisLang.Compiler.Topology;

/// <summary>Testy budowania grafu pipeline przez <see cref="PipelineGraph"/>.</summary>
public sealed partial class TopologyTests
{
    [Fact]
    public void Build_EmptyDocument_EmptyGraph()
    {
        var doc = MakeDoc([], []);
        var g = PipelineGraph.Build(doc);

        Assert.Empty(g.Nodes);
        Assert.Empty(g.Edges);
    }

    [Fact]
    public void Build_SingleTask_NoEdges()
    {
        var doc = MakeDoc(["A"], []);
        var g = PipelineGraph.Build(doc);

        Assert.Contains("A", g.Nodes);
        Assert.Empty(g.Edges);
    }

    [Fact]
    public void Build_LinearChain_CorrectEdges()
    {
        var doc = MakeDoc(["A", "B", "C"], [("A", "B"), ("B", "C")]);
        var g = PipelineGraph.Build(doc);

        Assert.Equal(3, g.Nodes.Count);
        Assert.Equal(2, g.Edges.Count);
        Assert.Contains(g.Edges, e => e.From == "A" && e.To == "B");
        Assert.Contains(g.Edges, e => e.From == "B" && e.To == "C");
    }

    [Fact]
    public void Build_TaskNotInTaskList_EmitsT0002WithoutInventingNode()
    {
        var doc = MakeDoc(["A"], [("A", "X")]);
        var g = PipelineGraph.Build(doc);

        Assert.DoesNotContain("X", g.Nodes);
        Assert.Empty(g.Edges);
        Assert.Contains(g.Diagnostics, diagnostic => diagnostic.Code == "T0002");
    }

    [Fact]
    public void Build_Successors_ReturnsCorrectTargets()
    {
        var doc = MakeDoc(["A", "B", "C"], [("A", "B"), ("A", "C")]);
        var g = PipelineGraph.Build(doc);

        var succ = g.Successors("A");
        Assert.Equal(2, succ.Count);
        Assert.Contains("B", succ);
        Assert.Contains("C", succ);
    }

    [Fact]
    public void Build_PortAwareEdge_PreservesEndpoints()
    {
        var taskA = new IRTask("A", [], [], null, [], null, [], []);
        var taskB = new IRTask("B", [], [], null, [], null, [], []);
        var pipeline = new IRPipeline("P", [new IRUseConn("A", "out", "B", "input")]);
        var doc = new IRDocument("test.jnx", [], [], [], [], [taskA, taskB], [pipeline],
            [], [], [], [], [], [], [], [], [], [], [], [], []);

        var edge = Assert.Single(PipelineGraph.Build(doc).Edges);

        Assert.Equal("out", edge.FromPort);
        Assert.Equal("input", edge.ToPort);
    }
}
