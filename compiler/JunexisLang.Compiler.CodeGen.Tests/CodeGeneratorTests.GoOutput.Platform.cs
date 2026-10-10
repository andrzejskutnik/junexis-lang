namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Go_Output_EmitsResolvedPlatformProfileConstants()
    {
        var gen = new GoCodeGenerator();
        var content = gen.Generate(DocWith(ServerTask("Process")), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("const ProcessPlatformVariant = \"linux\"", content);
        Assert.Contains("const ProcessSupportsThreading = true", content);
        Assert.Contains("const ProcessSupportsNetworking = true", content);
        Assert.Contains("const ProcessSupportsFileIO = true", content);
    }

    [Fact]
    public void Go_Output_UsesCapabilitiesForImportsAndBootstrap()
    {
        var gen = new GoCodeGenerator();
        var content = gen.Generate(DocWith(ServerTask("Process")), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("\t\"net/http\"", content);
        Assert.Contains("\t\"os\"", content);
        Assert.Contains("\t\"runtime\"", content);
        Assert.Contains("func configurePlatformNetworking()", content);
        Assert.Contains("func configurePlatformFileIO()", content);
        Assert.Contains("func configurePlatformThreading()", content);
        Assert.Contains("configurePlatformNetworking()", content);
        Assert.Contains("configurePlatformFileIO()", content);
        Assert.Contains("configurePlatformThreading()", content);
        Assert.Contains("_ = http.MethodGet", content);
        Assert.Contains("_ = os.Stdout", content);
        Assert.Contains("_ = runtime.NumCPU()", content);
    }

    [Fact]
    public void Go_Platform_IsServer()
        => Assert.Equal("server", new GoCodeGenerator().Platform);
}
