using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Topology;

namespace JunexisLang.Compiler.Tests.IR;

public sealed class StorageMutationSourceProjectorTests
{
    [Fact]
    public void ProjectsInsertUpdateAndDeleteWithoutLosingAuthorIntent()
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

                mutation RenameSensor(id: Uuid, sensor: Text)
                    update single Observation {
                    set sensor = params.sensor
                    where id == params.id
                }

                mutation DeleteSensorHistory(sensor: Text)
                    delete many Observation {
                    where sensor == params.sensor
                }
            }
            """;

        var parse = new JunexisCompiler().ParseSource(source, "home-mutations.jnx");
        Assert.True(parse.Success, Diagnostics(parse.Diagnostics));
        var astStorage = Assert.IsType<StorageDeclarationNode>(Assert.Single(parse.Ast!.Declarations));
        Assert.Equal(3, astStorage.Mutations.Count);
        Assert.Equal(StorageMutationKindNode.Update, astStorage.Mutations[1].Kind);
        Assert.Equal(StorageMutationCardinalityNode.Many, astStorage.Mutations[2].Cardinality);

        var structural = new AstToIrTransformer().Transform(parse.Ast);
        var irStorage = Assert.Single(structural.StorageDeclarations);
        Assert.Equal(5, irStorage.Mutations[0].Assignments.Count);
        Assert.NotNull(irStorage.Mutations[1].Predicate);
        Assert.Contains(structural.SourceMap, entry =>
            entry.Path == "/StorageDeclarations[0]/Mutations[1]/Assignments[0]");

        var result = StorageSourceProjector.Project(structural);

        Assert.True(result.Success, Diagnostics(result.Diagnostics));
        Assert.Empty(result.Plans);
        Assert.Equal(3, result.MutationPlans.Count);
        var insert = result.MutationPlans[0];
        Assert.Equal(StorageMutationKind.Insert, insert.Kind);
        Assert.Equal(StorageMutationCardinality.Single, insert.Cardinality);
        Assert.Null(insert.Predicate);
        var update = result.MutationPlans[1];
        Assert.Equal("RenameSensor", update.Name);
        Assert.IsType<StorageComparisonPredicate>(update.Predicate);
        var delete = result.MutationPlans[2];
        Assert.Equal(StorageMutationKind.Delete, delete.Kind);
        Assert.Equal(StorageMutationCardinality.Many, delete.Cardinality);
    }

    [Fact]
    public void ReportsUnsafeSingleMutationAtWhereLocation()
    {
        var result = Project("""
            storage HomeData {
                entity Observation {
                    id: Uuid
                    sensor: Text
                    key(id)
                }
                mutation DeleteSensor(sensor: Text) delete single Observation {
                    where sensor == params.sensor
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SMP041");

        Assert.False(result.Success);
        Assert.Equal("StorageMutation", diagnostic.Category);
        Assert.Equal(8, diagnostic.Line);
    }

    [Fact]
    public void RejectsFieldDependentSetAtAssignmentLocation()
    {
        var result = Project("""
            storage HomeData {
                entity Observation {
                    id: Uuid
                    sensor: Text
                    key(id)
                }
                mutation Rename(id: Uuid) update single Observation {
                    set sensor = sensor
                    where id == params.id
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SMP074");

        Assert.Single(result.Diagnostics);
        Assert.Equal(8, diagnostic.Line);
        Assert.Contains("params.<nazwa> albo literałem", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMutationNameCollidingWithQuery()
    {
        var result = Project("""
            storage HomeData {
                entity Observation {
                    id: Uuid
                    key(id)
                }
                query Find(id: Uuid) from Observation {
                    filter id == params.id
                }
                mutation Find(id: Uuid) delete single Observation {
                    where id == params.id
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SMP080");

        Assert.Equal(9, diagnostic.Line);
        Assert.Empty(result.MutationPlans);
    }

    [Fact]
    public void ReportsNonPortableMutationParameterAtItsLocation()
    {
        var result = Project("""
            storage HomeData {
                entity Observation {
                    id: Uuid
                    key(id)
                }
                mutation Remove(id: Map<Text, Text>) delete single Observation {
                    where id == params.id
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SMP070");

        Assert.Single(result.Diagnostics);
        Assert.Equal(6, diagnostic.Line);
        Assert.False(result.Success);
    }

    [Fact]
    public void PublishesMutationDiagnosticsThroughTheSharedCheckPipeline()
    {
        var source = """
            storage HomeData {
                entity Observation {
                    id: Uuid
                    sensor: Text
                    key(id)
                }
                mutation Unsafe(sensor: Text) delete single Observation {
                    where sensor == params.sensor
                }
            }
            """;
        var parse = new JunexisCompiler().ParseSource(source, "mutation-check.jnx");
        Assert.True(parse.Success, Diagnostics(parse.Diagnostics));

        var result = StructuralAnalysisPipeline.Analyze(parse.Ast!);

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SMP041");
        Assert.Equal("mutation-check.jnx", diagnostic.File);
        Assert.Equal(8, diagnostic.Line);
    }

    private static StorageSourceProjectionResult Project(string source)
    {
        var parse = new JunexisCompiler().ParseSource(source, "mutation-test.jnx");
        Assert.True(parse.Success, Diagnostics(parse.Diagnostics));
        return StorageSourceProjector.Project(new AstToIrTransformer().Transform(parse.Ast!));
    }

    private static string Diagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(item =>
            $"{item.Code} {item.Line}:{item.Column} {item.Message}"));
}
