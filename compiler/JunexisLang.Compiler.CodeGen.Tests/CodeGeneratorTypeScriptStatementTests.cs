namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Testy instrukcji i przeplywu danych TypeScriptCodeGenerator.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: instrukcje i send-flow ────────────────────

    [Fact]
    public async Task TS_EveryStmt_EmitsNestedBodyStatements()
    {
        const string source = """
            task Tick() @report {
                every 1s {
                    let value: Int = 42
                    log("tick", value)
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("let value: number = 42;", index.Content);
        Assert.Contains("console.log('tick', value);", index.Content);
        Assert.DoesNotContain("// TODO: every body", index.Content);
    }

    [Fact]
    public async Task TS_TaskBody_EmitsTopLevelStatements()
    {
        const string source = """
            task Process() @report {
                let value: Int = 7
                log(value)
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("let value: number = 7;", index.Content);
        Assert.Contains("console.log(value);", index.Content);
        Assert.DoesNotContain("// TODO: implement Process", index.Content);
    }

    [Fact]
    public async Task TS_LoopStmt_EmitsInfiniteLoopBody()
    {
        const string source = """
            task ProcessLoop() @report {
                loop {
                    log("tick")
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("while (true) {", index.Content);
        Assert.Contains("console.log('tick');", index.Content);
        Assert.DoesNotContain("Unsupported statement: IRLoopStmt", index.Content);
    }

    [Fact]
    public async Task TS_ForStmt_EmitsAsyncForOfBody()
    {
        const string source = """
            task ProcessItems(readings: [Text]) @report {
                for reading in readings {
                    log(reading)
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("for await (const reading of readings) {", index.Content);
        Assert.Contains("console.log(reading);", index.Content);
        Assert.DoesNotContain("Unsupported statement: IRForStmt", index.Content);
    }

    [Fact]
    public async Task TS_IfStmt_EmitsElseIfChain()
    {
        const string source = """
            task Decide(isHot: Bool, isWarm: Bool) @report {
                if isHot {
                    log("hot")
                } else if isWarm {
                    log("warm")
                } else {
                    log("cold")
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("if (isHot) {", index.Content);
        Assert.Contains("} else if (isWarm) {", index.Content);
        Assert.Contains("console.log('cold');", index.Content);
    }

    [Fact]
    public async Task TS_MatchStmt_EmitsLiteralWildcardAndBodyExprArms()
    {
        const string source = """
            fn enable() -> Bool {
                return true
            }

            task HandleAction(action: Text) @report {
                match action {
                    "on" -> enable(),
                    _ -> {
                        log("unknown")
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("const __match = action;", index.Content);
        Assert.Contains("case 'on':", index.Content);
        Assert.Contains("enable();", index.Content);
        Assert.Contains("default:", index.Content);
        Assert.DoesNotContain("Unsupported statement: IRMatchStmt", index.Content);
    }

    [Fact]
    public async Task TS_MatchStmt_EmitsBindingPatternAssignment()
    {
        const string source = """
            task HandleReading(incoming: Text) @report {
                match incoming {
                    reading -> {
                        log(reading)
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("const reading = __match;", index.Content);
        Assert.Contains("console.log(reading);", index.Content);
    }

    [Fact]
    public async Task TS_SendStmt_WithExplicitTarget_EmitsRuntimeSend()
    {
        const string source = """
            task Publish(out: chan<Text>) @report {
                let reading: Text = "hello"
                send reading to out
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("await junexis.send(out, reading);", index.Content);
        Assert.DoesNotContain("void out;", index.Content);
        Assert.DoesNotContain("void reading;", index.Content);
    }

    [Fact]
    public async Task TS_SendStmt_WithoutTarget_EmitsExplicitTodoSkeleton()
    {
        const string source = """
            task Publish() @report {
                let reading: Text = "hello"
                send reading
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("// send: no output type declared for this task", index.Content);
        Assert.DoesNotContain("void reading;", index.Content);
    }

    [Fact]
    public async Task TS_SendStmt_WithoutTarget_ForChanOutput_BuffersGeneratedResult()
    {
        const string source = """
            task Publish() @report -> chan<Text> {
                let reading: Text = "hello"
                send reading
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new TypeScriptCodeGenerator();
        var index = gen.Generate(ir, Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("const result: Channel<string> = new Channel<string>(1);", index.Content);
        Assert.Contains("await result.send(reading);", index.Content);
        Assert.DoesNotContain("// TODO: send reading to chan output result", index.Content);
    }
}
