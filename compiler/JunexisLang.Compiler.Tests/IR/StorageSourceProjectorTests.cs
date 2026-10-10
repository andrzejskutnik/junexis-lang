using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

public sealed class StorageSourceProjectorTests
{
    [Fact]
    public void ProjectsExplicitStorageSourceWithoutLosingModelOrQueryIntent()
    {
        var source = """
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

                query Recent(homeId: Uuid) from Observation {
                    filter homeId == params.homeId and value > 0.0
                    project sensor as sensorName, value, recordedAt
                    sort recordedAt desc
                    limit 20 offset 5
                }
            }
            """;

        var parse = new JunexisCompiler().ParseSource(source, "home-data.jnx");
        Assert.True(parse.Success, Diagnostics(parse.Diagnostics));
        var astStorage = Assert.IsType<StorageDeclarationNode>(Assert.Single(parse.Ast!.Declarations));
        Assert.Equal("HomeData", astStorage.Name);
        Assert.Equal(6, Assert.Single(astStorage.Entities).Fields.Count);
        Assert.Equal(4, Assert.Single(astStorage.Queries).Steps.Count);

        var structural = new AstToIrTransformer().Transform(parse.Ast);
        var irStorage = Assert.Single(structural.StorageDeclarations);
        Assert.Equal(["id"], Assert.Single(Assert.Single(irStorage.Entities).PrimaryKeys).Fields);
        Assert.Contains(structural.SourceMap, entry => entry.Path == "/StorageDeclarations[0]/Queries[0]/Steps[3]");

        var result = StorageSourceProjector.Project(structural);

        Assert.True(result.Success, Diagnostics(result.Diagnostics));
        var model = Assert.Single(result.Models);
        var entity = Assert.Single(model.Entities);
        var note = Assert.Single(entity.Fields, field => field.Name == "note");
        Assert.Equal(StorageValueType.Text, note.Type);
        Assert.True(note.Nullable);

        var plan = Assert.Single(result.Plans);
        Assert.Equal("Recent", plan.Name);
        Assert.Equal(StorageValueType.Uuid, Assert.Single(plan.Parameters).Type);
        var limit = Assert.IsType<StorageLimitPlan>(plan.Root);
        Assert.Equal(20, limit.Count);
        Assert.Equal(5, limit.Offset);
        var sort = Assert.IsType<StorageSortPlan>(limit.Input);
        Assert.Equal(StorageSortDirection.Descending, Assert.Single(sort.Keys).Direction);
        var project = Assert.IsType<StorageProjectPlan>(sort.Input);
        Assert.Equal("sensorName", project.Projections[0].Name);
        var filter = Assert.IsType<StorageFilterPlan>(project.Input);
        var logical = Assert.IsType<StorageLogicalPredicate>(filter.Predicate);
        Assert.Equal(StorageLogicalOperator.And, logical.Operator);
        Assert.IsType<StorageScanPlan>(filter.Input);
    }

    [Fact]
    public void ReportsUnsupportedStorageTypeAtItsSourceLocation()
    {
        var result = Project("""
            storage Counters {
                entity Counter {
                    id: Uuid
                    impossible: u64
                    key(id)
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SQP072");

        Assert.False(result.Success);
        Assert.Equal("storage-test.jnx", diagnostic.File);
        Assert.Equal(4, diagnostic.Line);
        Assert.True(diagnostic.Column > 1);
    }

    [Fact]
    public void RejectsFilterThatCannotBeRepresentedWithoutSemanticLoss()
    {
        var result = Project("""
            storage HomeData {
                entity Observation {
                    id: Uuid
                    value: Float
                    key(id)
                }
                query Broken() from Observation {
                    filter value + 1.0 > 2.0
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SQP077");

        Assert.False(result.Success);
        Assert.Equal(8, diagnostic.Line);
        Assert.Contains("polem, params.<nazwa> albo literałem", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunsM131ValidationAgainstTheProjectedStepOrder()
    {
        var result = Project("""
            storage HomeData {
                entity Observation {
                    id: Uuid
                    sensor: Text
                    recordedAt: Time
                    key(id)
                }
                query Broken() from Observation {
                    project sensor
                    sort recordedAt desc
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SQP060");

        Assert.False(result.Success);
        Assert.Equal("StorageQuery", diagnostic.Category);
        Assert.Equal(8, diagnostic.Line);
    }

    [Fact]
    public void RejectsMultiplePrimaryKeyDeclarationsAndDuplicateQueries()
    {
        var result = Project("""
            storage HomeData {
                entity Observation {
                    id: Uuid
                    homeId: Uuid
                    key(id)
                    key(id, homeId)
                }
                query Recent() from Observation { limit 1 }
                query Recent() from Observation { limit 2 }
            }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Code == "SQP071");
        Assert.Contains(result.Diagnostics, item => item.Code == "SQP081");
    }

    private static StorageSourceProjectionResult Project(string source)
    {
        var parse = new JunexisCompiler().ParseSource(source, "storage-test.jnx");
        Assert.True(parse.Success, Diagnostics(parse.Diagnostics));
        var structural = new AstToIrTransformer().Transform(parse.Ast!);
        return StorageSourceProjector.Project(structural);
    }

    private static string Diagnostics(IEnumerable<Diagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(item => $"{item.Code} {item.Line}:{item.Column} {item.Message}"));
}
