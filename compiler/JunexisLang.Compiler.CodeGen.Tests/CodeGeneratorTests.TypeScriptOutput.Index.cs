using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: index.ts ──────────────────────────────────

    [Fact]
    public async Task TS_Index_ContainsExportAsyncFunction()
    {
        const string source = """
            task ShowChart() @report {
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts).Single(f => f.FilePath == "report/index.ts");
        Assert.Contains("export async function ShowChart", index.Content);
    }

    [Fact]
    public async Task TS_Index_ReturnType_DefaultIsPromiseVoid()
    {
        const string source = """
            task Noop() @report {
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts).Single(f => f.FilePath == "report/index.ts");
        Assert.Contains("Promise<void>", index.Content);
    }

    [Fact]
    public async Task TS_Index_ReturnType_MappedForOutputType()
    {
        const string source = """
            task Fetch() @report -> Text {
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts).Single(f => f.FilePath == "report/index.ts");
        Assert.Contains("Promise<string>", index.Content);
    }

    [Fact]
    public async Task TS_Index_Params_MappedToTSTypes()
    {
        const string source = """
            task Filter(count: Int) @report {
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts).Single(f => f.FilePath == "report/index.ts");
        Assert.Contains("count: number", index.Content);
    }
}
