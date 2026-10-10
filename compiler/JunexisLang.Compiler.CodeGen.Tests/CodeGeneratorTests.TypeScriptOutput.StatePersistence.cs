using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: state persistence ─────────────────────────

    [Fact]
    public void TS_StatePersistence_EnablesLocalStorageForStatefulReportTasks()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "Render",
            states: [new IRStateDecl("lastSeen", new IRNamedType("Text"), null)]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("const supportsLocalPersistence = typeof localStorage !== 'undefined';", content);
        Assert.Contains("function initializePlatformFileIO(): void", content);
        Assert.Contains("initializePlatformFileIO();", content);
        Assert.Contains("const persisted = localStorage.getItem('junexis.state.Render');", content);
        Assert.Contains("const parsed = JSON.parse(persisted) as RenderState;", content);
        Assert.Contains("localStorage.setItem('junexis.state.Render', JSON.stringify(state));", content);
    }

    [Fact]
    public async Task TS_StatefulTaskBody_LoadsMutatesAndSavesState()
    {
        var ir = await TransformToIrAsync("""
            task Count(delta: Int) @report -> Int {
                state value: Int = 1
                value = value + delta
                return value
            }
            """);

        var content = new TypeScriptCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("const state = loadCountState();", content);
        Assert.Contains("state.value = (state.value + delta);", content);
        Assert.Contains("return state.value;", content);
        Assert.Contains("finally {", content);
        Assert.Contains("saveCountState(state);", content);
    }
}
