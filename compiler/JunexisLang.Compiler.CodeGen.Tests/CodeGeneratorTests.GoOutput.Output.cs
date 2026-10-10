namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_Output_ContainsPackageMain()
    {
        const string source = """
            task Process() @server {
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("package main", content);
    }

    [Fact]
    public async Task Go_Output_ContainsFuncDeclaration()
    {
        const string source = """
            task Handle() @server {
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("func Handle()", content);
    }
}
