namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Go_NoServerTask_ReturnsEmpty()
    {
        var gen = new GoCodeGenerator();
        var doc = DocWith(EmbeddedTask("Blink"));

        Assert.Empty(gen.Generate(doc, Opts));
    }

    [Fact]
    public void Go_SingleTask_GeneratesBuildableModuleInventory()
    {
        var gen = new GoCodeGenerator();
        var doc = DocWith(ServerTask("Process"));

        var files = gen.Generate(doc, Opts);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.FilePath == "server/main.go");
        Assert.Contains(files, file => file.FilePath == "server/go.mod");
    }
}
