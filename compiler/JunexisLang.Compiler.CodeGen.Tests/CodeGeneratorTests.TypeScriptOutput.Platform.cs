using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: platform ──────────────────────────────────

    [Fact]
    public void TS_Output_EmitsResolvedPlatformProfileConstants()
    {
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(DocWith(ReportTask("Render")), Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("export const RenderPlatformProfile = {", content);
        Assert.Contains("variant: 'browser'", content);
        Assert.Contains("supportsThreading: true", content);
        Assert.Contains("supportsNetworking: true", content);
        Assert.Contains("supportsFileIO: false", content);
    }

    [Fact]
    public void TS_Output_UsesCapabilitiesForBrowserBootstrap()
    {
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(DocWith(ReportTask("Render")), Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("const supportsWorkers = typeof Worker !== 'undefined';", content);
        Assert.Contains("const supportsFetch = typeof fetch === 'function';", content);
        Assert.DoesNotContain("const supportsLocalPersistence = typeof localStorage !== 'undefined';", content);
        Assert.Contains("function initializePlatformThreading(): void", content);
        Assert.Contains("function initializePlatformNetworking(): void", content);
        Assert.Contains("initializePlatformThreading();", content);
        Assert.Contains("initializePlatformNetworking();", content);
        Assert.Contains("void supportsWorkers;", content);
        Assert.Contains("void supportsFetch;", content);
    }

    [Fact]
    public void TS_Platform_IsReport()
        => Assert.Equal("report", new TypeScriptCodeGenerator().Platform);
}
