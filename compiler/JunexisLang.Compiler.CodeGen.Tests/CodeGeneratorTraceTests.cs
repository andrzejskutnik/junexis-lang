namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>Testy instrumentacji generowanych artefaktów Go i TypeScript.</summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task GoGenerator_CorrelatesChannelSendAndReceiveByPayloadHashAsync()
    {
        const string source = """
            task Produce() @server -> chan<Text> {
                send "hello"
            }

            task Consume(messages: chan<Text>) @server {
                for message in messages {
                    log(message)
                }
            }

            pipeline Flow { use Produce -> Consume }

            narrative Story {
                trace full
                applies_to { Produce, Consume }
            }
            """;

        var document = await TransformToIrAsync(source);
        var main = new GoCodeGenerator().Generate(document, Opts)
            .Single(file => file.FilePath == "server/main.go").Content;

        Assert.Contains("func _junexisPayloadHash(value any) string", main);
        Assert.Contains("sha256.Sum256(payload)", main);
        Assert.Contains("__junexisPayload := \"hello\"", main);
        Assert.Contains("\"COMM_SEND\"", main);
        Assert.Contains("\"payloadHash\":_junexisPayloadHash(__junexisPayload)", main);
        Assert.Contains("\"COMM_RECV\"", main);
        Assert.Contains("\"payloadHash\":_junexisPayloadHash(message)", main);
    }

    [Fact]
    public async Task TypeScriptGenerator_EmitsCanonicalTraceForNarrativeAndSendAsync()
    {
        const string source = """
            task Publish() @report -> stream<Text> {
                send "hello"
            }

            narrative Story {
                trace full
            }
            """;
        var document = await TransformToIrAsync(source);
        var files = new TypeScriptCodeGenerator().Generate(document, Opts);
        var runtime = files.Single(file => file.FilePath == "report/runtime.ts").Content;
        var index = files.Single(file => file.FilePath == "report/index.ts").Content;

        Assert.Contains($"export const traceEventModel = '{TraceRuntimeContract.EventModel}'", runtime);
        Assert.Contains(TraceRuntimeContract.TraceUrlEnvironmentVariable, runtime);
        Assert.Contains(TraceRuntimeContract.BuildIdEnvironmentVariable, runtime);
        Assert.Contains(TraceRuntimeContract.BundleIdEnvironmentVariable, runtime);
        Assert.Contains(TraceRuntimeContract.BuildIdAttribute, runtime);
        Assert.Contains(TraceRuntimeContract.BundleIdAttribute, runtime);
        Assert.Contains($"`${{traceUrl}}{TraceRuntimeContract.EventsPath}`", runtime);
        Assert.Contains($"`${{traceUrl}}{TraceRuntimeContract.DebugSessionPath}`", runtime);
        Assert.Contains("Queue, Stream, recv_any, emitTrace", index);
        Assert.Contains($"emitTrace('{TraceRuntimeContract.CommunicationSend}'", index);
        Assert.Contains($"{TraceRuntimeContract.NarrativeIdAttribute}: 'Story'", index);
        Assert.Contains($"{TraceRuntimeContract.PayloadTypeAttribute}: 'Text'", index);
        Assert.Contains("result.publish(", index);
    }

    [Fact]
    public async Task CppGenerator_EmitsCanonicalTraceRuntimeAndEsp32TransportAsync()
    {
        const string source = """
            task Publish() @embedded -> stream<Text> {
                send "hello"
            }

            narrative Story {
                trace full
            }
            """;
        var document = await TransformToIrAsync(source);
        var files = new CppCodeGenerator().Generate(document, Opts);
        var runtime = files.Single(file => file.FilePath == "embedded/junexis/runtime.h").Content;
        var transport = files.Single(file => file.FilePath == "embedded/junexis_trace.cpp").Content;
        var implementation = files.Single(file => file.FilePath == "embedded/Publish.cpp").Content;
        var main = files.Single(file => file.FilePath == "embedded/junexis_app.cpp").Content;
        var platformIo = files.Single(file => file.FilePath == "embedded/platformio.ini").Content;

        Assert.Contains($"trace_event_model = \"{TraceRuntimeContract.EventModel}\"", runtime);
        Assert.Contains($"trace_url_setting = \"{TraceRuntimeContract.TraceUrlEnvironmentVariable}\"", runtime);
        Assert.Contains($"trace_bundle_id_setting = \"{TraceRuntimeContract.BundleIdEnvironmentVariable}\"", runtime);
        Assert.Contains("std::string bundle_id", runtime);
        Assert.Contains("struct TraceEvent", runtime);
        Assert.Contains("set_trace_sink", runtime);
        Assert.Contains($"defined({TraceRuntimeContract.TraceUrlEnvironmentVariable})", transport);
        Assert.Contains($"\"{TraceRuntimeContract.EventsPath}\"", transport);
        Assert.Contains($"\"{TraceRuntimeContract.DebugSessionPath}\"", transport);
        Assert.Contains($"attributes[\"{TraceRuntimeContract.BundleIdAttribute}\"]", transport);
        Assert.Contains($"junexis::emit_trace(\"{TraceRuntimeContract.CommunicationSend}\"", implementation);
        Assert.Contains("\"Story\"", implementation);
        Assert.Contains("\"Text\"", implementation);
        Assert.Contains("junexis::install_default_trace_transport();", main);
        Assert.Contains("bblanchon/ArduinoJson@^6.21.5", platformIo);
    }

    [Fact]
    public async Task Generators_UseGlobalIrOrderForCrossPlatformStartupCorrelationAsync()
    {
        const string source = """
            task Device() @embedded {}
            task Service() @server {}
            task Dashboard() @report {}

            narrative RuntimePath {
                trace full
            }
            """;
        var document = await TransformToIrAsync(source);
        var cpp = new CppCodeGenerator().Generate(document, Opts)
            .Single(file => file.FilePath == "embedded/Device.cpp").Content;
        var go = new GoCodeGenerator().Generate(document, Opts)
            .Single(file => file.FilePath == "server/main.go").Content;
        var typeScript = new TypeScriptCodeGenerator().Generate(document, Opts)
            .Single(file => file.FilePath == "report/index.ts").Content;

        Assert.Contains("\"startup-0\", \"task-start\"", cpp);
        Assert.Contains("\"payloadHash\":\"startup-0\"", go);
        Assert.Contains("\"payloadHash\":\"startup-1\"", go);
        Assert.Contains("healthPort := flag.Int(\"health-port\"", go);
        Assert.Contains("payloadHash: 'startup-1'", typeScript);
        Assert.Contains("payloadHash: 'startup-2'", typeScript);
    }

    [Theory]
    [InlineData("trace off")]
    [InlineData("trace none")]
    public async Task Generators_RespectExplicitTraceOptOutAsync(string policy)
    {
        var source = $$"""
            task Report() @report { log("ok") }
            task Server() @server { log("ok") }
            task Device() @embedded { log("ok") }
            narrative Story {
                {{policy}}
            }
            """;
        var document = await TransformToIrAsync(source);
        var typeScriptFiles = new TypeScriptCodeGenerator().Generate(document, Opts);
        var runtime = typeScriptFiles.Single(file => file.FilePath == "report/runtime.ts").Content;
        var index = typeScriptFiles.Single(file => file.FilePath == "report/index.ts").Content;
        var go = new GoCodeGenerator().Generate(document, Opts)
            .Single(file => file.FilePath == "server/main.go").Content;
        var cppFiles = new CppCodeGenerator().Generate(document, Opts);
        var cppRuntime = cppFiles.Single(file => file.FilePath == "embedded/junexis/runtime.h").Content;
        var cppImplementation = cppFiles.Single(file => file.FilePath == "embedded/Device.cpp").Content;

        Assert.DoesNotContain("export async function emitTrace", runtime);
        Assert.DoesNotContain("emitTrace", index);
        Assert.DoesNotContain("_emitTrace", go);
        Assert.DoesNotContain(TraceRuntimeContract.TraceUrlEnvironmentVariable, go);
        Assert.DoesNotContain("TraceEvent", cppRuntime);
        Assert.DoesNotContain("emit_trace", cppImplementation);
        Assert.DoesNotContain(cppFiles, file => file.FilePath == "embedded/junexis_trace.cpp");
    }

    [Fact]
    public async Task Generators_WithoutNarrative_DoNotEmitTraceRuntimeAsync()
    {
        var document = await TransformToIrAsync("""
            task Report() @report { log("ok") }
            task Server() @server { log("ok") }
            task Device() @embedded { log("ok") }
            """);

        var typeScriptFiles = new TypeScriptCodeGenerator().Generate(document, Opts);
        var runtime = typeScriptFiles.Single(file => file.FilePath == "report/runtime.ts").Content;
        var index = typeScriptFiles.Single(file => file.FilePath == "report/index.ts").Content;
        var go = new GoCodeGenerator().Generate(document, Opts)
            .Single(file => file.FilePath == "server/main.go").Content;
        var cppFiles = new CppCodeGenerator().Generate(document, Opts);

        Assert.DoesNotContain("export async function emitTrace", runtime);
        Assert.DoesNotContain("emitTrace", index);
        Assert.DoesNotContain("_emitTrace", go);
        Assert.DoesNotContain("TraceEvent", cppFiles.Single(file =>
            file.FilePath == "embedded/junexis/runtime.h").Content);
        Assert.DoesNotContain(cppFiles, file => file.FilePath == "embedded/junexis_trace.cpp");
    }
}
