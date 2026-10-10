namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>
/// Testy fabryki generatorów kodu.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Factory_NullTargets_ReturnsAllFour()
    {
        var gens = CodeGeneratorFactory.Get(null);

        Assert.Equal(4, gens.Count);
        Assert.Contains(gens, g => g.Platform == "embedded");
        Assert.Contains(gens, g => g.Platform == "server");
        Assert.Contains(gens, g => g.Platform == "report");
        Assert.Contains(gens, g => g.Platform == "proto");
    }

    [Fact]
    public void Factory_SpecificTarget_ReturnsSingleGenerator()
    {
        var gens = CodeGeneratorFactory.Get(["server"]);

        Assert.Single(gens);
        Assert.Equal("server", gens[0].Platform);
    }

    [Fact]
    public void Factory_UnknownTarget_ReturnsEmpty()
    {
        var gens = CodeGeneratorFactory.Get(["rust"]);

        Assert.Empty(gens);
    }

    [Fact]
    public void Factory_CaseInsensitive_Works()
    {
        var gens = CodeGeneratorFactory.Get(["EMBEDDED"]);

        Assert.Single(gens);
        Assert.Equal("embedded", gens[0].Platform);
    }
}