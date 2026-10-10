namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Wspólne helpery dla testów bridge generation i end-to-end routing.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    private static async Task<(IRDocument Ir, string GoContent, string TsContent)> GenerateServerAndReportAsync(string source)
    {
        var ir = await TransformToIrAsync(source);
        var goContent = new GoCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;
        var tsContent = new TypeScriptCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;
        return (ir, goContent, tsContent);
    }

    private static IRQueueBridgeDescriptor AssertQueueBridgeDescriptor(
        IRDocument ir,
        string sourceTask,
        string? sourcePort,
        string targetTask,
        string? targetPort,
        string pollRoute,
        string ackRoute)
    {
        var descriptor = Assert.Single(ir.QueueBridges);
        Assert.Equal(sourceTask, descriptor.SourceTask);
        Assert.Equal(sourcePort, descriptor.SourcePort);
        Assert.Equal(targetTask, descriptor.TargetTask);
        Assert.Equal(targetPort, descriptor.TargetPort);
        Assert.Equal(pollRoute, descriptor.PollRoute);
        Assert.Equal(ackRoute, descriptor.AckRoute);
        return descriptor;
    }

    private static IRStreamBridgeDescriptor AssertStreamBridgeDescriptor(
        IRDocument ir,
        string sourceTask,
        string? sourcePort,
        string targetTask,
        string? targetPort,
        string streamRoute)
    {
        var descriptor = Assert.Single(ir.StreamBridges);
        Assert.Equal(sourceTask, descriptor.SourceTask);
        Assert.Equal(sourcePort, descriptor.SourcePort);
        Assert.Equal(targetTask, descriptor.TargetTask);
        Assert.Equal(targetPort, descriptor.TargetPort);
        Assert.Equal(streamRoute, descriptor.StreamRoute);
        return descriptor;
    }

    private static IRChanBridgeDescriptor AssertChanBridgeDescriptor(
        IRDocument ir,
        string sourceTask,
        string? sourcePort,
        string targetTask,
        string? targetPort,
        string channelRoute)
    {
        var descriptor = Assert.Single(ir.ChanBridges);
        Assert.Equal(sourceTask, descriptor.SourceTask);
        Assert.Equal(sourcePort, descriptor.SourcePort);
        Assert.Equal(targetTask, descriptor.TargetTask);
        Assert.Equal(targetPort, descriptor.TargetPort);
        Assert.Equal(channelRoute, descriptor.ChannelRoute);
        return descriptor;
    }

}