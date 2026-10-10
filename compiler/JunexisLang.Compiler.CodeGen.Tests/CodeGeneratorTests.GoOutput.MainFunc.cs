namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Go_Output_MainFunc_SpawnsGoroutines()
    {
        var gen = new GoCodeGenerator();
        var doc = DocWith([ServerTask("A"), ServerTask("B")]);
        var content = gen.Generate(doc, Opts)[0].Content;

        Assert.Contains("func main()", content);
        Assert.Contains("go func()", content);
    }
}
