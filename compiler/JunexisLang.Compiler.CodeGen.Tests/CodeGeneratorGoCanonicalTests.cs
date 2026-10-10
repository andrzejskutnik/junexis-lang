namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>
/// Kanoniczne scenariusze regresyjne GoCodeGenerator dla sterowania, sensorow i logiki przeplywu.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── GoCodeGenerator: scenariusze kanoniczne ────────────────────────────

    [Fact]
    public async Task Go_CanonicalSensorAggregatorFragment_GeneratesExpectedSkeleton()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
            }

            task SensorAggregator(
                kitchen: stream<SensorReading>,
                bedroom: stream<SensorReading>,
                living_room: stream<SensorReading>
            ) @server -> stream<SensorReading> {
                loop {
                    match recv_any(kitchen, bedroom, living_room) {
                        reading -> { send reading; }
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)[0].Content;

        Assert.Contains("func SensorAggregator(kitchen *Stream[SensorReading], bedroom *Stream[SensorReading], living_room *Stream[SensorReading]) *Stream[SensorReading]", content);
        Assert.Contains("for {", content);
        Assert.Contains("switch __match := recv_any(kitchen, bedroom, living_room); __match {", content);
        Assert.Contains("reading := __match", content);
        Assert.Contains("result := NewStream[SensorReading]()", content);
        Assert.Contains("publishResult(reading)", content);
    }

    [Fact]
    public async Task Go_CanonicalCommandRoutingFragment_PreservesFieldAccessAndWildcardArm()
    {
        const string source = """
            struct DeviceCommand {
                target: Text
                action: Text
                intensity: Float
            }

            task ApplyCommands(commands: [DeviceCommand]) @server {
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
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("for _, cmd := range commands {", content);
        Assert.Contains("if (cmd.Target == \"kitchen\") {", content);
        Assert.Contains("switch __match := cmd.Action; __match {", content);
        Assert.Contains("case \"hvac_on\":", content);
        Assert.Contains("digital_write(config.Hvac_pin, true)", content);
        Assert.Contains("pwm_write(config.Fan_pin, cmd.Intensity)", content);
        Assert.Contains("default:", content);
        Assert.Contains("log.Println(fmt.Sprintf(\"Unknown command: %v\", cmd.Action))", content);
    }

    [Fact]
    public async Task Go_CanonicalCooldownGuardFragment_EmitsBooleanNegationOnCall()
    {
        const string source = """
            struct SensorReading {
                celsius: Float
            }

            task BuildAlerts(r: SensorReading) @server {
                if r.celsius > config.temp_high {
                    if not is_on_cooldown("temp_high") {
                        set_cooldown("temp_high", 10m)
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("if (r.Celsius > config.Temp_high) {", content);
        Assert.Contains("if (!is_on_cooldown(\"temp_high\")) {", content);
        Assert.Contains("set_cooldown(\"temp_high\", 10 * time.Minute)", content);
    }

    [Fact]
    public async Task Go_CanonicalMotionGuardFragment_MapsLogicalOperatorsToGoSyntax()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                motion: Bool
            }

            task BuildAlerts(r: SensorReading) @server {
                if r.motion and (r.device_id == config.motion_zone_1 or r.device_id == config.motion_zone_2) {
                    log("Motion detected in {r.device_id}")
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("if (r.Motion && ((r.Device_id == config.Motion_zone_1) || (r.Device_id == config.Motion_zone_2))) {", content);
        Assert.Contains("log.Println(fmt.Sprintf(\"Motion detected in %v\", r.Device_id))", content);
    }

    [Fact]
    public async Task Go_CanonicalHvacHeatingGuardFragment_EmitsReducedIntensityAndStateAssignment()
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

            task HVACController(r: SensorReading) @server -> chan<DeviceCommand> {
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
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("result := make(chan DeviceCommand)", content);
        Assert.Contains("needs_heating := (r.Celsius < (config.Target_temp - config.Hysteresis))", content);
        Assert.Contains("if (needs_heating && (!state.Hvac_on)) {", content);
        Assert.Contains("result <- DeviceCommand{Target: r.Device_id, Action: \"hvac_on\", Intensity: 0.5}", content);
        Assert.Contains("state.Hvac_on = true", content);
        Assert.Contains("log.Println(fmt.Sprintf(\"HVAC HEAT ON: %v temp=%.1f\", r.Device_id, r.Celsius))", content);
    }

    [Fact]
    public async Task Go_CanonicalHealthMonitorFragment_EmitsStructuredPayloadAndUptimeDelta()
    {
        const string source = """
            struct SystemHealth {
                devices_online: Int
                devices_offline: Int
                avg_temp: Float
                alert_count: Int
                uptime: Time
            }

            extern task count_online_devices() @server -> Int
            extern task count_total_devices() @server -> Int
            extern task get_avg_temp() @server -> Float

            task HealthMonitor() @server -> stream<SystemHealth> {
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
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("func HealthMonitor() *Stream[SystemHealth] {", content);
        Assert.Contains("result := NewStream[SystemHealth]()", content);
        Assert.Contains("offline := (count_total_devices() - online)", content);
        Assert.Contains("publishResult(SystemHealth{Devices_online: online, Devices_offline: offline, Avg_temp: get_avg_temp(), Alert_count: state.Alert_count, Uptime: now().Sub(state.Start_time)})", content);
        Assert.Contains("var count_online_devices = func() int64", content);
    }

    [Fact]
    public async Task Go_CanonicalLivingRoomCastFragment_EmitsGoCastForm()
    {
        const string source = """
            task LivingRoomSensor() @server {
                let aqi = adc_read(config.air_qual_pin) as Float * 0.4885
                log(aqi)
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("float64(adc_read(config.Air_qual_pin))", content);
        Assert.Contains("0.4885", content);
        Assert.DoesNotContain(" as Float ", content);
    }

    [Fact]
    public async Task Go_CanonicalSensorKitchenFragment_EmitsConfiguredTickerAndStructuredPublishTodo()
    {
        const string source = """
            struct SensorReading {
                device_id: Text
                celsius: Float
                humidity: Float
                motion: Bool
                air_quality: Float
                timestamp: Time
            }

            extern task dht_read_celsius(pin: u8) @server -> Float
            extern task dht_read_humidity(pin: u8) @server -> Float
            extern task digital_read(pin: u8) @server -> Bool

            task SensorKitchenTask() @server -> stream<SensorReading> {
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
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("func SensorKitchenTask() *Stream[SensorReading] {", content);
        Assert.Contains("result := NewStream[SensorReading]()", content);
        Assert.Contains("ticker := time.NewTicker(config.Sensor_poll)", content);
        Assert.Contains("temp := dht_read_celsius(config.Dht22_pin)", content);
        Assert.Contains("motion := digital_read(config.Motion_pin)", content);
        Assert.Contains("publishResult(SensorReading{Device_id: config.Device_id, Celsius: temp, Humidity: humidity, Motion: motion, Air_quality: 0, Timestamp: now()})", content);
        Assert.Contains("var dht_read_celsius = func(pin uint8) float64", content);
    }
}

