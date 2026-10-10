namespace JunexisLang.Compiler.Tests.TestHelpers;

using JunexisLang.Compiler.IR;

internal static class BridgeDescriptorAssertions
{
    internal static IRQueueBridgeDescriptor AssertQueueBridge(
        IRDocument ir,
        string pipelineName,
        string sourceTask,
        string? sourcePort,
        string targetTask,
        string? targetPort,
        string pollRoute,
        string ackRoute)
    {
        var descriptor = Assert.Single(ir.QueueBridges);
        Assert.Equal(pipelineName, descriptor.PipelineName);
        Assert.Equal(sourceTask, descriptor.SourceTask);
        Assert.Equal(sourcePort, descriptor.SourcePort);
        Assert.Equal(targetTask, descriptor.TargetTask);
        Assert.Equal(targetPort, descriptor.TargetPort);
        Assert.Equal(pollRoute, descriptor.PollRoute);
        Assert.Equal(ackRoute, descriptor.AckRoute);
        return descriptor;
    }

    internal static IRStreamBridgeDescriptor AssertStreamBridge(
        IRDocument ir,
        string pipelineName,
        string sourceTask,
        string? sourcePort,
        string targetTask,
        string? targetPort,
        string streamRoute)
    {
        var descriptor = Assert.Single(ir.StreamBridges);
        Assert.Equal(pipelineName, descriptor.PipelineName);
        Assert.Equal(sourceTask, descriptor.SourceTask);
        Assert.Equal(sourcePort, descriptor.SourcePort);
        Assert.Equal(targetTask, descriptor.TargetTask);
        Assert.Equal(targetPort, descriptor.TargetPort);
        Assert.Equal(streamRoute, descriptor.StreamRoute);
        return descriptor;
    }

    internal static IRChanBridgeDescriptor AssertChanBridge(
        IRDocument ir,
        string pipelineName,
        string sourceTask,
        string? sourcePort,
        string targetTask,
        string? targetPort,
        string channelRoute)
    {
        var descriptor = Assert.Single(ir.ChanBridges);
        Assert.Equal(pipelineName, descriptor.PipelineName);
        Assert.Equal(sourceTask, descriptor.SourceTask);
        Assert.Equal(sourcePort, descriptor.SourcePort);
        Assert.Equal(targetTask, descriptor.TargetTask);
        Assert.Equal(targetPort, descriptor.TargetPort);
        Assert.Equal(channelRoute, descriptor.ChannelRoute);
        return descriptor;
    }
}