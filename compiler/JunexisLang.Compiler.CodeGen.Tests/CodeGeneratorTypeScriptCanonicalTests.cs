namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>
/// Kanoniczne scenariusze regresyjne TypeScriptCodeGenerator dla sterowania, sensorow i logiki przeplywu.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: scenariusze kanoniczne ───────────────────

    [Fact]
    public async Task TS_CanonicalSensorAggregatorFragment_PreservesRecvAnyBinding()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
            }

            task SensorAggregator(
                kitchen: stream<SensorReading>,
                bedroom: stream<SensorReading>,
                living_room: stream<SensorReading>
            ) @report -> stream<SensorReading> {
                loop {
                    match recv_any(kitchen, bedroom, living_room) {
                        reading -> { send reading; }
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var files = gen.Generate(ir, Opts);
        var content = files.Single(file => file.FilePath == "report/index.ts").Content;
        var runtime = files.Single(file => file.FilePath == "report/runtime.ts").Content;

        Assert.Contains("export async function SensorAggregator(kitchen: Stream<SensorReading>, bedroom: Stream<SensorReading>, living_room: Stream<SensorReading>): Promise<Stream<SensorReading>>", content);
        Assert.Contains("import { Channel, Queue, Stream, recv_any } from './runtime.js';", content);
        Assert.Contains("while (true) {", content);
        Assert.Contains("const __match = recv_any(kitchen, bedroom, living_room);", content);
        Assert.Contains("const reading = __match;", content);
        Assert.Contains("let __latestResult: SensorReading | undefined;", content);
        Assert.Contains("const result = {", content);
        Assert.Contains("result.publish(reading);", content);
        Assert.Contains("export function recv_any<T>", runtime);
        Assert.Contains("peek(): { messageId: string; value: T } | undefined", runtime);
    }

    [Fact]
    public async Task TS_CanonicalCooldownGuardFragment_EmitsBooleanNegationOnCall()
    {
        const string source = """
            struct SensorReading {
                celsius: Float
            }

            task BuildAlerts(r: SensorReading) @report {
                if r.celsius > config.temp_high {
                    if not is_on_cooldown("temp_high") {
                        set_cooldown("temp_high", 10m)
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("if ((r.celsius > config.temp_high)) {", content);
        Assert.Contains("if ((!is_on_cooldown('temp_high'))) {", content);
        Assert.Contains("set_cooldown('temp_high', (10 * 60 * 1000));", content);
    }

    [Fact]
    public async Task TS_CanonicalMotionGuardFragment_MapsLogicalOperatorsToTypeScriptSyntax()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                motion: Bool
            }

            task BuildAlerts(r: SensorReading) @report {
                if r.motion and (r.device_id == config.motion_zone_1 or r.device_id == config.motion_zone_2) {
                    log("Motion detected in {r.device_id}")
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("if ((r.motion && ((r.device_id == config.motion_zone_1) || (r.device_id == config.motion_zone_2)))) {", content);
        Assert.Contains("console.log(`Motion detected in ${r.device_id}`);", content);
    }

    [Fact]
    public async Task TS_CanonicalHvacHeatingGuardFragment_EmitsReducedIntensityAndStateAssignment()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                celsius: Float
            }

            struct DeviceCommand {
                target: Text
                action: Text
                intensity: Float
            }

            task HVACController(r: SensorReading) @report -> chan<DeviceCommand> {
                state hvac_on: Bool = false

                let needs_heating = r.celsius < config.target_temp - config.hysteresis

                if needs_heating and not hvac_on {
                    send DeviceCommand { target: r.device_id, action: "hvac_on", intensity: 0.5 }
                    hvac_on = true
                    log("HVAC HEAT ON: {r.device_id} temp={r.celsius:.1f}")
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("const result: Channel<DeviceCommand> = new Channel<DeviceCommand>(1);", content);
        Assert.Contains("let needs_heating = (r.celsius < (config.target_temp - config.hysteresis));", content);
        Assert.Contains("if ((needs_heating && (!state.hvac_on))) {", content);
        Assert.Contains("await result.send({ target: r.device_id, action: 'hvac_on', intensity: 0.5 });", content);
        Assert.Contains("state.hvac_on = true;", content);
        Assert.Contains("console.log(`HVAC HEAT ON: ${r.device_id} temp=${r.celsius.toFixed(1)}`);", content);
    }

    [Fact]
    public async Task TS_CanonicalHealthMonitorFragment_EmitsStructuredPayloadAndUptimeDelta()
    {
        const string source = """
            struct SystemHealth {
                devices_online: Int
                devices_offline: Int
                avg_temp: Float
                alert_count: Int
                uptime: Time
            }

            task HealthMonitor() @report -> stream<SystemHealth> {
                state start_time: Time = now()
                state alert_count: Int = 0

                every 60s {
                    let online = count_online_devices()
                    let offline = count_total_devices() - online

                    send SystemHealth {
                        devices_online: online,
                        devices_offline: offline,
                        avg_temp: get_avg_temp(),
                        alert_count: alert_count,
                        uptime: now() - start_time
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("export async function HealthMonitor(): Promise<Stream<SystemHealth>> {", content);
        Assert.Contains("let offline = (count_total_devices() - online);", content);
        Assert.Contains("result.publish({ devices_online: online, devices_offline: offline, avg_temp: get_avg_temp(), alert_count: state.alert_count, uptime: (now() - state.start_time) });", content);
    }

    [Fact]
    public async Task TS_CanonicalCommandRoutingFragment_PreservesFieldAccessAndWildcardArm()
    {
        const string source = """
            struct DeviceCommand {
                target: Text
                action: Text
                intensity: Float
            }

            task ApplyCommands(commands: [DeviceCommand]) @report {
                config {
                    hvac_pin: u8 : 12
                    fan_pin: u8 : 13
                }

                for cmd in commands {
                    if cmd.target == "kitchen" {
                        match cmd.action {
                            "hvac_on"  -> digital_write(config.hvac_pin, true)
                            "hvac_off" -> digital_write(config.hvac_pin, false)
                            "fan_on"   -> pwm_write(config.fan_pin, cmd.intensity)
                            "fan_off"  -> pwm_write(config.fan_pin, 0.0)
                            _           -> { log("Unknown command: {cmd.action}") }
                        }
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("for await (const cmd of commands) {", content);
        Assert.Contains("if ((cmd.target == 'kitchen')) {", content);
        Assert.Contains("const __match = cmd.action;", content);
        Assert.Contains("case 'hvac_on':", content);
        Assert.Contains("digital_write(config.hvac_pin, true);", content);
        Assert.Contains("pwm_write(config.fan_pin, cmd.intensity);", content);
        Assert.Contains("default:", content);
        Assert.Contains("console.log(`Unknown command: ${cmd.action}`);", content);
    }

    [Fact]
    public async Task TS_CanonicalLivingRoomCastFragment_EmitsTypeScriptCastForm()
    {
        const string source = """
            task LivingRoomSensor() @report {
                let aqi = adc_read(config.air_qual_pin) as Float * 0.4885
                log(aqi)
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("Number(adc_read(config.air_qual_pin))", index.Content);
        Assert.Contains("0.4885", index.Content);
        Assert.DoesNotContain(" as Float ", index.Content);
    }

    [Fact]
    public async Task TS_NumericCastPrimitives_EmitRuntimeSafeNumberCoercions()
    {
        const string source = """
            task CastDemo() @report {
                let sample: f32 = f32(1.25)
                let count: u64 = u64(42.9)
                log(sample)
                log(count)
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("let sample: number = Math.fround(Number(1.25));", index.Content);
        Assert.Contains("let count: number = Math.trunc(Number(42.9));", index.Content);
        Assert.DoesNotContain("f32(1.25)", index.Content);
        Assert.DoesNotContain("u64(42.9)", index.Content);
    }

    [Fact]
    public async Task TS_CanonicalSensorKitchenFragment_EmitsConfiguredIntervalAndStructuredPublishTodo()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                celsius: Float
                humidity: Float
                motion: Bool
                air_quality: Float
                timestamp: Text
            }

            task SensorKitchenTask() @report -> stream<SensorReading> {
                config {
                    device_id: Text : "kitchen"
                    dht22_pin: u8 : 4
                    motion_pin: u8 : 6
                    sensor_poll: Duration : 5s
                }

                every config.sensor_poll {
                    let temp = dht_read_celsius(config.dht22_pin)
                    let humidity = dht_read_humidity(config.dht22_pin)
                    let motion = digital_read(config.motion_pin)

                    send SensorReading {
                        device_id: config.device_id,
                        celsius: temp,
                        humidity: humidity,
                        motion: motion,
                        air_quality: 0.0,
                        timestamp: now()
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "report/index.ts")
            .Content;

        Assert.Contains("export async function SensorKitchenTask(): Promise<Stream<SensorReading>> {", content);
        Assert.Contains("}, config.sensor_poll);", content);
        Assert.Contains("let temp = dht_read_celsius(config.dht22_pin);", content);
        Assert.Contains("let motion = digital_read(config.motion_pin);", content);
        Assert.Contains("result.publish({ device_id: config.device_id, celsius: temp, humidity: humidity, motion: motion, air_quality: 0, timestamp: now() });", content);
    }
}
