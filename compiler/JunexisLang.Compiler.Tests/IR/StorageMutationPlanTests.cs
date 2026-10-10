using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

public sealed class StorageMutationPlanTests
{
    [Fact]
    public void ValidatesPortableInsertWithoutProviderOrSqlDetails()
    {
        var model = HomeModel();
        var plan = new StorageMutationPlan(
            StorageMutationPlan.CurrentVersion,
            "AddObservation",
            model.Name,
            "Observation",
            StorageMutationKind.Insert,
            StorageMutationCardinality.Single,
            Parameters(),
            Assignments(),
            Predicate: null);

        var issues = StorageMutationPlanValidator.Validate(model, plan);

        Assert.Empty(issues);
        Assert.Equal("junexis.storage.mutation-plan/0.1", plan.SchemaVersion);
        Assert.DoesNotContain("sql", plan.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("postgres", plan.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", plan.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatesSingleUpdateBoundByCompleteCompositeKey()
    {
        var model = HomeModel(compositeKey: true);
        var plan = new StorageMutationPlan(
            StorageMutationPlan.CurrentVersion,
            "RenameSensor",
            model.Name,
            "Observation",
            StorageMutationKind.Update,
            StorageMutationCardinality.Single,
            [
                new StorageQueryParameter("id", StorageValueType.Uuid),
                new StorageQueryParameter("homeId", StorageValueType.Uuid),
                new StorageQueryParameter("sensor", StorageValueType.Text)
            ],
            [new StorageMutationAssignment("sensor", new StorageParameterExpression("sensor"))],
            new StorageLogicalPredicate(StorageLogicalOperator.And,
            [
                Equal("id", "id"),
                Equal("homeId", "homeId")
            ]));

        Assert.Empty(StorageMutationPlanValidator.Validate(model, plan));
    }

    [Fact]
    public void RequiresExplicitManyForPredicateNotBoundByCompleteKey()
    {
        var model = HomeModel();
        var single = new StorageMutationPlan(
            StorageMutationPlan.CurrentVersion,
            "DeleteSensorHistory",
            model.Name,
            "Observation",
            StorageMutationKind.Delete,
            StorageMutationCardinality.Single,
            [new StorageQueryParameter("sensor", StorageValueType.Text)],
            [],
            Equal("sensor", "sensor"));

        var issue = Assert.Single(StorageMutationPlanValidator.Validate(model, single));
        Assert.Equal("SMP041", issue.Code);

        var many = single with { Cardinality = StorageMutationCardinality.Many };
        Assert.Empty(StorageMutationPlanValidator.Validate(model, many));
    }

    [Fact]
    public void RejectsNullableKeyInputForSingleMutation()
    {
        var model = HomeModel();
        var plan = ValidDelete(model) with
        {
            Parameters = [new StorageQueryParameter("id", StorageValueType.Uuid, Nullable: true)]
        };

        Assert.Contains(
            StorageMutationPlanValidator.Validate(model, plan),
            issue => issue.Code == "SMP041");
    }

    [Fact]
    public void RejectsInsertMissingRequiredFieldsAndNullableValue()
    {
        var model = HomeModel();
        var plan = new StorageMutationPlan(
            StorageMutationPlan.CurrentVersion,
            "BrokenInsert",
            model.Name,
            "Observation",
            StorageMutationKind.Insert,
            StorageMutationCardinality.Single,
            [new StorageQueryParameter("id", StorageValueType.Uuid, Nullable: true)],
            [new StorageMutationAssignment("id", new StorageParameterExpression("id"))],
            Predicate: null);

        var issues = StorageMutationPlanValidator.Validate(model, plan);

        Assert.Contains(issues, issue => issue.Code == "SMP037");
        Assert.Equal(4, issues.Count(issue => issue.Code == "SMP038"));
    }

    [Fact]
    public void RejectsUnsafeUpdateShapeAndPrimaryKeyMutation()
    {
        var model = HomeModel();
        var plan = new StorageMutationPlan(
            StorageMutationPlan.CurrentVersion,
            "BrokenUpdate",
            model.Name,
            "Observation",
            StorageMutationKind.Update,
            StorageMutationCardinality.Single,
            [new StorageQueryParameter("id", StorageValueType.Uuid)],
            [new StorageMutationAssignment("id", new StorageFieldExpression("id"))],
            Predicate: null);

        var issues = StorageMutationPlanValidator.Validate(model, plan);

        Assert.Contains(issues, issue => issue.Code == "SMP036");
        Assert.Contains(issues, issue => issue.Code == "SMP052");
        Assert.Contains(issues, issue => issue.Code == "SMP040");
    }

    [Fact]
    public void RejectsUnknownEntityVersionsParametersAndPredicateTypes()
    {
        var model = HomeModel();
        var unknownEntity = ValidDelete(model) with
        {
            SchemaVersion = "junexis.storage.mutation-plan/9.9",
            Model = "Other",
            Entity = "Missing"
        };
        var entityIssues = StorageMutationPlanValidator.Validate(model, unknownEntity);
        Assert.Contains(entityIssues, issue => issue.Code == "SMP001");
        Assert.Contains(entityIssues, issue => issue.Code == "SMP002");
        Assert.Contains(entityIssues, issue => issue.Code == "SMP020");

        var invalidPredicate = ValidDelete(model) with
        {
            Parameters = [new StorageQueryParameter("id", StorageValueType.Text)],
            Predicate = Equal("id", "id")
        };
        Assert.Contains(
            StorageMutationPlanValidator.Validate(model, invalidPredicate),
            issue => issue.Code == "SMP050");
    }

    private static StorageMutationPlan ValidDelete(StorageDataModel model) => new(
        StorageMutationPlan.CurrentVersion,
        "DeleteObservation",
        model.Name,
        "Observation",
        StorageMutationKind.Delete,
        StorageMutationCardinality.Single,
        [new StorageQueryParameter("id", StorageValueType.Uuid)],
        [],
        Equal("id", "id"));

    private static StorageComparisonPredicate Equal(string field, string parameter) => new(
        new StorageFieldExpression(field),
        StorageComparisonOperator.Equal,
        new StorageParameterExpression(parameter));

    private static IReadOnlyList<StorageQueryParameter> Parameters() =>
    [
        new StorageQueryParameter("id", StorageValueType.Uuid),
        new StorageQueryParameter("homeId", StorageValueType.Uuid),
        new StorageQueryParameter("sensor", StorageValueType.Text),
        new StorageQueryParameter("value", StorageValueType.Float64),
        new StorageQueryParameter("recordedAt", StorageValueType.Timestamp)
    ];

    private static IReadOnlyList<StorageMutationAssignment> Assignments() =>
    [
        new StorageMutationAssignment("id", new StorageParameterExpression("id")),
        new StorageMutationAssignment("homeId", new StorageParameterExpression("homeId")),
        new StorageMutationAssignment("sensor", new StorageParameterExpression("sensor")),
        new StorageMutationAssignment("value", new StorageParameterExpression("value")),
        new StorageMutationAssignment("recordedAt", new StorageParameterExpression("recordedAt"))
    ];

    private static StorageDataModel HomeModel(bool compositeKey = false) => new(
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
            compositeKey ? ["id", "homeId"] : ["id"])]);
}
