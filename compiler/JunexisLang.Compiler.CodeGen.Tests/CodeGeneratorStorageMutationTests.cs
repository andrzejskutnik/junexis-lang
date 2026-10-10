namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>Kontrakty generowanego klienta nazwanych mutacji storage M14.9.</summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_StorageMutation_EmitsTypedProviderNeutralClient()
    {
        var ir = await TransformToIrAsync("""
            storage HomeData {
                entity Observation { id: Uuid sensor: Text value: Float key(id) }
                mutation AddObservation(id: Uuid, sensor: Text, value: Float)
                    insert single Observation {
                    set id = params.id
                    set sensor = params.sensor
                    set value = params.value
                }
            }
            task Api() @server { }
            """);

        var files = new GoCodeGenerator().Generate(ir, Opts);
        var client = files.Single(file =>
            file.FilePath == "server/junexis/storage/mutations/generated.go").Content;
        var runtime = files.Single(file =>
            file.FilePath == "server/junexis/storage/runtime/client.go").Content;

        Assert.Contains("type HomeData_AddObservationParams struct", client);
        Assert.Contains("Id string", client);
        Assert.Contains("type HomeData_AddObservationResult struct", client);
        Assert.Contains("AffectedRows int64", client);
        Assert.Contains("func HomeData_AddObservation(ctx context.Context", client);
        Assert.Contains("junexis.storage.mutation-invocation/0.1", client);
        Assert.Contains("provider.MutationPlan(ctx, input)", client);
        Assert.Contains("func (p *Provider) MutationPlan", runtime);
        Assert.DoesNotContain("postgres", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(files, file =>
            file.FilePath == "server/junexis/storage/queries/generated.go");
    }

    [Fact]
    public async Task Go_ServerTaskMutationCall_EmitsRuntimeLifecycleAndTypedWrapper()
    {
        var ir = await TransformToIrAsync("""
            storage HomeData {
                entity Observation { id: Uuid sensor: Text key(id) }
                mutation AddObservation(id: Uuid, sensor: Text) insert single Observation {
                    set id = params.id
                    set sensor = params.sensor
                }
            }
            task Add(id: Uuid, sensor: Text) @server -> HomeData_AddObservationResult {
                return HomeData_AddObservation(sensor: sensor, id: id)
            }
            """);

        var main = new GoCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go").Content;

        Assert.Contains("type HomeData_AddObservationResult struct", main);
        Assert.Contains("func HomeData_AddObservation(id string, sensor string) HomeData_AddObservationResult", main);
        Assert.Contains("junexisMutations.HomeData_AddObservation(", main);
        Assert.Contains("return HomeData_AddObservation(id, sensor)", main);
        Assert.Contains("junexisStorageRuntime.OpenFromEnvironment(ctx)", main);
        Assert.Contains("junexisStorageProvider = storageProvider", main);
        Assert.Contains("storageProvider.Close()", main);
    }
}
