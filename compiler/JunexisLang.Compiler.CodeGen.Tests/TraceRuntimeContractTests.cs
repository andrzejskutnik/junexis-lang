namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>Testy kanonicznej polityki instrumentacji runtime.</summary>
public sealed class TraceRuntimeContractTests
{
    [Fact]
    public void ResolvePolicy_WithoutNarrative_DisablesTracing()
    {
        Assert.Equal(TraceEmissionPolicy.Off, TraceRuntimeContract.ResolvePolicy([]));
    }

    [Theory]
    [InlineData("intent \"observe\"", TraceEmissionPolicy.Minimal)]
    [InlineData("trace causal", TraceEmissionPolicy.Minimal)]
    [InlineData("trace: minimal", TraceEmissionPolicy.Minimal)]
    [InlineData("trace full", TraceEmissionPolicy.Full)]
    [InlineData("TRACE: FULL", TraceEmissionPolicy.Full)]
    [InlineData("trace none", TraceEmissionPolicy.Off)]
    [InlineData("trace: off", TraceEmissionPolicy.Off)]
    public void ResolvePolicy_UsesFirstNarrativeDeclaration(
        string body,
        TraceEmissionPolicy expected)
    {
        var narratives = new[] { new IRNarrative("Story", body) };

        Assert.Equal(expected, TraceRuntimeContract.ResolvePolicy(narratives));
    }

    [Fact]
    public void Contract_MatchesTraceCollectorV1()
    {
        Assert.Equal("junexis.tracecollector.v1", TraceRuntimeContract.EventModel);
        Assert.Equal("JUNEXIS_TRACE_URL", TraceRuntimeContract.TraceUrlEnvironmentVariable);
        Assert.Equal("JUNEXIS_BUILD_ID", TraceRuntimeContract.BuildIdEnvironmentVariable);
        Assert.Equal("JUNEXIS_BUNDLE_ID", TraceRuntimeContract.BundleIdEnvironmentVariable);
        Assert.Equal("build_id", TraceRuntimeContract.BuildIdAttribute);
        Assert.Equal("bundle_id", TraceRuntimeContract.BundleIdAttribute);
        Assert.Equal("/events", TraceRuntimeContract.EventsPath);
        Assert.Equal("/debug/session", TraceRuntimeContract.DebugSessionPath);
        Assert.Equal("COMM_SEND", TraceRuntimeContract.CommunicationSend);
        Assert.Equal("COMM_RECV", TraceRuntimeContract.CommunicationReceive);
    }
}
