using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

public sealed class StorageRuntimeMutationLoweringTests
{
    [Fact]
    public void LowersSourceMutationToProviderNeutralWriteInvocation()
    {
        var (model, plan) = ProjectInsertSource();
        var invocation = Lower(model, plan,
        [
            new StorageMutationArgument("id", StorageValueType.Uuid, "aa62e76122e64dce9227050c87e9131b"),
            new StorageMutationArgument("homeId", StorageValueType.Uuid, "bb62e76122e64dce9227050c87e9131b"),
            new StorageMutationArgument("sensor", StorageValueType.Text, "kitchen"),
            new StorageMutationArgument("value", StorageValueType.Float64, "21.500"),
            new StorageMutationArgument("recordedAt", StorageValueType.Timestamp, "2026-08-23T10:00:00+02:00")
        ]);

        Assert.Equal("junexis.storage.runtime/0.1", invocation.RuntimeAbi);
        Assert.Equal("invoke", invocation.Action);
        Assert.Equal("mutation.plan", invocation.Operation);
        Assert.Equal("write", invocation.Effect);
        Assert.Equal(["mutation-plan"], invocation.RequiredCapabilities);
        Assert.Equal("junexis.storage.mutation-invocation/0.1", invocation.Input.SchemaVersion);
        Assert.Equal("insert", invocation.Input.Mutation.Kind);
        Assert.Equal("single", invocation.Input.Mutation.Cardinality);
        Assert.Equal(5, invocation.Input.Mutation.Assignments.Count);
        Assert.Null(invocation.Input.Mutation.Predicate);

        var json = StorageRuntimeMutationInvocationJson.Serialize(invocation);
        Assert.Contains("\"operation\":\"mutation.plan\"", json, StringComparison.Ordinal);
        Assert.Contains("\"effect\":\"write\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sql", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("postgres", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProducesDeterministicJsonAndCanonicalArgumentOrder()
    {
        var (model, plan) = ProjectInsertSource();
        var first = Lower(model, plan,
        [
            new StorageMutationArgument("value", StorageValueType.Float64, "21.500"),
            new StorageMutationArgument("recordedAt", StorageValueType.Timestamp, "2026-08-23T10:00:00+02:00"),
            new StorageMutationArgument("sensor", StorageValueType.Text, "kitchen"),
            new StorageMutationArgument("homeId", StorageValueType.Uuid, "BB62E761-22E6-4DCE-9227-050C87E9131B"),
            new StorageMutationArgument("id", StorageValueType.Uuid, "AA62E76122E64DCE9227050C87E9131B")
        ]);
        var second = Lower(model, plan,
        [
            new StorageMutationArgument("id", StorageValueType.Uuid, "aa62e761-22e6-4dce-9227-050c87e9131b"),
            new StorageMutationArgument("homeId", StorageValueType.Uuid, "bb62e76122e64dce9227050c87e9131b"),
            new StorageMutationArgument("sensor", StorageValueType.Text, "kitchen"),
            new StorageMutationArgument("value", StorageValueType.Float64, "21.5"),
            new StorageMutationArgument("recordedAt", StorageValueType.Timestamp, "2026-08-23T10:00:00.0000000+02:00")
        ]);

        Assert.Equal(["id", "homeId", "sensor", "value", "recordedAt"],
            first.Input.Arguments.Select(argument => argument.Name));
        Assert.Equal(
            StorageRuntimeMutationInvocationJson.Serialize(first),
            StorageRuntimeMutationInvocationJson.Serialize(second));
        Assert.Equal(
            StorageRuntimeMutationInvocationJson.FingerprintSha256(first),
            StorageRuntimeMutationInvocationJson.FingerprintSha256(second));
        Assert.Equal(64, StorageRuntimeMutationInvocationJson.FingerprintSha256(first).Length);
    }

    [Fact]
    public void RejectsMissingDuplicateAndUnknownArgumentsTogether()
    {
        var model = HomeModel();
        var plan = UpdatePlan(model);

        var result = StorageRuntimeMutationLowerer.Lower(model, plan,
        [
            new StorageMutationArgument("id", StorageValueType.Uuid, "aa62e761-22e6-4dce-9227-050c87e9131b"),
            new StorageMutationArgument("id", StorageValueType.Uuid, "aa62e761-22e6-4dce-9227-050c87e9131b"),
            new StorageMutationArgument("unexpected", StorageValueType.Text, "value")
        ]);

        Assert.False(result.Success);
        Assert.Null(result.Invocation);
        Assert.Contains(result.Issues, issue => issue.Code == "SMR010");
        Assert.Contains(result.Issues, issue => issue.Code == "SMR011");
        Assert.Contains(result.Issues, issue => issue.Code == "SMR012");
    }

    [Fact]
    public void RejectsTypeNullabilityAndCanonicalValueMismatches()
    {
        var model = HomeModel();
        var plan = UpdatePlan(model);
        var id = new StorageMutationArgument("id", StorageValueType.Uuid, "aa62e761-22e6-4dce-9227-050c87e9131b");

        var wrongType = StorageRuntimeMutationLowerer.Lower(model, plan,
        [id, new StorageMutationArgument("sensor", StorageValueType.Uuid, "aa62e761-22e6-4dce-9227-050c87e9131b")]);
        var unexpectedNull = StorageRuntimeMutationLowerer.Lower(model, plan,
        [id, new StorageMutationArgument("sensor", StorageValueType.Text, null)]);
        var invalidValue = StorageRuntimeMutationLowerer.Lower(model, plan,
        [new StorageMutationArgument("id", StorageValueType.Uuid, "not-a-uuid"),
         new StorageMutationArgument("sensor", StorageValueType.Text, "kitchen")]);

        Assert.Contains(wrongType.Issues, issue => issue.Code == "SMR013");
        Assert.Contains(unexpectedNull.Issues, issue => issue.Code == "SMR014");
        Assert.Contains(invalidValue.Issues, issue => issue.Code == "SMR015");
    }

    [Fact]
    public void PreservesExplicitNullArgumentInWireJson()
    {
        var model = HomeModel();
        var plan = new StorageMutationPlan(
            StorageMutationPlan.CurrentVersion,
            "ClearNote",
            model.Name,
            "Observation",
            StorageMutationKind.Update,
            StorageMutationCardinality.Single,
            [
                new StorageQueryParameter("id", StorageValueType.Uuid),
                new StorageQueryParameter("note", StorageValueType.Text, Nullable: true)
            ],
            [new StorageMutationAssignment("note", new StorageParameterExpression("note"))],
            Equal("id", "id"));
        var invocation = Lower(model, plan,
        [
            new StorageMutationArgument("id", StorageValueType.Uuid, "aa62e761-22e6-4dce-9227-050c87e9131b"),
            new StorageMutationArgument("note", StorageValueType.Text, null)
        ]);

        Assert.Contains(
            "\"name\":\"note\",\"type\":\"text\",\"value\":null",
            StorageRuntimeMutationInvocationJson.Serialize(invocation),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToLowerPlanThatFailedMutationValidation()
    {
        var model = HomeModel();
        var plan = UpdatePlan(model) with { Predicate = Equal("sensor", "sensor") };

        var result = StorageRuntimeMutationLowerer.Lower(model, plan, []);

        Assert.False(result.Success);
        Assert.Null(result.Invocation);
        Assert.Contains(result.Issues, issue => issue.Code == "SMP041");
    }

    [Fact]
    public void ValidatesSuccessfulResultAndKeepsUnknownOutcomeInErrorChannel()
    {
        var model = HomeModel();
        var update = UpdatePlan(model);
        var applied = new StorageRuntimeMutationResult(
            StorageRuntimeMutationResult.CurrentVersion,
            StorageRuntimeMutationResult.Applied,
            1);
        var missing = new StorageRuntimeMutationResult(
            StorageRuntimeMutationResult.CurrentVersion,
            StorageRuntimeMutationResult.NotApplied,
            0);

        Assert.Empty(StorageRuntimeMutationLowerer.ValidateResult(update, applied));
        Assert.Empty(StorageRuntimeMutationLowerer.ValidateResult(update, missing));

        var tooMany = applied with { AffectedRows = 2 };
        Assert.Contains(
            StorageRuntimeMutationLowerer.ValidateResult(update, tooMany),
            issue => issue.Code == "SMR035");

        var falseSuccess = applied with { Outcome = "unknown" };
        Assert.Contains(
            StorageRuntimeMutationLowerer.ValidateResult(update, falseSuccess),
            issue => issue.Code == "SMR034");
        Assert.Equal("unknown-outcome", StorageRuntimeMutationInvocation.UnknownOutcomeErrorClass);

        var insert = ProjectInsertSource().Plan;
        Assert.Contains(
            StorageRuntimeMutationLowerer.ValidateResult(insert, missing),
            issue => issue.Code == "SMR036");
    }

    private static StorageRuntimeMutationInvocation Lower(
        StorageDataModel model,
        StorageMutationPlan plan,
        IReadOnlyList<StorageMutationArgument> arguments)
    {
        var result = StorageRuntimeMutationLowerer.Lower(model, plan, arguments);
        Assert.True(result.Success, Issues(result.Issues));
        return Assert.IsType<StorageRuntimeMutationInvocation>(result.Invocation);
    }

    private static (StorageDataModel Model, StorageMutationPlan Plan) ProjectInsertSource()
    {
        const string source = """
            storage HomeData {
                entity Observation {
                    id: Uuid
                    homeId: Uuid
                    sensor: Text
                    value: Float
                    recordedAt: Time
                    note: Text | Null
                    key(id)
                }
                mutation AddObservation(
                    id: Uuid,
                    homeId: Uuid,
                    sensor: Text,
                    value: Float,
                    recordedAt: Time
                ) insert single Observation {
                    set id = params.id
                    set homeId = params.homeId
                    set sensor = params.sensor
                    set value = params.value
                    set recordedAt = params.recordedAt
                }
            }
            """;
        var parse = new JunexisCompiler().ParseSource(source, "runtime-mutation.jnx");
        Assert.True(parse.Success, string.Join(Environment.NewLine, parse.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var projection = StorageSourceProjector.Project(new AstToIrTransformer().Transform(parse.Ast!));
        Assert.True(projection.Success, string.Join(Environment.NewLine, projection.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return (Assert.Single(projection.Models), Assert.Single(projection.MutationPlans));
    }

    private static StorageMutationPlan UpdatePlan(StorageDataModel model) => new(
        StorageMutationPlan.CurrentVersion,
        "RenameSensor",
        model.Name,
        "Observation",
        StorageMutationKind.Update,
        StorageMutationCardinality.Single,
        [
            new StorageQueryParameter("id", StorageValueType.Uuid),
            new StorageQueryParameter("sensor", StorageValueType.Text)
        ],
        [new StorageMutationAssignment("sensor", new StorageParameterExpression("sensor"))],
        Equal("id", "id"));

    private static StorageComparisonPredicate Equal(string field, string parameter) => new(
        new StorageFieldExpression(field),
        StorageComparisonOperator.Equal,
        new StorageParameterExpression(parameter));

    private static StorageDataModel HomeModel() => new(
        StorageDataModel.CurrentVersion,
        "HomeData",
        [new StorageModelEntity(
            "Observation",
            [
                new StorageModelField("id", StorageValueType.Uuid),
                new StorageModelField("homeId", StorageValueType.Uuid),
                new StorageModelField("sensor", StorageValueType.Text),
                new StorageModelField("value", StorageValueType.Float64),
                new StorageModelField("recordedAt", StorageValueType.Timestamp),
                new StorageModelField("note", StorageValueType.Text, Nullable: true)
            ],
            ["id"])]);

    private static string Issues(IEnumerable<StorageRuntimeMutationLoweringIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}"));
}
