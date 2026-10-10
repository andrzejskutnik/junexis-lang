namespace JunexisLang.Compiler.CodeGen;

using JunexisLang.Compiler.IR;

internal sealed record ChanRuntimeBinding(
    string HandleType,
    string? FactoryExpression,
    bool RequiresBufferedOutputAdapter,
    ChanRuntimeContract Contract)
{
    public bool Supports(ChanRuntimeCapabilities capability)
        => Contract.Supports(capability);

    public string RenderSendInvocation(string handleExpression, string valueExpression)
        => Contract.RenderSendInvocation(handleExpression, valueExpression);
}

internal static class ChanRuntimeContracts
{
    public static ChanRuntimeBinding ForCpp(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToCpp(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Chan;
        return new ChanRuntimeBinding(
            HandleType: $"junexis::Channel<{elementTypeName}>",
            FactoryExpression: null,
            RequiresBufferedOutputAdapter: false,
            Contract: new ChanRuntimeContract(
                SendMethodName: "send",
                SendStyle: ChanSendStyle.MethodCall,
                Capabilities: ChanRuntimeCapabilities.Send | ChanRuntimeCapabilities.Consume,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }

    public static ChanRuntimeBinding ForGo(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToGo(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Chan;
        return new ChanRuntimeBinding(
            HandleType: $"chan {elementTypeName}",
            FactoryExpression: $"make(chan {elementTypeName})",
            RequiresBufferedOutputAdapter: false,
            Contract: new ChanRuntimeContract(
                SendMethodName: null,
                SendStyle: ChanSendStyle.OperatorArrow,
                Capabilities: ChanRuntimeCapabilities.Send | ChanRuntimeCapabilities.Consume,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }

    public static ChanRuntimeBinding ForTypeScript(IRType elementType)
    {
        var elementTypeName = TypeMappings.ToTypeScript(elementType);
        var semantics = CommunicationRuntimeContractCatalog.Chan;
        return new ChanRuntimeBinding(
            HandleType: $"Channel<{elementTypeName}>",
            FactoryExpression: $"new Channel<{elementTypeName}>(1)",
            RequiresBufferedOutputAdapter: false,
            Contract: new ChanRuntimeContract(
                SendMethodName: "send",
                SendStyle: ChanSendStyle.MethodCall,
                Capabilities: ChanRuntimeCapabilities.Send | ChanRuntimeCapabilities.Consume,
                Delivery: semantics.Delivery,
                Backpressure: semantics.Backpressure,
                Retention: semantics.Retention,
                Ordering: semantics.Ordering));
    }
}
