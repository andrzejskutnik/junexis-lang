namespace JunexisLang.Compiler.CodeGen.Tests;

using System.Text.Json;
using JunexisLang.Compiler.IR;

public sealed class SemanticConformanceLedgerTests
{
    private const string LedgerSchema = "junexis.semantic-conformance-ledger/0.1";
    private static readonly string[] Backends = ["cpp", "go", "typescript"];
    private static readonly Dictionary<string, int> LevelRanks = new(StringComparer.Ordinal)
    {
        ["S0"] = 0,
        ["S1"] = 1,
        ["S2"] = 2,
        ["S3"] = 3,
        ["S4"] = 4
    };

    [Fact]
    [Trait("Category", "SemanticConformanceLedger")]
    public void Ledger_IsVersionedCompleteAndHonestAboutCurrentEvidence()
    {
        using var document = ReadLedger();
        var root = document.RootElement;

        Assert.Equal(LedgerSchema, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("junexis.semantic-runtime/0.1", root.GetProperty("semanticContract").GetString());
        Assert.Equal("S3", root.GetProperty("targetLevel").GetString());

        var scenarios = root.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.Equal(6, scenarios.Length);
        Assert.Equal(scenarios.Length, scenarios.Select(GetId).Distinct(StringComparer.Ordinal).Count());

        foreach (var scenario in scenarios)
        {
            Assert.False(string.IsNullOrWhiteSpace(GetId(scenario)));
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("construct").GetString()));
            AssertSemanticsAreDefined(scenario);

            var evidence = scenario.TryGetProperty("backendEvidence", out var backendEvidence)
                ? backendEvidence.EnumerateArray().ToArray()
                : scenario.GetProperty("targetEvidence").EnumerateArray().ToArray();
            if (scenario.TryGetProperty("backendEvidence", out _))
                Assert.Equal(Backends, evidence.Select(item => item.GetProperty("backend").GetString()).Order().ToArray());
            else
                Assert.Equal(["report", "server"], evidence.Select(item => item.GetProperty("target").GetString()!).Order().ToArray());
            Assert.Equal(evidence.Length, evidence.Select(item => item.GetProperty("backend").GetString()).Distinct(StringComparer.Ordinal).Count());
            foreach (var item in evidence)
            {
                var level = item.GetProperty("level").GetString();
                Assert.NotNull(level);
                Assert.True(LevelRanks.ContainsKey(level), $"Unknown conformance level '{level}'.");
                Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("test").GetString()));
            }

            if (scenario.TryGetProperty("equivalenceEvidence", out var equivalence))
            {
                Assert.Equal("S3", equivalence.GetProperty("level").GetString());
                Assert.Equal("canonical-json", equivalence.GetProperty("normalForm").GetString());
                Assert.False(string.IsNullOrWhiteSpace(equivalence.GetProperty("test").GetString()));
                Assert.All(evidence, item => Assert.True(
                    LevelRanks[item.GetProperty("level").GetString()!] >= LevelRanks["S2"],
                    "S3 equivalence requires executable S2 evidence for every backend."));
            }

            if (scenario.TryGetProperty("distributedEvidence", out var distributed))
            {
                Assert.Equal("S4", scenario.GetProperty("targetLevel").GetString());
                Assert.Equal("S4", distributed.GetProperty("level").GetString());
                Assert.Equal("canonical-json", distributed.GetProperty("normalForm").GetString());
                Assert.All(evidence, item => Assert.True(
                    LevelRanks[item.GetProperty("level").GetString()!] >= LevelRanks["S2"],
                    "S4 distributed evidence requires executable target evidence."));
            }
        }

        foreach (var scenario in scenarios)
        {
            if (scenario.TryGetProperty("backendEvidence", out var evidence))
                Assert.All(evidence.EnumerateArray(), item => Assert.Equal("S2", item.GetProperty("level").GetString()));
        }
    }

    private static JsonDocument ReadLedger()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "conformance", "semantic-scenarios.json");
        Assert.True(File.Exists(path), $"Semantic conformance ledger was not copied to '{path}'.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string GetId(JsonElement scenario)
        => scenario.GetProperty("id").GetString() ?? string.Empty;

    private static void AssertSemanticsAreDefined(JsonElement scenario)
    {
        var semantics = scenario.GetProperty("semantics");
        if (scenario.GetProperty("construct").GetString() == "scalar/control-flow")
        {
            Assert.Equal(["Int", "Float", "Text", "Bool"],
                semantics.GetProperty("values").EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal(["if", "else-if", "else"],
                semantics.GetProperty("controlFlow").EnumerateArray().Select(item => item.GetString()!).ToArray());
            return;
        }

        if (scenario.GetProperty("construct").GetString() == "task/state/every")
        {
            Assert.Equal(TaskLifecycleRuntimeContract.Version, semantics.GetProperty("contract").GetString());
            Assert.Equal("once-per-task-instance", semantics.GetProperty("initialization").GetString());
            Assert.Equal("persists-between-invocations", semantics.GetProperty("state").GetString());
            Assert.Equal("after-interval", semantics.GetProperty("firstTick").GetString());
            Assert.Equal("between-completed-ticks", semantics.GetProperty("cancellation").GetString());
            return;
        }

        if (scenario.GetProperty("construct").GetString() == "extern/wire-boundary")
        {
            Assert.Equal("typed-injected-source-target", semantics.GetProperty("extern").GetString());
            Assert.Equal("server", semantics.GetProperty("sourceTarget").GetString());
            Assert.Equal("report", semantics.GetProperty("targetTarget").GetString());
            Assert.Equal(WireRuntimeContractCatalog.Json.Version, semantics.GetProperty("wireVersion").GetString());
            Assert.Equal(WireRuntimeContractCatalog.Json.Encoding, semantics.GetProperty("encoding").GetString());
            Assert.Equal("reject", semantics.GetProperty("unknownVersion").GetString());
            return;
        }

        var contract = scenario.GetProperty("construct").GetString() switch
        {
            "chan<T>" => CommunicationRuntimeContractCatalog.Chan,
            "queue<T>" => CommunicationRuntimeContractCatalog.Queue,
            "stream<T>" => CommunicationRuntimeContractCatalog.Stream,
            var construct => throw new Xunit.Sdk.XunitException($"Unknown ledger construct '{construct}'.")
        };

        Assert.Equal(contract.Delivery.ToString(), semantics.GetProperty("delivery").GetString());
        Assert.Equal(contract.Backpressure.ToString(), semantics.GetProperty("backpressure").GetString());
        Assert.Equal(contract.Retention.ToString(), semantics.GetProperty("retention").GetString());
        Assert.Equal(contract.Ordering.ToString(), semantics.GetProperty("ordering").GetString());
    }
}
