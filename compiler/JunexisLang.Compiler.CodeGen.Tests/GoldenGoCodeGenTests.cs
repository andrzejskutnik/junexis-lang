using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>
/// Golden/snapshot tests dla wygenerowanego kodu Go — weryfikują kluczowe struktury
/// wygenerowanego <c>main.go</c> dla trzech kanonicznych przykładów E2E green.
///
/// Testy celowo sprawdzają stabilne kontrakty (sygnatury funkcji, typy, main wire-up),
/// a nie całą zawartość pliku — żeby nie były kruche przy kosmetycznych zmianach codegen.
///
/// Canonical E2E green: 01-temperature-monitor, 02-ecommerce-api, 13-minimal-pipeline.
/// </summary>
public sealed class GoldenGoCodeGenTests
{
    private static readonly CodeGenOptions Opts = new(Path.GetTempPath());

    private static async Task<string> GenerateGoAsync(string jnxSource)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, jnxSource);
            var result = await new JunexisCompiler().CheckAsync(new CheckOptions(tmp));
            Assert.True(result.Success,
                $"Parse failed:\n{string.Join('\n', result.Diagnostics.Select(d => d.Message))}");
            var ir = new AstToIrTransformer().Transform(result.Ast!);
            var gen = new GoCodeGenerator();
            var files = gen.Generate(ir, Opts);
            var mainFile = files.FirstOrDefault(f => f.FilePath.EndsWith("main.go"));
            Assert.NotNull(mainFile);
            return mainFile.Content;
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ── 01-temperature-monitor ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_01_TemperatureMonitor_EmitsPackageMain()
    {
        var content = await GenerateGoForFile("01-temperature-monitor.jnx");
        Assert.Contains("package main", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_01_TemperatureMonitor_EmitsTempReadingStruct()
    {
        var content = await GenerateGoForFile("01-temperature-monitor.jnx");
        Assert.Contains("type TempReading struct", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_01_TemperatureMonitor_EmitsTempProcessorFunc()
    {
        var content = await GenerateGoForFile("01-temperature-monitor.jnx");
        Assert.Contains("func TempProcessor(", content);
        Assert.Contains("chan TempReading", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_01_TemperatureMonitor_EmitsStateStructAndPersistence()
    {
        var content = await GenerateGoForFile("01-temperature-monitor.jnx");
        Assert.Contains("type TempProcessorState struct", content);
        Assert.Contains("func loadTempProcessorState()", content);
        Assert.Contains("func saveTempProcessorState(", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_01_TemperatureMonitor_EmitsMainWithWireUp()
    {
        var content = await GenerateGoForFile("01-temperature-monitor.jnx");
        Assert.Contains("func main()", content);
        Assert.Contains("wg.Add(1)", content);
        Assert.Contains("TempProcessor(", content);
        Assert.Contains("wg.Wait()", content);
    }

    // ── 02-ecommerce-api ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_02_EcommerceApi_EmitsPackageMain()
    {
        var content = await GenerateGoForFile("02-ecommerce-api.jnx");
        Assert.Contains("package main", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_02_EcommerceApi_EmitsOrderStruct()
    {
        var content = await GenerateGoForFile("02-ecommerce-api.jnx");
        // przykład ma struktury związane z zamówieniami
        Assert.Contains("type ", content);
        Assert.Contains("struct {", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_02_EcommerceApi_EmitsMainWithWireUp()
    {
        var content = await GenerateGoForFile("02-ecommerce-api.jnx");
        Assert.Contains("func main()", content);
        Assert.Contains("wg.Wait()", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_02_EcommerceApi_EmitsCyclicBridgeGoroutine()
    {
        // przykład 02 ma cykliczny pipeline — codegen musi emitować bridge goroutine
        var content = await GenerateGoForFile("02-ecommerce-api.jnx");
        Assert.Contains("go func()", content);
    }

    // ── 13-minimal-pipeline ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_13_MinimalPipeline_EmitsPackageMain()
    {
        var content = await GenerateGoForFile("13-minimal-pipeline.jnx");
        Assert.Contains("package main", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_13_MinimalPipeline_EmitsDaneSensoraStruct()
    {
        var content = await GenerateGoForFile("13-minimal-pipeline.jnx");
        Assert.Contains("type DaneSensora struct", content);
        Assert.Contains("Temperatura float32", content);
        Assert.Contains("Wilgotnosc float32", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_13_MinimalPipeline_EmitsPrzetworzoneDaneStruct()
    {
        var content = await GenerateGoForFile("13-minimal-pipeline.jnx");
        Assert.Contains("type PrzetworzoneDane struct", content);
        Assert.Contains("Srednia_temp float32", content);
        Assert.Contains("Srednia_wilg float32", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_13_MinimalPipeline_EmitsProcesorDanychWithChanSignature()
    {
        var content = await GenerateGoForFile("13-minimal-pipeline.jnx");
        Assert.Contains("func ProcesorDanych(wejscie chan DaneSensora) chan PrzetworzoneDane", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_13_MinimalPipeline_EmitsStateStructWithAllFields()
    {
        var content = await GenerateGoForFile("13-minimal-pipeline.jnx");
        Assert.Contains("type ProcesorDanychState struct", content);
        Assert.Contains("Suma_temp float32 `json:\"suma_temp\"`", content);
        Assert.Contains("Suma_wilg float32 `json:\"suma_wilg\"`", content);
        Assert.Contains("Licznik uint32 `json:\"licznik\"`", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_13_MinimalPipeline_EmitsMainWithPlaceholderAndWireUp()
    {
        var content = await GenerateGoForFile("13-minimal-pipeline.jnx");
        Assert.Contains("func main()", content);
        // CzytnikSensora jest @embedded — codegen emituje placeholder channel
        Assert.Contains("__wire0 := make(chan DaneSensora, 64)", content);
        Assert.Contains("ProcesorDanych(__wire0)", content);
        Assert.Contains("wg.Wait()", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "E2E-01")]
    public async Task Golden_13_MinimalPipeline_EmitsGeneratedByHeader()
    {
        var content = await GenerateGoForFile("13-minimal-pipeline.jnx");
        Assert.StartsWith("// Generated by JunexisLang compiler", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "M-Platform-01")]
    public async Task Golden_03_SmartHome_EmitsTraceHelperWithNarrativeId()
    {
        var content = await GenerateGoForFile("03-smart-home.jnx");

        Assert.Contains("var _traceURL = os.Getenv(\"JUNEXIS_TRACE_URL\")", content);
        Assert.Contains("var _traceBuildID = os.Getenv(\"JUNEXIS_BUILD_ID\")", content);
        Assert.Contains("var _traceBundleID = os.Getenv(\"JUNEXIS_BUNDLE_ID\")", content);
        Assert.Contains("attributes[\"build_id\"] = _traceBuildID", content);
        Assert.Contains("attributes[\"bundle_id\"] = _traceBundleID", content);
        Assert.Contains("func _emitTrace(kind string, taskId uint32, file string, line int, function string, attributes map[string]string) uint64", content);
        Assert.Contains("func _debugGate(eventId uint64)", content);
        Assert.Contains("Kind string `json:\"kind\"`", content);
        Assert.Contains("File string `json:\"file,omitempty\"`", content);
        Assert.Contains("Line int `json:\"line,omitempty\"`", content);
        Assert.Contains("Function string `json:\"function,omitempty\"`", content);
        Assert.Contains("Attributes map[string]string `json:\"attributes\"`", content);
        Assert.Contains("json.Marshal(body)", content);
        Assert.Contains("json.Unmarshal(responseBody, &envelope)", content);
        Assert.Contains("_debugGate(envelope.Event.EventId)", content);
        Assert.Contains("_traceURL+\"/debug/session\"", content);
        Assert.Contains("Smart Home Automation", content);
        Assert.Contains("_emitTrace(\"COMM_SEND\",", content);
        Assert.Contains("_emitTrace(\"COMM_RECV\",", content);
        Assert.Contains("\"startup-", content);
        Assert.DoesNotContain("\"Kind\":%d", content);
    }

    [Fact]
    [Trait("Category", "Golden")]
    [Trait("Issue", "M-Platform-01")]
    public async Task Golden_03_SmartHome_EmitsOnlyRequiredTraceImports()
    {
        var content = await GenerateGoForFile("03-smart-home.jnx");

        Assert.Contains("\t\"io\"", content);
        Assert.DoesNotContain("\t\"net\"", content);
    }

    // ── helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generuje kod Go dla przykładowego pliku .jnx z katalogu <c>examples/</c>.
    /// Ścieżka wyznaczana jest względem assembly testowego.
    /// </summary>
    private static async Task<string> GenerateGoForFile(string exampleFileName)
    {
        var examplesDir = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "..", "..", "..", "..", "..", "examples"));

        var jnxPath = Path.Combine(examplesDir, exampleFileName);
        Assert.True(File.Exists(jnxPath), $"Example not found: {jnxPath}");

        var source = await File.ReadAllTextAsync(jnxPath);
        return await GenerateGoAsync(source);
    }
}
