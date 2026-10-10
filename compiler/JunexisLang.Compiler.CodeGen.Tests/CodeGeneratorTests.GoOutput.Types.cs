namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_Output_Params_MappedToGoTypes()
    {
        const string source = """
            task Store(id: Int) @server {
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("id int64", content);
    }

    [Fact]
    public async Task Go_Output_ReturnType_MappedToGoType()
    {
        const string source = """
            task Fetch() @server -> Text {
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("func Fetch() string", content);
    }
}
