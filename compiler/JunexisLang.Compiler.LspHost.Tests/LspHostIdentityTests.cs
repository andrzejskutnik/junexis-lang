using System.Text.Json;

namespace JunexisLang.Compiler.LspHost.Tests;

public sealed class LspHostIdentityTests
{
    [Fact]
    public void Identity_IsMachineReadableAndCarriesProtocolVersion()
    {
        using var identity = JsonDocument.Parse(LspHostIdentity.RenderJson());
        var root = identity.RootElement;

        Assert.Equal("JunexisLang.Compiler.LspHost", root.GetProperty("name").GetString());
        Assert.Equal(LspHostIdentity.ProtocolVersion, root.GetProperty("protocolVersion").GetInt32());
        Assert.Matches(@"^\d+\.\d+\.\d+", root.GetProperty("version").GetString());
    }

    [Fact]
    public void DistributionManifest_CarriesPlatformAndPackagingContract()
    {
        using var manifest = JsonDocument.Parse(
            LspHostIdentity.RenderDistributionManifest(
                "linux-arm64",
                "JunexisLang.Compiler.LspHost",
                selfContained: true));
        var root = manifest.RootElement;

        Assert.Equal(
            LspHostIdentity.DistributionSchemaVersion,
            root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("linux-arm64", root.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal(
            "JunexisLang.Compiler.LspHost",
            root.GetProperty("entrypoint").GetString());
        Assert.True(root.GetProperty("readyToRun").GetBoolean());
        Assert.True(root.GetProperty("selfContained").GetBoolean());
    }

    [Theory]
    [InlineData("../host")]
    [InlineData("bin/host")]
    [InlineData("bin\\host.exe")]
    public void DistributionManifest_RejectsEntrypointTraversal(string entrypoint)
    {
        Assert.Throws<ArgumentException>(() =>
            LspHostIdentity.RenderDistributionManifest(
                "linux-x64",
                entrypoint,
                selfContained: true));
    }
}
