namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>
/// Kanoniczne scenariusze regresyjne GoCodeGenerator dla alertow, enumow i payloadow.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_CanonicalAlertFragment_EmitsEnumConstructorSkeleton()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
            }

            enum AlertType {
                MotionDetected(Text)
            }

            task BuildAlert(r: SensorReading) @server -> Text {
                let alert = AlertType::MotionDetected(r.device_id)
                log("Motion detected in {r.device_id}")
                return "ok"
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("type AlertType interface {", content);
        Assert.Contains("type AlertTypeMotionDetected struct {", content);
        Assert.Contains("Value string", content);
        Assert.Contains("func NewAlertTypeMotionDetected(value string) AlertType {", content);
        Assert.Contains("alert := NewAlertTypeMotionDetected(r.Device_id)", content);
        Assert.Contains("log.Println(fmt.Sprintf(\"Motion detected in %v\", r.Device_id))", content);
        Assert.Contains("return \"ok\"", content);
    }

    [Fact]
    public async Task Go_CanonicalTemperatureAlertFormatString_PreservesSpecifierAndUnicode()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                celsius: Float
            }

            task BuildAlert(r: SensorReading) @server -> Text {
                log("High temp in {r.device_id}: {r.celsius:.1f}°C")
                return "ok"
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("log.Println(fmt.Sprintf(\"High temp in %v: %.1f\u00b0C\", r.Device_id, r.Celsius))", content);
        Assert.Contains("return \"ok\"", content);
    }

    [Fact]
    public async Task Go_CanonicalHighTempAlertFragment_EmitsStructuredPayloadLiteral()
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

            task BuildAlerts(r: SensorReading) @server -> stream<HomeAlert> {
                send HomeAlert {
                    alert_type: AlertType::HighTemperature,
                    severity: 3,
                    message: "High temp in {r.device_id}: {r.celsius:.1f}°C",
                    at: r.timestamp
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("result := NewStream[HomeAlert]()", content);
        Assert.Contains("publishResult(HomeAlert{Alert_type: NewAlertTypeHighTemperature(), Severity: 3, Message: fmt.Sprintf(\"High temp in %v: %.1f\u00b0C\", r.Device_id, r.Celsius), At: r.Timestamp})", content);
    }

    [Fact]
    public async Task Go_CanonicalMotionDetectedAlertFragment_EmitsEnumPayloadInsideStructuredAlert()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                motion: Bool
                timestamp: Text
            }

            enum AlertType {
                MotionDetected(Text)
            }

            struct HomeAlert {
                alert_type: AlertType
                severity: Int
                message: Text
                at: Text
            }

            task BuildAlerts(r: SensorReading) @server -> stream<HomeAlert> {
                if r.motion and (r.device_id == config.motion_zone_1 or r.device_id == config.motion_zone_2) {
                    send HomeAlert {
                        alert_type: AlertType::MotionDetected(r.device_id),
                        severity: 1,
                        message: "Motion detected in {r.device_id}",
                        at: r.timestamp
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("result := NewStream[HomeAlert]()", content);
        Assert.Contains("if (r.Motion && ((r.Device_id == config.Motion_zone_1) || (r.Device_id == config.Motion_zone_2))) {", content);
        Assert.Contains("publishResult(HomeAlert{Alert_type: NewAlertTypeMotionDetected(r.Device_id), Severity: 1, Message: fmt.Sprintf(\"Motion detected in %v\", r.Device_id), At: r.Timestamp})", content);
    }
}
