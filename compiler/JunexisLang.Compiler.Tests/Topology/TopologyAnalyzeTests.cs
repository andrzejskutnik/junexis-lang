namespace JunexisLang.Compiler.Tests.Topology;

using JunexisLang.Compiler.Topology;

/// <summary>Testy analizy topologii wykonywane przez <see cref="TopologyAnalyzer"/>.</summary>
public sealed partial class TopologyTests
{
    [Fact]
    public void Analyze_NoCycles_HasCyclesFalse()
    {
        var doc = MakeDoc(["A", "B", "C"], [("A", "B"), ("B", "C")]);
        var result = TopologyAnalyzer.Analyze(doc);

        Assert.False(result.HasCycles);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "T0001");
    }

    [Fact]
    public void Analyze_SimpleCycle_HasCyclesTrue_T0001Emitted()
    {
        var doc = MakeDoc(["A", "B"], [("A", "B"), ("B", "A")]);
        var result = TopologyAnalyzer.Analyze(doc);

        Assert.True(result.HasCycles);
        Assert.Contains(result.Diagnostics, d => d.Code == "T0001");
    }

    [Fact]
    public void Analyze_SelfLoop_HasCyclesTrue()
    {
        var doc = MakeDoc(["A"], [("A", "A")]);
        var result = TopologyAnalyzer.Analyze(doc);

        Assert.True(result.HasCycles);
        Assert.Contains(result.Diagnostics, d => d.Code == "T0001");
    }

    [Fact]
    public void Analyze_LongCycle_DetectedCorrectly()
    {
        var doc = MakeDoc(
            ["A", "B", "C", "D"],
            [("A", "B"), ("B", "C"), ("C", "D"), ("D", "A")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.True(result.HasCycles);
        var cycleScc = result.SCCs.FirstOrDefault(s => s.Count == 4);
        Assert.NotNull(cycleScc);
    }

    [Fact]
    public void Analyze_DiamondDAG_NoCycles()
    {
        var doc = MakeDoc(
            ["A", "B", "C", "D"],
            [("A", "B"), ("A", "C"), ("B", "D"), ("C", "D")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.False(result.HasCycles);
    }

    [Fact]
    public void Analyze_FanOut_CorrectCounts()
    {
        var doc = MakeDoc(
            ["A", "B", "C", "D", "E"],
            [("A", "B"), ("A", "C"), ("A", "D"), ("A", "E")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.Equal(4, result.FanOut["A"]);
        Assert.Equal(0, result.FanOut["B"]);
    }

    [Fact]
    public void Analyze_FanIn_CorrectCounts()
    {
        var doc = MakeDoc(
            ["B", "C", "D", "E", "Z"],
            [("B", "Z"), ("C", "Z"), ("D", "Z"), ("E", "Z")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.Equal(4, result.FanIn["Z"]);
    }

    [Fact]
    public void Analyze_HighFanOut_EmitsT0003()
    {
        var doc = MakeDoc(
            ["S", "A", "B", "C", "D"],
            [("S", "A"), ("S", "B"), ("S", "C"), ("S", "D")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.Contains(result.Diagnostics, d => d.Code == "T0003"
            && d.Message.Contains("S"));
    }

    [Fact]
    public void Analyze_HighFanIn_EmitsT0004()
    {
        var doc = MakeDoc(
            ["A", "B", "C", "D", "Z"],
            [("A", "Z"), ("B", "Z"), ("C", "Z"), ("D", "Z")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.Contains(result.Diagnostics, d => d.Code == "T0004"
            && d.Message.Contains("Z"));
    }

    [Fact]
    public void Analyze_FanOutBelow4_NoI5001()
    {
        var doc = MakeDoc(
            ["S", "A", "B", "C"],
            [("S", "A"), ("S", "B"), ("S", "C")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "T0003");
    }

    [Fact]
    public void Analyze_LinearChain_EachNodeOwnSCC()
    {
        var doc = MakeDoc(["A", "B", "C"], [("A", "B"), ("B", "C")]);
        var result = TopologyAnalyzer.Analyze(doc);

        Assert.Equal(3, result.SCCs.Count);
        Assert.All(result.SCCs, s => Assert.Single(s));
    }

    [Fact]
    public void Analyze_TwoCycles_TwoCyclicSCCs()
    {
        var doc = MakeDoc(
            ["A", "B", "C", "D"],
            [("A", "B"), ("B", "A"), ("C", "D"), ("D", "C")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.True(result.HasCycles);
        var cyclicSccs = result.SCCs.Where(s => s.Count > 1).ToList();
        Assert.Equal(2, cyclicSccs.Count);
    }

    [Fact]
    public void Analyze_ViaIRDocument_ProducesConsistentResult()
    {
        var doc = MakeDoc(["X", "Y"], [("X", "Y")]);

        var viaDoc = TopologyAnalyzer.Analyze(doc);
        var viaGraph = TopologyAnalyzer.Analyze(PipelineGraph.Build(doc));

        Assert.Equal(viaDoc.HasCycles, viaGraph.HasCycles);
        Assert.Equal(viaDoc.SCCs.Count, viaGraph.SCCs.Count);
    }

    [Fact]
    public void Analyze_LinearChain_ComputesDepth()
    {
        var result = TopologyAnalyzer.Analyze(
            MakeDoc(["A", "B", "C", "D"], [("A", "B"), ("B", "C"), ("C", "D")]));

        Assert.Equal(0, result.Depth["A"]);
        Assert.Equal(1, result.Depth["B"]);
        Assert.Equal(2, result.Depth["C"]);
        Assert.Equal(3, result.Depth["D"]);
        Assert.Equal(3, result.MaxDepth);
    }

    [Fact]
    public void Analyze_CycleUsesCondensationDepthAndRealCyclePath()
    {
        var doc = MakeDoc(
            ["A", "B", "C", "D"],
            [("A", "B"), ("B", "C"), ("C", "A"), ("C", "D")]);

        var result = TopologyAnalyzer.Analyze(doc);

        Assert.Equal(result.Depth["A"], result.Depth["B"]);
        Assert.Equal(result.Depth["B"], result.Depth["C"]);
        Assert.Equal(result.Depth["C"] + 1, result.Depth["D"]);
        var message = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "T0001").Message;
        Assert.Contains("A", message);
        Assert.Contains("B", message);
        Assert.Contains("C", message);
    }

    [Fact]
    public void Analyze_DuplicateSameEndpoint_CountsOneLogicalFanout()
    {
        var result = TopologyAnalyzer.Analyze(
            MakeDoc(["A", "B"], [("A", "B"), ("A", "B")]));

        Assert.Equal(1, result.FanOut["A"]);
        Assert.Equal(1, result.FanIn["B"]);
    }
}
