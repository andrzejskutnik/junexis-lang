using JunexisLang.Compiler.Core.Runtime;

namespace JunexisLang.Compiler.Tests.Runtime;

public sealed class RuntimePrimitiveRegistryTests
{
    [Fact]
    public void Now_IsSupportedAcrossAllTargets()
    {
        Assert.True(RuntimePrimitiveRegistry.TryGet("now", out var primitive));
        Assert.Equal(RuntimePrimitiveStatus.Supported, primitive.Status);
        Assert.True(primitive.SupportsSurface(RuntimePrimitiveSurface.Function));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Server));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Report));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Embedded));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.GoCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.TypeScriptCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.CppCodegen));
    }

    [Theory]
    [InlineData("send")]
    [InlineData("recv")]
    public void SendAndRecv_AreStableCommunicationPrimitives(string name)
    {
        Assert.True(RuntimePrimitiveRegistry.TryGet(name, out var primitive));
        Assert.True(RuntimePrimitiveRegistry.IsSupported(name));
        Assert.Equal(RuntimePrimitiveStatus.Supported, primitive.Status);

        Assert.True(primitive.SupportsSurface(RuntimePrimitiveSurface.Statement));
        Assert.True(primitive.SupportsSurface(RuntimePrimitiveSurface.Expression));
        Assert.False(primitive.SupportsSurface(RuntimePrimitiveSurface.Function));

        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Server));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Report));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Embedded));

        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.GoCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.TypeScriptCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.CppCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.DotNetRuntime));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.EmbeddedCRuntime));
    }

    [Fact]
    public void RecvAny_IsSupportedForServerAndReportFanIn()
    {
        Assert.True(RuntimePrimitiveRegistry.TryGet("recv_any", out var primitive));
        Assert.True(RuntimePrimitiveRegistry.IsKnown("recv_any"));
        Assert.True(RuntimePrimitiveRegistry.IsSupported("recv_any"));
        Assert.Equal(RuntimePrimitiveStatus.Supported, primitive.Status);
        Assert.True(primitive.SupportsSurface(RuntimePrimitiveSurface.Function));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Server));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Report));
        Assert.False(primitive.SupportsTarget(RuntimePrimitiveTarget.Embedded));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.GoCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.TypeScriptCodegen));
        Assert.False(primitive.HasImplementation(RuntimePrimitiveImplementation.CppCodegen));
    }

    [Theory]
    [InlineData("f32")]
    [InlineData("u64")]
    [InlineData("float32")]
    [InlineData("uint64")]
    public void NumericCasts_AreSupportedRuntimePrimitives(string name)
    {
        Assert.True(RuntimePrimitiveRegistry.TryGet(name, out var primitive));
        Assert.True(RuntimePrimitiveRegistry.IsSupported(name));
        Assert.True(primitive.SupportsSurface(RuntimePrimitiveSurface.Function));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Server));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Report));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Embedded));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.GoCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.TypeScriptCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.CppCodegen));
    }

    [Theory]
    [InlineData("pobierz")]
    [InlineData("zapisz")]
    [InlineData("usun")]
    public void PolishStorageAliases_AreKnownButProposed(string name)
    {
        Assert.True(RuntimePrimitiveRegistry.TryGet(name, out var primitive));
        Assert.True(RuntimePrimitiveRegistry.IsKnown(name));
        Assert.False(RuntimePrimitiveRegistry.IsSupported(name));
        Assert.Equal(RuntimePrimitiveStatus.Proposed, primitive.Status);
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Server));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Report));
        Assert.False(primitive.SupportsTarget(RuntimePrimitiveTarget.Embedded));
        Assert.Equal(RuntimePrimitiveImplementation.None, primitive.Implementations);
    }

    [Theory]
    [InlineData("ack")]
    [InlineData("nack")]
    public void QueueAcknowledgementPrimitives_AreKnownButProposedStatements(string name)
    {
        Assert.True(RuntimePrimitiveRegistry.TryGet(name, out var primitive));
        Assert.True(RuntimePrimitiveRegistry.IsKnown(name));
        Assert.False(RuntimePrimitiveRegistry.IsSupported(name));
        Assert.Equal(RuntimePrimitiveStatus.Proposed, primitive.Status);
        Assert.True(primitive.SupportsSurface(RuntimePrimitiveSurface.Statement));
        Assert.False(primitive.SupportsSurface(RuntimePrimitiveSurface.Function));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Server));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Report));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Embedded));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.GoCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.TypeScriptCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.CppCodegen));
    }

    [Fact]
    public void LatestPrimitive_IsKnownProposedAndLoweredForServerAndReport()
    {
        Assert.True(RuntimePrimitiveRegistry.TryGet("latest", out var primitive));
        Assert.True(RuntimePrimitiveRegistry.IsKnown("latest"));
        Assert.False(RuntimePrimitiveRegistry.IsSupported("latest"));
        Assert.Equal(RuntimePrimitiveStatus.Proposed, primitive.Status);
        Assert.True(primitive.SupportsSurface(RuntimePrimitiveSurface.Function));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Server));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Report));
        Assert.False(primitive.SupportsTarget(RuntimePrimitiveTarget.Embedded));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.GoCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.TypeScriptCodegen));
        Assert.False(primitive.HasImplementation(RuntimePrimitiveImplementation.CppCodegen));
    }

    [Fact]
    public void BatchPrimitive_IsKnownProposedAndLoweredForServerAndReport()
    {
        Assert.True(RuntimePrimitiveRegistry.TryGet("batch", out var primitive));
        Assert.True(RuntimePrimitiveRegistry.IsKnown("batch"));
        Assert.False(RuntimePrimitiveRegistry.IsSupported("batch"));
        Assert.Equal(RuntimePrimitiveStatus.Proposed, primitive.Status);
        Assert.True(primitive.SupportsSurface(RuntimePrimitiveSurface.Function));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Server));
        Assert.True(primitive.SupportsTarget(RuntimePrimitiveTarget.Report));
        Assert.False(primitive.SupportsTarget(RuntimePrimitiveTarget.Embedded));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.GoCodegen));
        Assert.True(primitive.HasImplementation(RuntimePrimitiveImplementation.TypeScriptCodegen));
        Assert.False(primitive.HasImplementation(RuntimePrimitiveImplementation.CppCodegen));
    }

    [Fact]
    public void Registry_DoesNotExposeDuplicatePrimitiveNames()
    {
        var names = RuntimePrimitiveRegistry.List().Select(primitive => primitive.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
