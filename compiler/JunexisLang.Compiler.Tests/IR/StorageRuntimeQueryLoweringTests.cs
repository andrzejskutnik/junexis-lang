using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

public sealed class StorageRuntimeQueryLoweringTests
{
    [Fact]
    public void LowersM132SourceToProviderNeutralRuntimeInvocation()
    {
        var (model, plan) = ProjectHomeSource();

        var result = StorageRuntimeQueryLowerer.Lower(
            model,
            plan,
            [new StorageQueryArgument("homeId", StorageValueType.Uuid, "AA62E761-22E6-4DCE-9227-050C87E9131B")]);

        Assert.True(result.Success, Issues(result.Issues));
        var invocation = Assert.IsType<StorageRuntimeQueryInvocation>(result.Invocation);
        Assert.Equal("junexis.storage.runtime/0.1", invocation.RuntimeAbi);
        Assert.Equal("invoke", invocation.Action);
        Assert.Equal("query.plan", invocation.Operation);
        Assert.Equal("read", invocation.Effect);
        Assert.Equal(["query-plan"], invocation.RequiredCapabilities);
        Assert.Equal("junexis.storage.query-invocation/0.1", invocation.Input.SchemaVersion);
        Assert.Equal(["filter", "project", "sort", "limit"], invocation.Input.Query.Steps.Select(step => step.Kind));
        Assert.Equal("aa62e761-22e6-4dce-9227-050c87e9131b", Assert.Single(invocation.Input.Arguments).Value);
        Assert.Equal(["sensorName", "value", "recordedAt"], invocation.Input.Query.Result.Select(column => column.Name));

        var json = StorageRuntimeQueryInvocationJson.Serialize(invocation);
        Assert.Contains("\"kind\":\"comparison\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"parameter\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sql", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("postgres", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProducesDeterministicJsonAndArgumentOrderFromTheDeclaration()
    {
        var model = HomeModel();
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "Window",
            model.Name,
            [
                new StorageQueryParameter("homeId", StorageValueType.Uuid),
                new StorageQueryParameter("minimum", StorageValueType.Float64)
            ],
            new StorageScanPlan("Observation"));
        var first = Lower(
            model,
            plan,
            [
                new StorageQueryArgument("minimum", StorageValueType.Float64, "1.500"),
                new StorageQueryArgument("homeId", StorageValueType.Uuid, "aa62e76122e64dce9227050c87e9131b")
            ]);
        var second = Lower(
            model,
            plan,
            [
                new StorageQueryArgument("homeId", StorageValueType.Uuid, "AA62E761-22E6-4DCE-9227-050C87E9131B"),
                new StorageQueryArgument("minimum", StorageValueType.Float64, "1.5")
            ]);

        Assert.Equal(["homeId", "minimum"], first.Input.Arguments.Select(argument => argument.Name));
        Assert.Equal(StorageRuntimeQueryInvocationJson.Serialize(first), StorageRuntimeQueryInvocationJson.Serialize(second));
        Assert.Equal(
            StorageRuntimeQueryInvocationJson.FingerprintSha256(first),
            StorageRuntimeQueryInvocationJson.FingerprintSha256(second));
        Assert.Equal(64, StorageRuntimeQueryInvocationJson.FingerprintSha256(first).Length);
    }

    [Fact]
    public void RejectsMissingDuplicateAndUnknownArgumentsTogether()
    {
        var model = HomeModel();
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "Window",
            model.Name,
            [
                new StorageQueryParameter("homeId", StorageValueType.Uuid),
                new StorageQueryParameter("minimum", StorageValueType.Float64)
            ],
            new StorageScanPlan("Observation"));

        var result = StorageRuntimeQueryLowerer.Lower(
            model,
            plan,
            [
                new StorageQueryArgument("homeId", StorageValueType.Uuid, "aa62e761-22e6-4dce-9227-050c87e9131b"),
                new StorageQueryArgument("homeId", StorageValueType.Uuid, "aa62e761-22e6-4dce-9227-050c87e9131b"),
                new StorageQueryArgument("unexpected", StorageValueType.Text, "value")
            ]);

        Assert.False(result.Success);
        Assert.Null(result.Invocation);
        Assert.Contains(result.Issues, issue => issue.Code == "SQR010");
        Assert.Contains(result.Issues, issue => issue.Code == "SQR011");
        Assert.Contains(result.Issues, issue => issue.Code == "SQR012");
    }

    [Fact]
    public void CanonicalizesEveryPortableRuntimeValueType()
    {
        var model = HomeModel();
        var parameters = new[]
        {
            new StorageQueryParameter("integer", StorageValueType.Int64),
            new StorageQueryParameter("number", StorageValueType.Float64),
            new StorageQueryParameter("flag", StorageValueType.Bool),
            new StorageQueryParameter("text", StorageValueType.Text),
            new StorageQueryParameter("bytes", StorageValueType.Bytes),
            new StorageQueryParameter("amount", StorageValueType.Decimal),
            new StorageQueryParameter("at", StorageValueType.Timestamp),
            new StorageQueryParameter("id", StorageValueType.Uuid),
            new StorageQueryParameter("document", StorageValueType.Json),
            new StorageQueryParameter("optional", StorageValueType.Text, Nullable: true)
        };
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "Typed",
            model.Name,
            parameters,
            new StorageScanPlan("Observation"));

        var invocation = Lower(model, plan,
        [
            new StorageQueryArgument("integer", StorageValueType.Int64, "+0042"),
            new StorageQueryArgument("number", StorageValueType.Float64, "1.500"),
            new StorageQueryArgument("flag", StorageValueType.Bool, "TRUE"),
            new StorageQueryArgument("text", StorageValueType.Text, "Junexis"),
            new StorageQueryArgument("bytes", StorageValueType.Bytes, "AQI="),
            new StorageQueryArgument("amount", StorageValueType.Decimal, "001.2300"),
            new StorageQueryArgument("at", StorageValueType.Timestamp, "2026-08-22T12:30:00+02:00"),
            new StorageQueryArgument("id", StorageValueType.Uuid, "AA62E76122E64DCE9227050C87E9131B"),
            new StorageQueryArgument("document", StorageValueType.Json, "{ \"ok\" : true }"),
            new StorageQueryArgument("optional", StorageValueType.Text, null)
        ]);
        var values = invocation.Input.Arguments.ToDictionary(argument => argument.Name, argument => argument.Value);

        Assert.Equal("42", values["integer"]);
        Assert.Equal("1.5", values["number"]);
        Assert.Equal("true", values["flag"]);
        Assert.Equal("Junexis", values["text"]);
        Assert.Equal("AQI=", values["bytes"]);
        Assert.Equal("1.23", values["amount"]);
        Assert.Equal("2026-08-22T12:30:00.0000000+02:00", values["at"]);
        Assert.Equal("aa62e761-22e6-4dce-9227-050c87e9131b", values["id"]);
        Assert.Equal("{\"ok\":true}", values["document"]);
        Assert.Null(values["optional"]);
        Assert.Contains(
            "\"name\":\"optional\",\"type\":\"text\",\"value\":null",
            StorageRuntimeQueryInvocationJson.Serialize(invocation),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTypeNullabilityAndCanonicalValueMismatches()
    {
        var model = HomeModel();
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "Typed",
            model.Name,
            [new StorageQueryParameter("homeId", StorageValueType.Uuid)],
            new StorageScanPlan("Observation"));

        var wrongType = StorageRuntimeQueryLowerer.Lower(
            model, plan, [new StorageQueryArgument("homeId", StorageValueType.Text, "value")]);
        var unexpectedNull = StorageRuntimeQueryLowerer.Lower(
            model, plan, [new StorageQueryArgument("homeId", StorageValueType.Uuid, null)]);
        var invalidValue = StorageRuntimeQueryLowerer.Lower(
            model, plan, [new StorageQueryArgument("homeId", StorageValueType.Uuid, "not-a-uuid")]);

        Assert.Contains(wrongType.Issues, issue => issue.Code == "SQR013");
        Assert.Contains(unexpectedNull.Issues, issue => issue.Code == "SQR014");
        Assert.Contains(invalidValue.Issues, issue => issue.Code == "SQR015");
    }

    [Fact]
    public void RefusesToLowerAPlanThatFailedM131Validation()
    {
        var model = HomeModel();
        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            "Broken",
            model.Name,
            [],
            new StorageProjectPlan(
                new StorageScanPlan("Observation"),
                [new StorageProjection("missing", new StorageFieldExpression("missing"))]));

        var result = StorageRuntimeQueryLowerer.Lower(model, plan, []);

        Assert.False(result.Success);
        Assert.Null(result.Invocation);
        Assert.Contains(result.Issues, issue => issue.Code == "SQP060");
    }

    private static StorageRuntimeQueryInvocation Lower(
        StorageDataModel model,
        StorageQueryPlan plan,
        IReadOnlyList<StorageQueryArgument> arguments)
    {
        var result = StorageRuntimeQueryLowerer.Lower(model, plan, arguments);
        Assert.True(result.Success, Issues(result.Issues));
        return Assert.IsType<StorageRuntimeQueryInvocation>(result.Invocation);
    }

    private static (StorageDataModel Model, StorageQueryPlan Plan) ProjectHomeSource()
    {
        const string source = """
            storage HomeData {
                entity Observation {
                    id: Uuid
                    homeId: Uuid
                    sensor: Text
                    value: Float
                    recordedAt: Time
                    key(id)
                }
                query Recent(homeId: Uuid) from Observation {
                    filter homeId == params.homeId and value > 0.0
                    project sensor as sensorName, value, recordedAt
                    sort recordedAt desc
                    limit 20
                }
            }
            """;
        var parse = new JunexisCompiler().ParseSource(source, "runtime-query.jnx");
        Assert.True(parse.Success, Issues(parse.Diagnostics.Select(diagnostic => new StorageRuntimeQueryLoweringIssue(
            diagnostic.Code, $"{diagnostic.Line}:{diagnostic.Column}", diagnostic.Message))));
        var structural = new AstToIrTransformer().Transform(parse.Ast!);
        var projection = StorageSourceProjector.Project(structural);
        Assert.True(projection.Success, string.Join(Environment.NewLine, projection.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return (Assert.Single(projection.Models), Assert.Single(projection.Plans));
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

    private static string Issues(IEnumerable<StorageRuntimeQueryLoweringIssue> issues)
        => string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}"));
}
