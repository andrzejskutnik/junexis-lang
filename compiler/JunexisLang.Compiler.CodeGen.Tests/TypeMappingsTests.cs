namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>Testy jednostkowe <see cref="TypeMappings"/>.</summary>
public sealed class TypeMappingsTests
{
    // ─── ToCpp ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Int",   "int32_t")]
    [InlineData("Float", "float")]
    [InlineData("Bool",  "bool")]
    [InlineData("Text",  "std::string")]
    [InlineData("MyStruct", "MyStruct")]
    public void ToCpp_PrimitiveAndNamed(string typeName, string expected)
        => Assert.Equal(expected, TypeMappings.ToCpp(new IRNamedType(typeName)));

    [Theory]
    [InlineData(IRCommKind.Chan,   "junexis::Channel<int32_t>")]
    [InlineData(IRCommKind.Queue,  "junexis::Queue<int32_t>")]
    [InlineData(IRCommKind.Stream, "junexis::Stream<int32_t>")]
    public void ToCpp_CommTypes(IRCommKind kind, string expected)
        => Assert.Equal(expected, TypeMappings.ToCpp(
            new IRCommType(kind, new IRNamedType("Int"))));

    [Fact]
    public void ToCpp_ArrayType()
        => Assert.Equal("std::vector<float>",
            TypeMappings.ToCpp(new IRArrayType(new IRNamedType("Float"))));

    [Fact]
    public void ToCpp_UnionType_TwoVariants()
        => Assert.Equal("std::variant<int32_t, bool>",
            TypeMappings.ToCpp(new IRUnionType(
                [new IRNamedType("Int"), new IRNamedType("Bool")])));

    // ─── ToGo ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Int",   "int64")]
    [InlineData("Float", "float64")]
    [InlineData("Bool",  "bool")]
    [InlineData("Text",  "string")]
    [InlineData("Event", "Event")]
    public void ToGo_PrimitiveAndNamed(string typeName, string expected)
        => Assert.Equal(expected, TypeMappings.ToGo(new IRNamedType(typeName)));

    [Theory]
    [InlineData(IRCommKind.Chan,   "chan int64")]
    [InlineData(IRCommKind.Queue,  "*Queue[int64]")]
    [InlineData(IRCommKind.Stream, "*Stream[int64]")]
    public void ToGo_CommTypes(IRCommKind kind, string expected)
        => Assert.Equal(expected, TypeMappings.ToGo(
            new IRCommType(kind, new IRNamedType("Int"))));

    [Fact]
    public void ToGo_ArrayType()
        => Assert.Equal("[]string",
            TypeMappings.ToGo(new IRArrayType(new IRNamedType("Text"))));

    // ─── ToTypeScript ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Int",   "number")]
    [InlineData("Float", "number")]
    [InlineData("Bool",  "boolean")]
    [InlineData("Text",  "string")]
    [InlineData("Metric","Metric")]
    public void ToTypeScript_PrimitiveAndNamed(string typeName, string expected)
        => Assert.Equal(expected, TypeMappings.ToTypeScript(new IRNamedType(typeName)));

    [Theory]
    [InlineData(IRCommKind.Chan,   "Channel<number>")]
    [InlineData(IRCommKind.Queue,  "Queue<number>")]
    [InlineData(IRCommKind.Stream, "Stream<number>")]
    public void ToTypeScript_CommTypes(IRCommKind kind, string expected)
        => Assert.Equal(expected, TypeMappings.ToTypeScript(
            new IRCommType(kind, new IRNamedType("Int"))));

    [Fact]
    public void ToTypeScript_ArrayType()
        => Assert.Equal("string[]",
            TypeMappings.ToTypeScript(new IRArrayType(new IRNamedType("Text"))));

    [Fact]
    public void ToTypeScript_UnionType()
        => Assert.Equal("number | boolean",
            TypeMappings.ToTypeScript(new IRUnionType(
                [new IRNamedType("Int"), new IRNamedType("Bool")])));
}

