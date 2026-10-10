namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>Kontrakty generowanego klienta nazwanych zapytań storage M13.9.</summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_StorageModelWithoutQuery_DoesNotEmitUnusedRuntimePackages()
    {
        var ir = await TransformToIrAsync("""
            storage HomeData {
                entity Observation {
                    id: Uuid
                    key(id)
                }
            }
            task Api() @server { }
            """);

        var files = new GoCodeGenerator().Generate(ir, Opts);

        Assert.DoesNotContain(files, file => file.FilePath.Contains("junexis/storage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Go_StorageQuery_EmitsTypedProviderNeutralClient()
    {
        var ir = await TransformToIrAsync("""
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

                query RecentObservations(homeId: Uuid) from Observation {
                    filter homeId == params.homeId and value > 0.0
                    project sensor as sensorName, value, recordedAt
                    sort recordedAt desc
                    limit 20
                }
            }

            task Api() @server { }
            """);

        var files = new GoCodeGenerator().Generate(ir, Opts);
        var queryClient = files.Single(file =>
            file.FilePath == "server/junexis/storage/queries/generated.go").Content;
        var runtimeClient = files.Single(file =>
            file.FilePath == "server/junexis/storage/runtime/client.go").Content;

        Assert.Contains("type HomeData_RecentObservationsParams struct", queryClient);
        Assert.Contains("HomeId string", queryClient);
        Assert.Contains("type HomeData_RecentObservationsRow struct", queryClient);
        Assert.Contains("SensorName string `json:\"sensorName\"`", queryClient);
        Assert.Contains("RecordedAt string `json:\"recordedAt\"`", queryClient);
        Assert.Contains("func HomeData_RecentObservations(ctx context.Context", queryClient);
        Assert.Contains("junexis.storage.query-invocation/0.1", queryClient);
        Assert.Contains("func (p *Provider) QueryPlan", runtimeClient);
        Assert.DoesNotContain("postgres", queryClient, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", queryClient, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Go_ServerTaskQueryCall_EmitsRuntimeLifecycleAndTypedWrapper()
    {
        var ir = await TransformToIrAsync("""
            storage HomeData {
                entity Observation { id: Uuid homeId: Uuid sensor: Text key(id) }
                query ByHome(homeId: Uuid, sensor: Text) from Observation {
                    filter homeId == params.homeId and sensor == params.sensor
                    project sensor
                }
            }
            task Read(homeId: Uuid, sensor: Text) @server -> List<HomeData_ByHomeRow> {
                return HomeData_ByHome(sensor: sensor, homeId: homeId)
            }
            """);

        var main = new GoCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go").Content;

        Assert.Contains("type HomeData_ByHomeRow struct", main);
        Assert.Contains("func HomeData_ByHome(homeId string, sensor string) []HomeData_ByHomeRow", main);
        Assert.Contains("return HomeData_ByHome(homeId, sensor)", main);
        Assert.Contains("junexisStorageRuntime.OpenFromEnvironment(ctx)", main);
        Assert.Contains("junexisStorageProvider = storageProvider", main);
        Assert.Contains("storageProvider.Close()", main);
    }
}
