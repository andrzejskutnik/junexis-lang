using System.Text.Json;
using JunexisLang.Compiler.Core.Runtime;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class RuntimePrimitivesHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static async Task<int> HandleAsync(bool json)
    {
        var primitives = RuntimePrimitiveRegistry.List()
            .OrderBy(primitive => primitive.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();

        if (json)
        {
            var payload = new RuntimePrimitivesEnvelope("0.2", "runtime primitives", primitives);
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions));
            return 0;
        }

        await Console.Out.WriteLineAsync("Junexis runtime primitives (0.2 experimental)");
        await Console.Out.WriteLineAsync("Name       Status     Targets                 Implementations                 Summary");
        await Console.Out.WriteLineAsync("---------  ---------  ----------------------  ------------------------------  ----------------------------------------");

        foreach (var primitive in primitives)
        {
            await Console.Out.WriteLineAsync(
                $"{primitive.Name,-9}  {primitive.Status,-9}  {string.Join(",", primitive.Targets),-22}  {string.Join(",", primitive.Implementations),-30}  {primitive.Summary}");
        }

        return 0;
    }

    private static RuntimePrimitiveDto ToDto(RuntimePrimitive primitive)
        => new(
            primitive.Name,
            primitive.Status.ToString().ToLowerInvariant(),
            SplitFlags(primitive.Targets),
            SplitFlags(primitive.Implementations),
            SplitFlags(primitive.Surface),
            primitive.Summary);

    private static IReadOnlyList<string> SplitFlags<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var raw = Convert.ToInt64(value);
        if (raw == 0)
            return ["none"];

        return Enum.GetValues<TEnum>()
            .Where(flag =>
            {
                var flagValue = Convert.ToInt64(flag);
                return flagValue != 0 && (raw & flagValue) == flagValue && IsPowerOfTwo(flagValue);
            })
            .Select(flag => flag.ToString())
            .ToList();
    }

    private static bool IsPowerOfTwo(long value)
        => value > 0 && (value & (value - 1)) == 0;

    private sealed record RuntimePrimitivesEnvelope(
        string SchemaVersion,
        string Command,
        IReadOnlyList<RuntimePrimitiveDto> Primitives);

    private sealed record RuntimePrimitiveDto(
        string Name,
        string Status,
        IReadOnlyList<string> Targets,
        IReadOnlyList<string> Implementations,
        IReadOnlyList<string> Surface,
        string Summary);
}
