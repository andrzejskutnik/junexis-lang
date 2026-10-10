namespace JunexisLang.Compiler.CodeGen;

using JunexisLang.Compiler.IR;

internal sealed record StreamRuntimeBinding(
    string HandleType,
    string? FactoryExpression,
    StreamRuntimeContract Contract)
{
    public string PublishMethodName => Contract.PublishMethodName;

    public string SubscribeMethodName => Contract.SubscribeMethodName;

    public StreamRuntimeCapabilities Capabilities => Contract.Capabilities;

    public bool Supports(StreamRuntimeCapabilities capability)
        => Contract.Supports(capability);

    public string RenderPublishInvocation(string handleName, string valueExpression)
        => Contract.RenderPublishInvocation(handleName, valueExpression);

    public string RenderSubscribeInvocation(string handleName)
        => Contract.RenderSubscribeInvocation(handleName);
}

internal static class StreamRuntimeContracts
{
    public static StreamRuntimeBinding ForCpp(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToCpp(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Stream;
        return new StreamRuntimeBinding(
            HandleType: $"junexis::Stream<{elementTypeName}>",
            FactoryExpression: null,
            Contract: new StreamRuntimeContract(
                PublishMethodName: "publish",
                SubscribeMethodName: "subscribe",
                Capabilities: StreamRuntimeCapabilities.Publish | StreamRuntimeCapabilities.Consume | StreamRuntimeCapabilities.Subscribe,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }

    public static StreamRuntimeBinding ForGo(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToGo(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Stream;
        return new StreamRuntimeBinding(
            HandleType: $"*Stream[{elementTypeName}]",
            FactoryExpression: $"NewStream[{elementTypeName}]()",
            Contract: new StreamRuntimeContract(
                PublishMethodName: "Publish",
                SubscribeMethodName: "Subscribe",
                Capabilities: StreamRuntimeCapabilities.Publish | StreamRuntimeCapabilities.Consume | StreamRuntimeCapabilities.Subscribe,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }

    public static StreamRuntimeBinding ForTypeScript(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToTypeScript(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Stream;
        return new StreamRuntimeBinding(
            HandleType: $"Stream<{elementTypeName}>",
            FactoryExpression: null,
            Contract: new StreamRuntimeContract(
                PublishMethodName: "publish",
                SubscribeMethodName: "subscribe",
                Capabilities: StreamRuntimeCapabilities.Publish | StreamRuntimeCapabilities.Consume | StreamRuntimeCapabilities.Subscribe,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }
}
