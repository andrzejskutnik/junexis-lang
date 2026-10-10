namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>
/// Kanoniczne scenariusze regresyjne CppCodeGenerator dla tasku @embedded.
/// Każdy test weryfikuje jeden kluczowy aspekt generowanego kodu C++.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── CppCodeGenerator: scenariusze kanoniczne ───────────────────────────

    [Fact]
    public async Task Cpp_CanonicalEveryLoopAndSend_GeneratesWhileTrueWithVTaskDelay()
    {
        const string source = """
            struct SensorReading {
                celsius: Float
                device_id: Text
            }

            task TempSensor() @embedded -> stream<SensorReading> {
                config {
                    dht_pin: u8 : 4
                    device_id: Text : "sensor_01"
                    poll_ms: u32 : 1000
                }

                every config.poll_ms {
                    let temp = dht_read_celsius(config.dht_pin);
                    send SensorReading { celsius: temp, device_id: config.device_id };
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new CppCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "embedded/TempSensor.cpp")
            .Content;

        Assert.Contains("while (true) {", content);
        Assert.Contains("auto temp = dht_read_celsius(config.dht_pin);", content);
        Assert.Contains("SensorReading{.celsius = temp, .device_id = config.device_id}", content);
        Assert.Contains("vTaskDelay(pdMS_TO_TICKS(config.poll_ms));", content);
    }

    [Fact]
    public async Task Cpp_CanonicalStructLiteralDesignatedInitializers_AllFieldsEmitted()
    {
        const string source = """
            struct DeviceStatus {
                device_id: Text
                online: Bool
                value: Float
            }

            task StatusReporter(id: Text, val: Float) @embedded -> chan<DeviceStatus> {
                send DeviceStatus { device_id: id, online: true, value: val };
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new CppCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "embedded/StatusReporter.cpp")
            .Content;

        // C++20 designated initializers
        Assert.Contains("DeviceStatus{.device_id = id, .online = true, .value = val}", content);
    }

    [Fact]
    public async Task Cpp_OutputTask_EmitsFallbackReturnValue()
    {
        const string source = """
            task Source() @embedded -> chan<Int> {
                send 1
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new CppCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "embedded/Source.cpp")
            .Content;

        Assert.Contains("junexis::Channel<int32_t> task_Source()", content);
        Assert.Contains("return {};", content);
    }

    [Fact]
    public async Task Cpp_NumericCastPrimitives_EmitStaticCasts()
    {
        const string source = """
            task CastDemo() @embedded {
                let sample: f32 = f32(1.25)
                let count: u64 = u64(42.9)
                log(sample)
                log(count)
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new CppCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "embedded/CastDemo.cpp")
            .Content;

        Assert.Contains("auto sample = static_cast<float>(", content);
        Assert.Contains("auto count = static_cast<uint64_t>(", content);
        Assert.DoesNotContain("f32(1.25)", content);
        Assert.DoesNotContain("u64(42.9)", content);
    }

    [Fact]
    public async Task Cpp_CanonicalMatchOnString_GeneratesIfElseChain()
    {
        const string source = """
            task CommandHandler(action: Text) @embedded {
                config {
                    pin: u8 : 5
                }

                match action {
                    "on"  -> digital_write(config.pin, true)
                    "off" -> digital_write(config.pin, false)
                    _     -> { log("Unknown: {action}") }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new CppCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "embedded/CommandHandler.cpp")
            .Content;

        // C++ has no string switch — must use if/else if chain
        Assert.Contains("const auto __match = action;", content);
        Assert.Contains("if (__match == \"on\") {", content);
        Assert.Contains("else if (__match == \"off\") {", content);
        Assert.Contains("else {", content);
        Assert.Contains("junexis_to_string(action)", content);
        Assert.Contains("Unknown:", content);
    }

    [Fact]
    public async Task Cpp_CanonicalIfElseGuard_GeneratesNativeCppConditional()
    {
        const string source = """
            struct Reading {
                celsius: Float
                motion: Bool
            }

            task AlertTask(r: Reading) @embedded {
                config {
                    threshold: Float : 28.0
                }

                if r.celsius > config.threshold {
                    log("Hot: {r.celsius:.1f}")
                } else if r.motion {
                    log("Motion detected")
                } else {
                    log("Normal")
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new CppCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "embedded/AlertTask.cpp")
            .Content;

        Assert.Contains("if ((r.celsius > config.threshold)) {", content);
        Assert.Contains("else if (r.motion) {", content);
        Assert.Contains("else {", content);
        Assert.Contains("String(r.celsius, 1)", content);
        Assert.Contains("Hot:", content);
        Assert.DoesNotContain(".toFixed(", content);
    }

    [Fact]
    public async Task Cpp_CanonicalConfigAccess_GeneratesDotNotation()
    {
        const string source = """
            task PinBlink() @embedded {
                config {
                    led_pin: u8 : 13
                    blink_ms: u32 : 500
                }

                every config.blink_ms {
                    digital_write(config.led_pin, true);
                    digital_write(config.led_pin, false);
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new CppCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "embedded/PinBlink.cpp")
            .Content;

        Assert.Contains("digital_write(config.led_pin, true)", content);
        Assert.Contains("digital_write(config.led_pin, false)", content);
        Assert.Contains("vTaskDelay(pdMS_TO_TICKS(config.blink_ms));", content);
    }
}
