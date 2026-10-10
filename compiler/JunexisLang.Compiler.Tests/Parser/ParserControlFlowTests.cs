using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Tests.Parser;

/// <summary>Testy skladni sterowania, konstrukcji opaque oraz bledow parsera.</summary>
public sealed partial class ParserTests
{
    [Fact]
    public async Task IfElse_BodySplitCorrectly()
    {
        const string src = """
            task Logic() {
                if x > 0 {
                    log("pos")
                } else {
                    log("neg")
                }
            }
            """;
        var ast = await Parse(src);
        var task = Assert.IsType<TaskDeclarationNode>(ast.Declarations.Single());
        var ifNode = Assert.IsType<IfStmtNode>(task.Body.Single());

        Assert.Single(ifNode.Then);
        var elseBlock = Assert.IsType<ElseBlockNode>(ifNode.Else);
        Assert.Single(elseBlock.Body);
    }

    [Fact]
    public async Task IfElseIf_ChainParsesOk()
    {
        const string src = """
            task T() {
                if a { log("a") }
                else if b { log("b") }
                else { log("c") }
            }
            """;
        var ast = await Parse(src);
        var task = Assert.IsType<TaskDeclarationNode>(ast.Declarations.Single());
        var ifNode = Assert.IsType<IfStmtNode>(task.Body.Single());

        var elseIf = Assert.IsType<IfStmtNode>(ifNode.Else);
        Assert.IsType<ElseBlockNode>(elseIf.Else);
    }

    [Fact]
    public async Task Match_WildcardAndBinding_ParsesOk()
    {
        const string src = """
            task M(v: Int) {
                match v {
                    0 -> { log("zero") },
                    n -> { log("other") },
                }
            }
            """;
        var ast = await Parse(src);
        var task = Assert.IsType<TaskDeclarationNode>(ast.Declarations.Single());
        var match = Assert.IsType<MatchStmtNode>(task.Body.Single());
        Assert.Equal(2, match.Arms.Count);
        Assert.IsType<LiteralPatternNode>(match.Arms[0].Pattern);
        Assert.IsType<BindingPatternNode>(match.Arms[1].Pattern);
    }

    [Fact]
    public async Task Narrative_OpaqueParsesOk()
    {
        const string src = """
            narrative "E2E" {
                intent "ensure delivery"
                trace full
            }
            """;
        var ast = await Parse(src);
        var node = Assert.IsType<NarrativeDeclarationNode>(ast.Declarations.Single());
        Assert.Equal("E2E", node.Label);
        Assert.Contains("intent", node.Body);
    }

    [Fact]
    public async Task SyntaxError_ReturnsFailure()
    {
        var result = await Check("task {{{");
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }
}