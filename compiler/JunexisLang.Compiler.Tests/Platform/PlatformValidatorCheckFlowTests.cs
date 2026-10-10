using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.Tests.Platform;

/// <summary>Testy walidacji platform zintegrowanej z przeplywem CheckAsync.</summary>
public sealed partial class PlatformValidatorTests
{
    /// <summary>
    /// Verifies a task exceeding platform memory fails check with E5001.
    /// </summary>
    [Fact]
    public async Task CheckAsync_MemoryExceedsPlatformLimit_EmitsE5001()
    {
        const string source = "task Sensor() @embedded(esp32c3, memory=512KB) { }";

        var result = await new JunexisCompiler().CheckSourceAsync(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "E5001");
    }

    /// <summary>
    /// Verifies unsupported networking requirement fails check with E5003.
    /// </summary>
    [Fact]
    public async Task CheckAsync_UnsupportedNetworking_EmitsE5003()
    {
        const string source = "task Sensor() @embedded(generic, networking=true) { }";

        var result = await new JunexisCompiler().CheckSourceAsync(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "E5003");
    }

    /// <summary>
    /// Verifies high but valid memory usage surfaces a warning without failing the check.
    /// </summary>
    [Fact]
    public async Task CheckAsync_HighMemoryUsage_EmitsW5002AndKeepsSuccess()
    {
        const string source = "task Sensor() @embedded(esp32c3, memory=340KB) { }";

        var result = await new JunexisCompiler().CheckSourceAsync(source);

        Assert.True(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "W5002");
    }

    /// <summary>
    /// Verifies inherited server profile constraints flow into check diagnostics.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ServerInheritedProfile_EmitsProfileConstraints()
    {
        const string source = "task Api() @server(\"linux-container\") { }";

        var result = await new JunexisCompiler().CheckSourceAsync(source);

        Assert.True(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EPHEMERAL_STORAGE");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CONTAINER_NETWORK");
    }

    /// <summary>
    /// Verifies profile-level informational constraints are emitted once per profile,
    /// not once per task.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ReportProfileConstraints_AreDeduplicatedPerProfile()
    {
        const string source = """
            task Panel() @report { }
            task Dashboard() @report { }
            """;

        var result = await new JunexisCompiler().CheckSourceAsync(source);

        Assert.True(result.Success);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "NO_FILE_IO");
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "CORS");
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "MEMORY_LIMIT");
    }

    /// <summary>Eksperymentalna platforma bez profilu nie może przerwać procesu.</summary>
    [Fact]
    public async Task CheckAsync_UnknownExperimentalPlatform_EmitsW5000WithoutCrash()
    {
        const string source = "task EdgeWorker() @edge { }";

        var result = await new JunexisCompiler().CheckSourceAsync(source);

        Assert.True(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "W5000");
    }
}
