namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

public sealed class DebugManifestGeneratorTests
{
    [Fact]
    public void Generate_MapsJunexisSystemConceptsToDebuggerManifest()
    {
        var document = CreateDocument();

        var file = DebugManifestGenerator.Generate(document);

        Assert.Equal("debug", file.Platform);
        Assert.Equal("debug/junexis.debug.toml", file.FilePath);
        Assert.Contains("kind = \"junexis.debug-manifest\"", file.Content);
        Assert.Contains("traceCollectorEventModel = \"junexis.tracecollector.v1\"", file.Content);
        Assert.Contains("taskIdStrategy = \"1-based IR task order\"", file.Content);
        Assert.Contains("symbolId = \"task:", file.Content);
        Assert.Contains("messageCorrelationAttribute = \"payloadHash\"", file.Content);
        Assert.Contains("narrativeAttribute = \"narrative_id\"", file.Content);
        Assert.Contains("id = 1", file.Content);
        Assert.Contains("name = \"OperatorPanel\"", file.Content);
        Assert.Contains("targets = [\"report\"]", file.Content);
        Assert.Contains("id = 2", file.Content);
        Assert.Contains("name = \"MotionController\"", file.Content);
        Assert.Contains("targets = [\"embedded\"]", file.Content);
        Assert.Contains("pipeline = \"CncDemo\"", file.Content);
        Assert.Contains("sourceTask = \"OperatorPanel\"", file.Content);
        Assert.Contains("targetTask = \"MotionController\"", file.Content);
        Assert.Contains("[[communications]]", file.Content);
        Assert.Contains("id = \"communication:CncDemo:OperatorPanel->MotionController\"", file.Content);
        Assert.Contains("kind = \"chan\"", file.Content);
        Assert.Contains("sourceSymbolId = \"task:OperatorPanel\"", file.Content);
        Assert.Contains("targetSymbolId = \"task:MotionController\"", file.Content);
        Assert.Contains("role = \"motion\"", file.Content);
        Assert.Contains("name = \"MotionDriver\"", file.Content);
        Assert.Contains("userFile = \"user/embedded/motion_driver.cpp\"", file.Content);
        Assert.Contains("label = \"CncSafetyStory\"", file.Content);
        Assert.Contains("sourceLine = 10", file.Content);
    }

    private static IRDocument CreateDocument()
    {
        var report = new IRPlatform("report", []);
        var embedded = new IRPlatform("embedded", [new IRPlatformArg(null, new IRLiteralExpr(new IRTextValue("esp32c3")))]);

        return new IRDocument(
            "src/main.jnx",
            Imports: [],
            Structs: [],
            Enums: [],
            Functions: [],
            Tasks:
            [
                new IRTask("OperatorPanel", [], [report], new IRCommType(IRCommKind.Chan, new IRNamedType("MotionCommand")), [], null, [], []),
                new IRTask("MotionController", [new IRParam("commands", new IRCommType(IRCommKind.Chan, new IRNamedType("MotionCommand")), null)], [embedded], null, [], null, [], [])
            ],
            Pipelines:
            [
                new IRPipeline("CncDemo", [new IRUseConn("OperatorPanel", null, "MotionController", null)])
            ],
            QueueBridges: [],
            StreamBridges: [],
            ChanBridges: [],
            Devices:
            [
                new IRDevice("cnc_controller", embedded, null, [], null)
            ],
            Narratives:
            [
                new IRNarrative("CncSafetyStory", "intent \"Show CNC debugging story\"")
            ],
            Deployments: [],
            Supervisors: [],
            Quirks: [],
            CircuitBreakers: [],
            Protocols: [],
            ExternTasks: [],
            ExternTypes: [],
            ExternContracts:
            [
                new IRExternContract(
                    "motion",
                    "MotionDriver",
                    "embedded",
                    "cpp",
                    "user/embedded/motion_driver.cpp",
                    [new IRExternContractFunction("move_linear", [new IRParam("cmd", new IRNamedType("MotionCommand"), null)], null)])
            ])
        {
            SourceMap =
            [
                new IRSourceMapEntry("/Tasks[0]", new IRSourceSpan("src/main.jnx", 10, 1)),
                new IRSourceMapEntry("/Tasks[1]", new IRSourceSpan("src/main.jnx", 20, 1)),
                new IRSourceMapEntry("/Pipelines[0]", new IRSourceSpan("src/main.jnx", 30, 1)),
                new IRSourceMapEntry("/Devices[0]", new IRSourceSpan("src/main.jnx", 40, 1)),
                new IRSourceMapEntry("/ExternContracts[0]", new IRSourceSpan("src/main.jnx", 50, 1)),
                new IRSourceMapEntry("/Narratives[0]", new IRSourceSpan("src/main.jnx", 60, 1))
            ]
        };
    }
}
