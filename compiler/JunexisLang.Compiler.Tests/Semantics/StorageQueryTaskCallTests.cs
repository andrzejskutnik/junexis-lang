namespace JunexisLang.Compiler.Tests.Semantics;

using JunexisLang.Compiler.Core;

public sealed class StorageQueryTaskCallTests
{
    [Fact]
    public async Task NamedQuery_IsTypedAndCallableFromServerTask()
    {
        var result = await CheckAsync("""
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

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
    }

    [Fact]
    public async Task NamedQuery_RejectsWrongArgumentType()
    {
        var result = await CheckAsync("""
            storage HomeData {
                entity Observation { id: Uuid homeId: Uuid key(id) }
                query ByHome(homeId: Uuid) from Observation { project id }
            }
            task Read(value: Text) @server { HomeData_ByHome(value) }
            """);

        Assert.Contains(result.Diagnostics, item => item.Code == "SQC005");
    }

    [Fact]
    public async Task NamedQuery_RejectsNonServerTarget()
    {
        var result = await CheckAsync("""
            storage HomeData {
                entity Observation { id: Uuid homeId: Uuid sensor: Text key(id) }
                query ByHome(homeId: Uuid, sensor: Text) from Observation { project id }
            }
            task Read(homeId: Uuid, sensor: Text) @report {
                HomeData_ByHome(homeId, sensor)
            }
            """);

        Assert.Contains(result.Diagnostics, item => item.Code == "SQC001");
    }

    private static async Task<CompilerResult> CheckAsync(string source)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, source);
            return await new JunexisCompiler().CheckAsync(new CheckOptions(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
