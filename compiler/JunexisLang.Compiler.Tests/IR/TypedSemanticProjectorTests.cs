using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

public sealed partial class IrTransformerTests
{
    [Fact]
    public async Task TypedSemanticProjector_PreservesTypesEffectsCommunicationLifecycleAndOrigins()
    {
        var structural = await TransformAsync("""
            task Flow(input: chan<Int>) @server -> queue<Text> {
                state count: Int = 0
                every 1s {
                    let value = recv input
                    send "ready"
                }
            }
            """);

        var semantic = TypedSemanticProjector.Project(structural);

        Assert.Equal(TypedSemanticDocument.CurrentVersion, semantic.SchemaVersion);
        var task = Assert.Single(semantic.Tasks);
        Assert.Equal("Flow", task.Name);
        Assert.Equal(TypedSemanticLifecycle.Periodic, task.Lifecycle);
        Assert.Equal("chan<Int>", Assert.Single(task.Inputs).Type.DisplayName);
        Assert.Equal("queue<Text>", task.Output!.DisplayName);
        Assert.Equal("Int", Assert.Single(task.State).Type.DisplayName);
        Assert.NotNull(task.Origin);

        Assert.Collection(task.Operations,
            state => AssertOperation(state, TypedSemanticOperationKind.InitializeState, TypedSemanticEffect.State),
            periodic => AssertOperation(periodic, TypedSemanticOperationKind.Periodic, TypedSemanticEffect.Time),
            receive =>
            {
                AssertOperation(receive, TypedSemanticOperationKind.Receive, TypedSemanticEffect.Communication);
                Assert.Equal("Int", receive.ValueType!.DisplayName);
                Assert.Equal(IRCommKind.Chan, receive.Communication!.Kind);
                Assert.Equal(CommunicationDeliverySemantics.Backpressured, receive.Communication.Contract.Delivery);
            },
            send =>
            {
                AssertOperation(send, TypedSemanticOperationKind.Send, TypedSemanticEffect.Communication);
                Assert.Equal("Text", send.ValueType!.DisplayName);
                Assert.Equal(IRCommKind.Queue, send.Communication!.Kind);
                Assert.Equal(CommunicationRetentionSemantics.UntilAck, send.Communication.Contract.Retention);
            });
    }

    [Fact]
    public async Task TypedSemanticProjector_SeparatesExternalEffectsFromPureEvaluation()
    {
        var structural = await TransformAsync("""
            extern task load_value() @server -> Int

            task Worker() @server {
                let value = load_value()
                let doubled = value * 2
                log(doubled)
            }
            """);

        var operations = Assert.Single(TypedSemanticProjector.Project(structural).Tasks).Operations;

        Assert.Collection(operations,
            external => AssertOperation(external, TypedSemanticOperationKind.ExternalCall, TypedSemanticEffect.External),
            pure => AssertOperation(pure, TypedSemanticOperationKind.BindValue, TypedSemanticEffect.Pure),
            log => AssertOperation(log, TypedSemanticOperationKind.Log, TypedSemanticEffect.External));
    }

    [Fact]
    public async Task TypedSemanticProjector_PreservesNestedExternBeforeCommunicationEffect()
    {
        var structural = await TransformAsync("""
            extern task decorate(value: Text) @server -> Text

            task Publish() @server -> stream<Text> {
                send decorate("hello")
            }
            """);

        var operations = Assert.Single(TypedSemanticProjector.Project(structural).Tasks).Operations;

        Assert.Collection(operations,
            external => AssertOperation(external, TypedSemanticOperationKind.ExternalCall, TypedSemanticEffect.External),
            send => AssertOperation(send, TypedSemanticOperationKind.Send, TypedSemanticEffect.Communication));
    }

    [Fact]
    public async Task TypedSemanticProjector_UsesOnlyBackendIndependentTypeNames()
    {
        var structural = await TransformAsync("""
            task Types(values: [Int], lookup: Map<Text,Float>) @report -> Int | Text {
                return 1
            }
            """);

        var task = Assert.Single(TypedSemanticProjector.Project(structural).Tasks);

        Assert.Equal("[Int]", task.Inputs[0].Type.DisplayName);
        Assert.Equal("Map<Text,Float>", task.Inputs[1].Type.DisplayName);
        Assert.Equal("Int|Text", task.Output!.DisplayName);
        var allNames = task.Inputs.Select(item => item.Type.DisplayName).Append(task.Output.DisplayName);
        Assert.DoesNotContain(allNames, name => name.Contains("std::", StringComparison.Ordinal)
            || name.Contains("golang", StringComparison.OrdinalIgnoreCase)
            || name.Contains("typescript", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("chan", IRCommKind.Chan, CommunicationDeliverySemantics.Backpressured)]
    [InlineData("queue", IRCommKind.Queue, CommunicationDeliverySemantics.AtLeastOnce)]
    [InlineData("stream", IRCommKind.Stream, CommunicationDeliverySemantics.LatestWins)]
    public async Task TypedSemanticProjector_BindsEveryCommunicationKindToCanonicalSemantics(
        string construct,
        IRCommKind expectedKind,
        CommunicationDeliverySemantics expectedDelivery)
    {
        var structural = await TransformAsync($$"""
            task Publish() @server -> {{construct}}<Int> {
                send 1
            }
            """);

        var operation = Assert.Single(Assert.Single(TypedSemanticProjector.Project(structural).Tasks).Operations);

        Assert.Equal(TypedSemanticOperationKind.Send, operation.Kind);
        Assert.Equal(expectedKind, operation.Communication!.Kind);
        Assert.Equal(expectedDelivery, operation.Communication.Contract.Delivery);
    }

    private static void AssertOperation(
        TypedSemanticOperation operation,
        TypedSemanticOperationKind kind,
        TypedSemanticEffect effect)
    {
        Assert.Equal(kind, operation.Kind);
        Assert.Equal(effect, operation.Effect);
        Assert.NotNull(operation.Origin);
        Assert.True(operation.Origin!.Line > 0);
        Assert.True(operation.Origin.Column > 0);
    }
}
