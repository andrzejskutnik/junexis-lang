using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: output task / result handles ──────────────

    [Fact]
    public async Task TS_OutputTask_DeclaresAndReturnsResultHandle()
    {
        const string source = """
            task Publish() @report -> stream<Int> {
                log("tick")
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts).Single(f => f.FilePath == "report/index.ts");
        Assert.Contains("let __latestResult: number | undefined;", index.Content);
        Assert.Contains("const result = {", index.Content);
        Assert.Contains("return result;", index.Content);
    }

    [Fact]
    public async Task TS_SendStmt_WithoutTarget_OnStreamOutput_EmitsSpecificTodo()
    {
        const string source = """
            task Publish() @report -> stream<Int> {
                let reading: Int = 7
                send reading
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts).Single(f => f.FilePath == "report/index.ts");
        Assert.Contains("result.publish(reading);", index.Content);
    }

    [Fact]
    public async Task TS_OutputTask_WithQueueOutput_DeclaresAndReturnsResultHandle()
    {
        const string source = """
            task Publish() @report -> queue<Int> {
                log("tick")
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts).Single(f => f.FilePath == "report/index.ts");
        Assert.Contains("const __queuedResults: number[] = [];", index.Content);
        Assert.Contains("const result = {", index.Content);
        Assert.Contains("enqueue(value: number): void {", index.Content);
        Assert.Contains("return result;", index.Content);
    }

    [Fact]
    public async Task TS_SendStmt_WithoutTarget_OnQueueOutput_EnqueuesIntoGeneratedHandle()
    {
        const string source = """
            task Publish() @report -> queue<Int> {
                let reading: Int = 7
                send reading
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts).Single(f => f.FilePath == "report/index.ts");
        Assert.Contains("result.enqueue(reading);", index.Content);
    }

    [Fact]
    public async Task TS_ZeroPayloadEnumAccess_EmitsTaggedUnionLiteral()
    {
        const string source = """
            enum Status {
                Ok,
                Error
            }

            task BuildStatus() @report -> Text {
                let status = Status::Ok
                return "ok"
            }
            """;
        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts).Single(file => file.FilePath == "report/index.ts").Content;
        Assert.Contains("export async function BuildStatus(): Promise<string>", content);
        Assert.Contains("let status = { kind: 'Ok' };", content);
        Assert.Contains("return 'ok';", content);
    }
}
