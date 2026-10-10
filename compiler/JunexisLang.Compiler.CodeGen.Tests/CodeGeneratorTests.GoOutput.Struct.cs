namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Go_Output_Struct_Emitted()
    {
        var gen = new GoCodeGenerator();
        var doc = DocWith(
            [ServerTask("Proc")],
            structs: [new IRStruct("Event", [new IRField("id", new IRNamedType("Int"), null)])]);
        var content = gen.Generate(doc, Opts)[0].Content;

        Assert.Contains("type Event struct", content);
        Assert.Contains("Id int64 `json:\"id\" msgpack:\"id\"`", content);
    }
}
