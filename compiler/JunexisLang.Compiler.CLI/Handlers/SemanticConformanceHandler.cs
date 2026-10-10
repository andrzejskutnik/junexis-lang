using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class SemanticConformanceHandler
{
    private const string UnboundSchema = "junexis.semantic-conformance/0.1";
    private const string BoundSchema = "junexis.semantic-conformance/0.2";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static async Task<int> HandleAsync(FileInfo report, bool json)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!report.Exists)
        {
            await Console.Error.WriteLineAsync($"error: conformance report was not found: {report.FullName}");
            return 2;
        }

        try
        {
            await using var stream = report.OpenRead();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var schema = RequiredString(root, "schemaVersion");
            if (schema is not UnboundSchema and not BoundSchema)
                throw new InvalidDataException($"Unsupported Semantic Conformance schema '{schema}'.");

            var identity = schema == BoundSchema ? ReadIdentity(root) : null;

            var summary = root.GetProperty("summary");
            var scenarios = root.GetProperty("scenarios").EnumerateArray().ToArray();
            var gaps = scenarios
                .Where(scenario => !scenario.GetProperty("targetSatisfied").GetBoolean())
                .Select(scenario => new ConformanceGap(
                    RequiredString(scenario, "id"),
                    RequiredString(scenario, "achievedLevel"),
                    RequiredString(scenario, "targetLevel")))
                .ToArray();
            var view = new ConformanceView(
                schema,
                RequiredString(root, "conformanceStatus"),
                RequiredString(root, "semanticContract"),
                RequiredString(root, "targetLevel"),
                summary.GetProperty("scenarioCount").GetInt32(),
                summary.GetProperty("targetSatisfiedCount").GetInt32(),
                summary.GetProperty("gapCount").GetInt32(),
                gaps,
                identity,
                Blocking: false,
                ReportPath: report.FullName);

            if (json)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(view, JsonOptions));
                return 0;
            }

            await Console.Out.WriteLineAsync(
                $"Semantic Conformance: {view.Status.ToUpperInvariant()} · {view.TargetSatisfiedCount}/{view.ScenarioCount} target(s) · {view.GapCount} gap(s)");
            await Console.Out.WriteLineAsync($"Contract: {view.SemanticContract} · default target {view.TargetLevel}");
            if (view.Identity is { } bound)
                await Console.Out.WriteLineAsync($"Identity: BOUND · build {bound.BuildId[..12]} · bundle {bound.BundleId[..12]} · binding {bound.BindingSha256[..12]}");
            if (view.Gaps.Count > 0)
            {
                await Console.Out.WriteLineAsync("Non-blocking conformance gaps:");
                foreach (var gap in view.Gaps)
                    await Console.Out.WriteLineAsync($"- {gap.Id}: {gap.AchievedLevel} → {gap.TargetLevel}");
            }
            await Console.Out.WriteLineAsync("Conformance is quality evidence; it does not change compilation success.");
            return 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException)
        {
            await Console.Error.WriteLineAsync($"error: invalid conformance report: {ex.Message}");
            return 2;
        }
    }

    private static string RequiredString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new InvalidDataException($"Conformance report has no string '{propertyName}'.");

    private static ConformanceIdentity ReadIdentity(JsonElement root)
    {
        if (!root.TryGetProperty("identity", out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Bound conformance report has no identity object.");
        var status = RequiredString(value, "status");
        var buildId = RequiredHash(value, "buildId");
        var bundleId = RequiredHash(value, "bundleId");
        var ledgerSha256 = RequiredHash(value, "ledgerSha256");
        var bindingSha256 = RequiredHash(value, "bindingSha256");
        if (status != "bound" || RequiredString(value, "algorithm") != "sha256")
            throw new InvalidDataException("Bound conformance identity must use status 'bound' and sha256.");
        if (ledgerSha256 != RequiredHash(root, "ledgerSha256"))
            throw new InvalidDataException("Conformance identity ledger hash does not match the report.");
        var material = Encoding.UTF8.GetBytes($"{buildId}\n{bundleId}\n{ledgerSha256}");
        var expected = Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant();
        if (bindingSha256 != expected)
            throw new InvalidDataException("Conformance identity binding hash is invalid.");
        return new ConformanceIdentity(buildId, bundleId, ledgerSha256, bindingSha256);
    }

    private static string RequiredHash(JsonElement element, string propertyName)
    {
        var value = RequiredString(element, propertyName);
        return value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : throw new InvalidDataException($"Conformance identity '{propertyName}' must be a lowercase SHA-256.");
    }

    private sealed record ConformanceView(
        string SchemaVersion,
        string Status,
        string SemanticContract,
        string TargetLevel,
        int ScenarioCount,
        int TargetSatisfiedCount,
        int GapCount,
        IReadOnlyList<ConformanceGap> Gaps,
        ConformanceIdentity? Identity,
        bool Blocking,
        string ReportPath);

    private sealed record ConformanceGap(string Id, string AchievedLevel, string TargetLevel);

    private sealed record ConformanceIdentity(
        string BuildId,
        string BundleId,
        string LedgerSha256,
        string BindingSha256);
}
