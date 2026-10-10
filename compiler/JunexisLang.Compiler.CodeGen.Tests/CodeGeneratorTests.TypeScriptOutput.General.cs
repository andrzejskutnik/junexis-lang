namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: output/general ────────────────────────────

    [Fact]
    public void TS_NoReportTask_ReturnsEmpty()
    {
        var gen = new TypeScriptCodeGenerator();
        var doc = DocWith(ServerTask("Srv"));

        Assert.Empty(gen.Generate(doc, Opts));
    }

    [Fact]
    public void TS_SingleTask_GeneratesSelfContainedProjectInventory()
    {
        var gen = new TypeScriptCodeGenerator();
        var doc = DocWith(ReportTask("Dashboard"));

        var files = gen.Generate(doc, Opts);

        Assert.Equal(16, files.Count);
        Assert.Contains(files, f => f.FilePath == "report/types.ts");
        Assert.Contains(files, f => f.FilePath == "report/index.ts");
        Assert.Contains(files, f => f.FilePath == "report/runtime.ts");
        Assert.Contains(files, f => f.FilePath == "report/deploy.ts");
        Assert.Contains(files, f => f.FilePath == "report/browser.ts");
        Assert.Contains(files, f => f.FilePath == "report/report-view.ts");
        Assert.Contains(files, f => f.FilePath == "report/report.css");
        Assert.Contains(files, f => f.FilePath == "report/index.html");
        Assert.Contains(files, f => f.FilePath == "report/serve.mjs");
        Assert.Contains(files, f => f.FilePath == "report/junexis-report-manifest.json");
        Assert.Contains(files, f => f.FilePath == "report/package.json");
        Assert.Contains(files, f => f.FilePath == "report/report-view-contract.ts");
        Assert.Contains(files, f => f.FilePath == "report/user-view.ts");
        Assert.Contains(files, f => f.FilePath == "report/tsconfig.json");
        var bootstrap = files.Single(f => f.FilePath == "report/deploy.ts").Content;
        Assert.Contains("junexis.local-runtime/0.1", bootstrap);
        Assert.Contains("void Dashboard().catch", bootstrap);
        Assert.Contains("runtimeProcess.exit(1);", bootstrap);
        Assert.DoesNotContain("runtimeProcess.exitCode = 1;", bootstrap);
    }
}
