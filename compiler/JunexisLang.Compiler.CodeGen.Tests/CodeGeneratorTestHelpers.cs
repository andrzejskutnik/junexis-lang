namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Bazowy partial z helperami współdzielonymi przez testy generatorów kodu.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    private static async Task<IRDocument> TransformToIrAsync(string source)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, source);
            var result = await new JunexisCompiler().CheckAsync(new CheckOptions(tmp));
            Assert.True(result.Success,
                $"Parse failed:\n{string.Join('\n', result.Diagnostics.Select(d => d.Message))}");
            return new AstToIrTransformer().Transform(result.Ast!);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static IRDocument EmptyDoc() => new(
        "test.jnx",
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        []);

    private static IRTask EmbeddedTask(string name,
        IReadOnlyList<IRParam>? @params = null,
        IRType? outputType = null,
        string variant = "esp32c3",
        IReadOnlyList<IRStateDecl>? states = null) =>
        new(name,
            @params ?? [],
            [new IRPlatform("embedded", [new IRPlatformArg(null, new IRIdentExpr(variant))])],
            outputType,
            states ?? [],
            null, [], []);

    private static IRTask ServerTask(string name,
        IReadOnlyList<IRParam>? @params = null,
        IRType? outputType = null,
        IReadOnlyList<IRStmt>? body = null,
        string? variant = null,
        IReadOnlyList<IRStateDecl>? states = null) =>
        new(name,
            @params ?? [],
            [new IRPlatform("server", variant is null ? [] : [new IRPlatformArg(null, new IRIdentExpr(variant))])],
            outputType,
            states ?? [], null, [], body ?? []);

    private static IRTask ReportTask(string name,
        IReadOnlyList<IRParam>? @params = null,
        IRType? outputType = null,
        IReadOnlyList<IRStmt>? body = null,
        string? variant = null,
        IReadOnlyList<IRStateDecl>? states = null) =>
        new(name,
            @params ?? [],
            [new IRPlatform("report", variant is null ? [] : [new IRPlatformArg(null, new IRIdentExpr(variant))])],
            outputType,
            states ?? [], null, [], body ?? []);

    private static IRDocument DocWith(IRTask task) =>
        EmptyDoc() with { Tasks = [task] };

    private static IRDocument DocWith(
        IRTask[] tasks,
        IRStruct[]? structs = null,
        IREnum[]? enums = null,
        IRPipeline[]? pipelines = null,
        IRQueueBridgeDescriptor[]? queueBridges = null,
        IRStreamBridgeDescriptor[]? streamBridges = null,
        IRChanBridgeDescriptor[]? chanBridges = null) =>
        new("test.jnx",
            [],
            structs ?? [],
            enums ?? [],
            [],
            tasks,
            pipelines ?? [],
            queueBridges ?? [],
            streamBridges ?? [],
            chanBridges ?? [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);

    private static CodeGenOptions Opts => new("out");
}
