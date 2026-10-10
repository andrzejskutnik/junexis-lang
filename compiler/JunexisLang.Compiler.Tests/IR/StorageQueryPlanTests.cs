using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

public sealed class StorageQueryPlanTests
{
    [Fact]
    public void ValidatesPortableReadPlanWithoutProviderOrSqlDetails()
    {
        var model = HomeModel();
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "RecentObservations",
            model.Name,
            [new StorageQueryParameter("homeId", StorageValueType.Uuid)],
            new StorageLimitPlan(
                new StorageSortPlan(
                    new StorageProjectPlan(
                        new StorageFilterPlan(
                            new StorageScanPlan("Observation"),
                            new StorageComparisonPredicate(
                                new StorageFieldExpression("homeId"),
                                StorageComparisonOperator.Equal,
                                new StorageParameterExpression("homeId"))),
                        [
                            new StorageProjection("sensor", new StorageFieldExpression("sensor")),
                            new StorageProjection("value", new StorageFieldExpression("value")),
                            new StorageProjection("recordedAt", new StorageFieldExpression("recordedAt"))
                        ]),
                    [new StorageSortKey(new StorageFieldExpression("recordedAt"), StorageSortDirection.Descending)]),
                Count: 20));

        var issues = StorageQueryPlanValidator.Validate(model, plan);

        Assert.Empty(issues);
        Assert.Equal("junexis.storage.model/0.1", model.SchemaVersion);
        Assert.Equal("junexis.storage.query-plan/0.1", plan.SchemaVersion);
        Assert.DoesNotContain("sql", plan.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("postgres", plan.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", plan.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportsUnknownFieldsParametersAndIncompatibleComparisonTypes()
    {
        var model = HomeModel();
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "Broken",
            model.Name,
            [new StorageQueryParameter("minimum", StorageValueType.Text)],
            new StorageProjectPlan(
                new StorageFilterPlan(
                    new StorageScanPlan("Observation"),
                    new StorageLogicalPredicate(StorageLogicalOperator.And,
                    [
                        new StorageComparisonPredicate(
                            new StorageFieldExpression("value"),
                            StorageComparisonOperator.GreaterThan,
                            new StorageParameterExpression("minimum")),
                        new StorageComparisonPredicate(
                            new StorageFieldExpression("missing"),
                            StorageComparisonOperator.Equal,
                            new StorageParameterExpression("unknown"))
                    ])),
                [new StorageProjection("missing", new StorageFieldExpression("missing"))]));

        var issues = StorageQueryPlanValidator.Validate(model, plan);

        Assert.Contains(issues, issue => issue.Code == "SQP050");
        Assert.Equal(2, issues.Count(issue => issue.Code == "SQP060"));
        Assert.Contains(issues, issue => issue.Code == "SQP061");
    }

    [Fact]
    public void ReportsInvalidModelAndPlanShapeWithoutThrowing()
    {
        var model = new StorageDataModel(
            StorageDataModel.CurrentVersion,
            "BrokenModel",
            [new StorageModelEntity(
                "Item",
                [
                    new StorageModelField("id", StorageValueType.Uuid, Nullable: true),
                    new StorageModelField("id", StorageValueType.Text)
                ],
                ["id", "absent"])]);
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "BrokenShape",
            model.Name,
            [],
            new StorageLimitPlan(
                new StorageSortPlan(new StorageScanPlan("Item"), []),
                Count: 0,
                Offset: -1));

        var issues = StorageQueryPlanValidator.Validate(model, plan);

        Assert.Contains(issues, issue => issue.Code == "SQP011");
        Assert.Contains(issues, issue => issue.Code == "SQP013");
        Assert.Contains(issues, issue => issue.Code == "SQP014");
        Assert.Contains(issues, issue => issue.Code == "SQP041");
        Assert.Contains(issues, issue => issue.Code == "SQP042");
        Assert.Contains(issues, issue => issue.Code == "SQP043");
    }

    [Fact]
    public void RejectsUnknownModelEntityAndContractVersions()
    {
        var model = HomeModel() with { SchemaVersion = "junexis.storage.model/9.9" };
        var plan = new StorageQueryPlan(
            "junexis.storage.query-plan/9.9",
            "Unknown",
            "AnotherModel",
            [],
            new StorageScanPlan("Missing"));

        var issues = StorageQueryPlanValidator.Validate(model, plan);

        Assert.Contains(issues, issue => issue.Code == "SQP001");
        Assert.Contains(issues, issue => issue.Code == "SQP002");
        Assert.Contains(issues, issue => issue.Code == "SQP003");
        Assert.Contains(issues, issue => issue.Code == "SQP031");
    }

    [Fact]
    public void TracksTheResultShapeBetweenPlanNodes()
    {
        var model = HomeModel();
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "InvalidPipeline",
            model.Name,
            [],
            new StorageSortPlan(
                new StorageProjectPlan(
                    new StorageScanPlan("Observation"),
                    [new StorageProjection("sensorName", new StorageFieldExpression("sensor"))]),
                [new StorageSortKey(new StorageFieldExpression("recordedAt"), StorageSortDirection.Descending)]));

        var issue = Assert.Single(StorageQueryPlanValidator.Validate(model, plan));

        Assert.Equal("SQP060", issue.Code);
        Assert.Contains("/keys/0/expression", issue.Path, StringComparison.Ordinal);
    }

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
                new StorageModelField("recordedAt", StorageValueType.Timestamp)
            ],
            ["id"])]);
}
