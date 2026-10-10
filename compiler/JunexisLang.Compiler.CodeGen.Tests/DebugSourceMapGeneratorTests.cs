using System.Text.Json;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed class DebugSourceMapGeneratorTests
{
    [Fact]
    public void Generate_MapsTaskToEachGeneratedBackendFunctionRange()
    {
        var source = Path.GetFullPath("source.jnx");
        var task = new IRTask(
            "ReadSensor",
            [],
            [new IRPlatform("embedded", []), new IRPlatform("server", []), new IRPlatform("report", [])],
            new IRCommType(IRCommKind.Chan, new IRNamedType("Text")),
            [],
            null,
            [],
            [new IRSendStmt(new IRLiteralExpr(new IRTextValue("reading")), null)]);
        var document = new IRDocument(
            source,
            Imports: [],
            Structs: [],
            Enums: [],
            Functions: [],
            Tasks: [task],
            Pipelines: [],
            QueueBridges: [],
            StreamBridges: [],
            ChanBridges: [],
            Devices: [],
            Narratives: [new IRNarrative("SourceMapNarrative", "")],
            Deployments: [],
            Supervisors: [],
            Quirks: [],
            CircuitBreakers: [],
            Protocols: [],
            ExternTasks: [],
            ExternTypes: [],
            ExternContracts: [])
        {
            SourceMap =
            [
                new IRSourceMapEntry("/Tasks[0]", new IRSourceSpan(source, 12, 5)),
                new IRSourceMapEntry("/Tasks[0]/Body[0]", new IRSourceSpan(source, 13, 3)),
            ]
        };
        GeneratedFile[] files =
        [
            new("embedded", "embedded/ReadSensor.cpp", "int task_ReadSensor() {\n  return 1;\n}\n"),
            new("server", "server/main.go", "func ReadSensor() int {\n\t// junexis:source /Tasks[0]/Body[0]\n\tlog.Println(\"reading\")\n\t// junexis:source /Tasks[0]/Body[0]\n\ttraceReading()\n\treturn 1\n}\n"),
            new("report", "report/index.ts", "export async function ReadSensor(): Promise<number> {\n  return 1;\n}\n"),
        ];

        var result = DebugSourceMapGenerator.Generate(document, files);
        using var json = JsonDocument.Parse(result.Content);
        var mappings = json.RootElement.GetProperty("mappings").EnumerateArray().ToArray();

        Assert.Equal("debug/junexis.source-map.json", result.FilePath);
        Assert.Equal("1.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(5, mappings.Length);
        var taskMappings = mappings.Where(mapping => mapping.GetProperty("kind").GetString() == "task").ToArray();
        Assert.Equal(3, taskMappings.Length);
        Assert.All(taskMappings, mapping =>
        {
            Assert.Equal("task:ReadSensor", mapping.GetProperty("symbolId").GetString());
            Assert.Equal(12, mapping.GetProperty("source").GetProperty("line").GetInt32());
            Assert.Equal(1, mapping.GetProperty("generated").GetProperty("startLine").GetInt32());
            Assert.True(mapping.GetProperty("generated").GetProperty("endLine").GetInt32() >= 3);
        });
        var statements = mappings.Where(mapping => mapping.GetProperty("kind").GetString() == "statement").ToArray();
        Assert.Equal(2, statements.Length);
        Assert.All(statements, statement =>
        {
            Assert.Equal("/Tasks[0]/Body[0]", statement.GetProperty("irPath").GetString());
            Assert.Equal(13, statement.GetProperty("source").GetProperty("line").GetInt32());
        });
        Assert.Equal([3, 5], statements.Select(statement =>
            statement.GetProperty("generated").GetProperty("startLine").GetInt32()).ToArray());
        Assert.Equal([2, 2], statements.Select(statement =>
            statement.GetProperty("generated").GetProperty("startColumn").GetInt32()).ToArray());

        var backendFiles = CodeGeneratorFactory.Get(["embedded", "server", "report"])
            .SelectMany(generator => generator.Generate(document, new CodeGenOptions(Path.GetFullPath("out"))))
            .ToArray();
        var backendMap = DebugSourceMapGenerator.Generate(document, backendFiles);
        using var backendJson = JsonDocument.Parse(backendMap.Content);
        var backendStatements = backendJson.RootElement.GetProperty("mappings").EnumerateArray()
            .Where(mapping => mapping.GetProperty("kind").GetString() == "statement")
            .ToArray();
        Assert.Equal(4, backendStatements.Length);
        Assert.Equal<string>(
            ["embedded", "report", "server", "server"],
            backendStatements.Select(mapping => mapping.GetProperty("generated").GetProperty("target").GetString()!)
                .OrderBy(target => target, StringComparer.Ordinal)
                .ToArray());
    }
}
