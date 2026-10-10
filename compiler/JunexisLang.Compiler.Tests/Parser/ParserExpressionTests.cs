using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Tests.Parser;

/// <summary>Testy aliasów i normalizacji operatorów logicznych.</summary>
public sealed partial class ParserTests
{
    [Theory]
    [InlineData("or", "or")]
    [InlineData("||", "or")]
    [InlineData("and", "and")]
    [InlineData("&&", "and")]
    public async Task LogicalBinaryAliases_NormalizeInAst(string sourceOperator, string expectedOperator)
    {
        var ast = await Parse($$"""
            fn logic(a: Bool, b: Bool) -> Bool {
                return a {{sourceOperator}} b
            }
            """);

        var fn = Assert.IsType<FnDeclarationNode>(ast.Declarations.Single());
        var ret = Assert.IsType<ReturnStmtNode>(fn.Body.Single());
        var binary = Assert.IsType<BinaryExprNode>(ret.Value);

        Assert.Equal(expectedOperator, binary.Op);
    }

    [Theory]
    [InlineData("not")]
    [InlineData("!")]
    public async Task LogicalNotAliases_NormalizeInAst(string sourceOperator)
    {
        var ast = await Parse($$"""
            fn negate(value: Bool) -> Bool {
                return {{sourceOperator}} value
            }
            """);

        var fn = Assert.IsType<FnDeclarationNode>(ast.Declarations.Single());
        var ret = Assert.IsType<ReturnStmtNode>(fn.Body.Single());
        var unary = Assert.IsType<UnaryExprNode>(ret.Value);

        Assert.Equal("not", unary.Op);
    }

    [Fact]
    public async Task LogicalAliases_PreserveAndBeforeOrPrecedence()
    {
        var ast = await Parse("""
            fn logic(a: Bool, b: Bool, c: Bool) -> Bool {
                return a || b && !c
            }
            """);

        var fn = Assert.IsType<FnDeclarationNode>(ast.Declarations.Single());
        var ret = Assert.IsType<ReturnStmtNode>(fn.Body.Single());
        var or = Assert.IsType<BinaryExprNode>(ret.Value);
        var and = Assert.IsType<BinaryExprNode>(or.Right);
        var not = Assert.IsType<UnaryExprNode>(and.Right);

        Assert.Equal("or", or.Op);
        Assert.Equal("and", and.Op);
        Assert.Equal("not", not.Op);
    }
}
