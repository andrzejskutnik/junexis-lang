using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: types.ts ──────────────────────────────────

    [Fact]
    public void TS_Types_ContainsInterface()
    {
        var gen = new TypeScriptCodeGenerator();
        var doc = DocWith(
            [ReportTask("Show")],
            structs: [new IRStruct("Metric", [new IRField("value", new IRNamedType("Float"), null)])]);
        var types = gen.Generate(doc, Opts).Single(f => f.FilePath == "report/types.ts");
        Assert.Contains("export interface Metric", types.Content);
        Assert.Contains("value: number", types.Content);
    }

    [Fact]
    public void TS_Types_ContainsEnumType()
    {
        var gen = new TypeScriptCodeGenerator();
        var doc = DocWith(
            [ReportTask("Show")],
            enums: [new IREnum("Status",
                [],
                [new IREnumVariant("Ok", []),
                 new IREnumVariant("Err", [new IRNamedType("Text")])])]);
        var types = gen.Generate(doc, Opts).Single(f => f.FilePath == "report/types.ts");
        Assert.Contains("export type Status", types.Content);
        Assert.Contains("'Ok'", types.Content);
        Assert.Contains("'Err'", types.Content);
    }
}
