namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>Kontrakty pierwszego pionu aplikacji przeglądarkowej M10.</summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void TS_Report_EmitsSelfContainedBrowserApplicationInventory()
    {
        var generator = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "HomeReport",
            @params: [new IRParam("observations", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)]);

        var files = generator.Generate(DocWith(task), Opts);
        var paths = files.Select(file => file.FilePath).ToHashSet(StringComparer.Ordinal);

        foreach (var required in new[]
        {
            "report/index.html",
            "report/browser.ts",
            "report/report-view.ts",
            "report/report-adapter-contract.ts",
            "report/user-adapter.ts",
            "report/report-view-contract.ts",
            "report/user-view.ts",
            "report/report.css",
            "report/serve.mjs",
            "report/junexis-report-manifest.json"
        })
            Assert.Contains(required, paths);

        var result = GeneratedArtifactContract.Validate(DocWith(task), "report", files);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Violations.Select(violation => violation.Message)));
    }

    [Fact]
    public void TS_Report_DeclarativeView_HasBoundedFieldRendererAndAutomaticFallback()
    {
        var files = new TypeScriptCodeGenerator().Generate(
            DocWith(ReportTask("HomeReport")),
            Opts);
        var contract = files.Single(file => file.FilePath == "report/report-view-contract.ts").Content;
        var fallback = files.Single(file => file.FilePath == "report/user-view.ts").Content;
        var view = files.Single(file => file.FilePath == "report/report-view.ts").Content;

        Assert.Contains("JunexisReportView", contract);
        Assert.Contains("'grid' | 'list'", contract);
        Assert.Contains("userReportView: JunexisReportView | undefined = undefined", fallback);
        Assert.Contains("declarativePanel(taskName)", view);
        Assert.Contains("readPath(item, field.path)", view);
        Assert.Contains("text(JSON.stringify(value, null, 2))", view);
        Assert.Contains("current.appendChild(renderValue(item));", view);
    }

    [Fact]
    public void TS_Report_UserAdapter_HasTypedHooksAndSafeAutomaticFallback()
    {
        var generator = new TypeScriptCodeGenerator();
        var task = ReportTask("HomeReport");
        var files = generator.Generate(DocWith(task), Opts);
        var contract = files.Single(file => file.FilePath == "report/report-adapter-contract.ts").Content;
        var adapter = files.Single(file => file.FilePath == "report/user-adapter.ts").Content;
        var view = files.Single(file => file.FilePath == "report/report-view.ts").Content;

        Assert.Contains("interface JunexisReportAdapter", contract);
        Assert.Contains("mount?", contract);
        Assert.Contains("onObservation?", contract);
        Assert.Contains("onConnection?", contract);
        Assert.Contains("export const userReportAdapter", adapter);
        Assert.Contains("if (!adapterOwnsView) taskNames.forEach(ensurePanel);", view);
        Assert.Contains("reportAdapterError('onObservation'", view);
        Assert.Contains("if (adapterFailure)", view);
    }

    [Fact]
    public void TS_Report_AutomaticRenderer_IsBoundedAndUsesSafeDomOperations()
    {
        var generator = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "HomeReport",
            @params: [new IRParam("observations", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)]);
        var files = generator.Generate(DocWith(task), Opts);
        var view = files.Single(file => file.FilePath == "report/report-view.ts").Content;
        var index = files.Single(file => file.FilePath == "report/index.ts").Content;
        var html = files.Single(file => file.FilePath == "report/index.html").Content;
        var package = files.Single(file => file.FilePath == "report/package.json").Content;

        Assert.Contains("const HISTORY_LIMIT = 50;", view);
        Assert.Contains("const MAX_COLLECTION_ITEMS = 20;", view);
        Assert.Contains("textContent", view);
        Assert.DoesNotContain("innerHTML", view);
        Assert.Contains("renderReportItem('HomeReport', item);", index);
        Assert.Contains("reconnecting: 'Połączenie przerwane — ponawianie…'", view);
        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("dist/browser.js", html);
        Assert.Contains("esbuild ./browser.ts --bundle", package);
        Assert.Contains("\"app:start\": \"node serve.mjs\"", package);
        var server = files.Single(file => file.FilePath == "report/serve.mjs").Content;
        Assert.Contains("const root = resolve(fileURLToPath", server);
    }

    [Fact]
    public void TS_Report_Manifest_DeclaresAutomaticRendererAndWireContract()
    {
        var generator = new TypeScriptCodeGenerator();
        var task = ReportTask("HomeReport");
        var manifest = generator.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "report/junexis-report-manifest.json")
            .Content;

        Assert.Contains("\"schemaVersion\": \"junexis.report-application/0.1\"", manifest);
        Assert.Contains("\"renderer\": \"automatic\"", manifest);
        Assert.Contains("\"wireContract\": \"junexis-wire/0.2\"", manifest);
        Assert.Contains("\"historyLimit\": 50", manifest);
        Assert.Contains("\"name\": \"HomeReport\"", manifest);
    }
}
