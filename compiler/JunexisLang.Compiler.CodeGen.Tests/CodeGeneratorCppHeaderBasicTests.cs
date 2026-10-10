namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy podstawowego kształtu nagłówków generowanych przez CppCodeGenerator.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Cpp_Header_ContainsPragmaOnce()
    {
        var gen = new CppCodeGenerator();
        var doc = DocWith(EmbeddedTask("Read"));
        var header = gen.Generate(doc, Opts)
            .Single(f => f.FilePath == "embedded/Read.h");

        Assert.Contains("#pragma once", header.Content);
    }

    [Fact]
    public void Cpp_Header_ContainsTaskDeclaration()
    {
        var gen = new CppCodeGenerator();
        var task = EmbeddedTask("MySensor", outputType: new IRNamedType("Float"));
        var header = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "embedded/MySensor.h");

        Assert.Contains("task_MySensor", header.Content);
        Assert.Contains("float", header.Content);
    }

    [Fact]
    public void Cpp_Header_Params_MappedToCppTypes()
    {
        var gen = new CppCodeGenerator();
        var task = EmbeddedTask("Send",
            @params: [new IRParam("msg", new IRNamedType("Text"), null)]);
        var header = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "embedded/Send.h");

        Assert.Contains("std::string msg", header.Content);
    }
}
