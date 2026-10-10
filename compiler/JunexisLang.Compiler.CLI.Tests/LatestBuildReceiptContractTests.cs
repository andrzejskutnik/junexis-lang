using System.Text.Json.Nodes;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class LatestBuildReceiptContractTests
{
    [Fact]
    public void Contract_RoundTripsCanonicalVersion10Receipt()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "junexis-receipt-contract"));
        var output = Path.Combine(root, "out");
        var created = LatestBuildReceiptContract.Create(
            new string('a', 64),
            "project",
            root,
            Path.Combine(root, "src", "main.jnx"),
            output,
            ["server", "report"],
            Path.Combine(output, "junexis.bundle.json"),
            Path.Combine(output, "junexis.artifacts.json"),
            Path.Combine(output, "debug", "junexis.debug.toml"),
            Path.Combine(output, "analysis", "junexis.analysis.json"));

        var parsed = LatestBuildReceiptContract.Parse(created.Json);

        Assert.Equal("1.0", parsed.SchemaVersion);
        Assert.Equal(new string('a', 64), parsed.BuildId);
        Assert.Equal(["server", "report"], parsed.Targets);
        Assert.Equal(Path.Combine(root, ".junexis", "state", "latest-build.json"), created.Path);
    }

    [Fact]
    public void Contract_RejectsUnsupportedSchema()
    {
        var json = CreateValidJson();
        var document = JsonNode.Parse(json)!.AsObject();
        document["schemaVersion"] = "2.0";

        var error = Assert.Throws<InvalidDataException>(
            () => LatestBuildReceiptContract.Parse(document.ToJsonString()));

        Assert.Contains("unsupported latest-build schema", error.Message);
    }

    [Fact]
    public void Contract_RejectsArtifactPathOutsideOutputRoot()
    {
        var json = CreateValidJson();
        var document = JsonNode.Parse(json)!.AsObject();
        document["artifactManifest"] = Path.Combine(Path.GetTempPath(), "foreign", "artifacts.json");

        var error = Assert.Throws<InvalidDataException>(
            () => LatestBuildReceiptContract.Parse(document.ToJsonString()));

        Assert.Contains("artifactManifest must stay inside", error.Message);
    }

    [Fact]
    public void Contract_RejectsMissingTargetsAsInvalidData()
    {
        var document = JsonNode.Parse(CreateValidJson())!.AsObject();
        document["targets"] = null;

        var error = Assert.Throws<InvalidDataException>(
            () => LatestBuildReceiptContract.Parse(document.ToJsonString()));

        Assert.Contains("targets must contain", error.Message);
    }

    private static string CreateValidJson()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "junexis-receipt-validation"));
        var output = Path.Combine(root, "out");
        return LatestBuildReceiptContract.Create(
            new string('0', 64),
            "file",
            root,
            Path.Combine(root, "main.jnx"),
            output,
            ["server"],
            Path.Combine(output, "junexis.bundle.json"),
            Path.Combine(output, "junexis.artifacts.json"),
            Path.Combine(output, "debug", "junexis.debug.toml"),
            Path.Combine(output, "analysis", "junexis.analysis.json")).Json;
    }
}
