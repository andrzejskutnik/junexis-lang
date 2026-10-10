namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy stanowych i profilowych fragmentów nagłówków generowanych przez CppCodeGenerator.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Cpp_Header_WithState_EmitsStateStruct()
    {
        var gen = new CppCodeGenerator();
        var task = EmbeddedTask("Stateful",
            states: [new IRStateDecl("counter", new IRNamedType("Int"), null)]);
        var header = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "embedded/Stateful.h");

        Assert.Contains("Stateful_State", header.Content);
        Assert.Contains("int32_t counter", header.Content);
    }

    [Fact]
    public void Cpp_Header_EmitsResolvedPlatformProfileConstants()
    {
        var gen = new CppCodeGenerator();
        var header = gen.Generate(DocWith(EmbeddedTask("Read", variant: "esp32c3")), Opts)
            .Single(f => f.FilePath.EndsWith("Read.h"));

        Assert.Contains("struct Read_PlatformProfile", header.Content);
        Assert.Contains("Variant = \"esp32c3\"", header.Content);
        Assert.Contains("SupportsNetworking = true", header.Content);
        Assert.Contains("MaxMemoryBytes = 409600", header.Content);
    }
}
