namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>
/// Kanoniczne scenariusze regresyjne TypeScriptCodeGenerator dla alertow, enumow i payloadow.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task TS_CanonicalAlertFragment_PreservesEnumCallAndTemplateText()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
            }

            enum AlertType {
                MotionDetected(Text)
            }

            task BuildAlert(r: SensorReading) @report -> Text {
                let alert = AlertType::MotionDetected(r.device_id)
                log("Motion detected in {r.device_id}")
                return "ok"
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("export async function BuildAlert(r: SensorReading): Promise<string>", content);
        Assert.Contains("let alert = { kind: 'MotionDetected', value: r.device_id };", content);
        Assert.Contains("console.log(`Motion detected in ${r.device_id}`);", content);
        Assert.Contains("return 'ok';", content);
    }

    [Fact]
    public async Task TS_CanonicalTemperatureAlertFormatString_PreservesSpecifierAndUnicode()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                celsius: Float
            }

            task BuildAlert(r: SensorReading) @report -> Text {
                log("High temp in {r.device_id}: {r.celsius:.1f}°C")
                return "ok"
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("console.log(`High temp in ${r.device_id}: ${r.celsius.toFixed(1)}°C`);", content);
        Assert.Contains("return 'ok';", content);
    }

    [Fact]
    public async Task TS_CanonicalHighTempAlertFragment_EmitsStructuredPayloadLiteral()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                celsius: Float
                timestamp: Text
            }

            enum AlertType {
                HighTemperature
            }

            struct HomeAlert {
                alert_type: AlertType
                severity: Int
                message: Text
                at: Text
            }

            task BuildAlerts(r: SensorReading) @report -> stream<HomeAlert> {
                send HomeAlert {
                    alert_type: AlertType::HighTemperature,
                    severity: 3,
                    message: "High temp in {r.device_id}: {r.celsius:.1f}°C",
                    at: r.timestamp
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("let __latestResult: HomeAlert | undefined;", content);
        Assert.Contains("result.publish({ alert_type: { kind: 'HighTemperature' }, severity: 3, message: `High temp in ${r.device_id}: ${r.celsius.toFixed(1)}°C`, at: r.timestamp });", content);
    }
}