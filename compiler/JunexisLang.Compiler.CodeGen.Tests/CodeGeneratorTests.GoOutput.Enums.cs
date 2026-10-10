namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_ZeroPayloadEnumAccess_EmitsConstructorCall()
    {
        const string source = """
            enum Status {
                Ok,
                Error
            }

            task BuildStatus() @server -> Text {
                let status = Status::Ok
                return "ok"
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("type Status interface {", content);
        Assert.Contains("type StatusOk struct {", content);
        Assert.Contains("func NewStatusOk() Status {", content);
        Assert.Contains("status := NewStatusOk()", content);
    }

    [Fact]
    public async Task Go_MultiPayloadEnumCall_EmitsConstructorWithMultipleFields()
    {
        const string source = """
            enum Msg {
                Pair(Int, Text)
            }

            task BuildMessage() @server -> Text {
                let msg = Msg::Pair(1, "x")
                return "ok"
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("type Msg interface {", content);
        Assert.Contains("type MsgPair struct {", content);
        Assert.Contains("Value1 int64", content);
        Assert.Contains("Value2 string", content);
        Assert.Contains("func NewMsgPair(value1 int64, value2 string) Msg {", content);
        Assert.Contains("msg := NewMsgPair(1, \"x\")", content);
    }
}
