namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Cpp_QueueAckNackStatements_LowerThroughHiddenMessageIdAsync()
    {
        var document = await TransformToIrAsync("""
            task Handle(input: queue<Text>) @embedded {
                let item = recv input
                nack item reason "retry"
                ack item
            }
            """);

        var content = new CppCodeGenerator().Generate(document, Opts)
            .Single(file => file.FilePath == "embedded/Handle.cpp").Content;

        Assert.Contains("auto __item_delivery = input.delivery();", content);
        Assert.Contains("if (!__item_delivery.ok) return;", content);
        Assert.Contains("auto item = __item_delivery.value;", content);
        Assert.Contains("input.nack(__item_delivery.message_id, \"retry\");", content);
        Assert.Contains("input.ack(__item_delivery.message_id);", content);
    }
}
