namespace JunexisLang.Compiler.Tests.Semantics;

using JunexisLang.Compiler.Core;

public sealed class StorageMutationTaskCallTests
{
    [Fact]
    public async Task NamedMutation_IsTypedAndCallableFromServerTask()
    {
        var result = await CheckAsync("""
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

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
    }

    [Fact]
    public async Task NamedMutation_RejectsWrongArgumentType()
    {
        var result = await CheckAsync("""
            storage HomeData {
                entity Observation { id: Uuid key(id) }
                mutation AddObservation(id: Uuid) insert single Observation { set id = params.id }
            }
            task Add(value: Text) @server { HomeData_AddObservation(value) }
            """);

        Assert.Contains(result.Diagnostics, item => item.Code == "SMC005");
    }

    [Fact]
    public async Task NamedMutation_RejectsNonServerTarget()
    {
        var result = await CheckAsync("""
            storage HomeData {
                entity Observation { id: Uuid key(id) }
                mutation AddObservation(id: Uuid) insert single Observation { set id = params.id }
            }
            task Add(id: Uuid) @report { HomeData_AddObservation(id) }
            """);

        Assert.Contains(result.Diagnostics, item => item.Code == "SMC001");
    }

    [Fact]
    public async Task NamedMutation_RejectsUnknownNamedArgument()
    {
        var unknown = await CheckAsync(Source("HomeData_AddObservation(id: id, other: sensor)"));

        Assert.Contains(unknown.Diagnostics, item => item.Code == "SMC004");
    }

    private static string Source(string call) => $$"""
        storage HomeData {
            entity Observation { id: Uuid sensor: Text key(id) }
            mutation AddObservation(id: Uuid, sensor: Text) insert single Observation {
                set id = params.id
                set sensor = params.sensor
            }
        }
        task Add(id: Uuid, sensor: Text) @server { {{call}} }
        """;

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
