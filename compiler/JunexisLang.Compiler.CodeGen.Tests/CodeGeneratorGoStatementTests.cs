namespace JunexisLang.Compiler.CodeGen.Tests;

/// <summary>
/// Testy generowania statementow i send-flow dla GoCodeGenerator.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── GoCodeGenerator: statements i send flow ───────────────────────────

    [Fact]
    public async Task Go_EveryStmt_EmitsNestedBodyStatements()
    {
        const string source = """
            task Tick() @server {
                every 1s {
                    let value: Int = 42
                    log("tick", value)
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("var value int64 = 42", content);
        Assert.Contains("log.Println(\"tick\", value)", content);
        Assert.Contains("case <-junexisTaskContext.Done():", content);
        Assert.Contains("defer ticker.Stop()", content);
        Assert.DoesNotContain("// TODO: every body", content);
    }

    [Fact]
    public async Task Go_TaskBody_EmitsTopLevelStatements()
    {
        const string source = """
            task Process() @server {
                let value: Int = 7
                log(value)
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("var value int64 = 7", content);
        Assert.Contains("log.Println(value)", content);
    }

    [Fact]
    public async Task Go_LoopStmt_EmitsInfiniteLoopBody()
    {
        const string source = """
            task ProcessLoop() @server {
                loop {
                    log("tick")
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("for {", content);
        Assert.Contains("case <-junexisTaskContext.Done():", content);
        Assert.Contains("log.Println(\"tick\")", content);
        Assert.DoesNotContain("Unsupported statement: IRLoopStmt", content);
    }

    [Fact]
    public async Task Go_ChanPipeline_ClosesOutputAndObservesServerShutdown()
    {
        const string source = """
            task Source() @server -> chan<Int> {
                every 1s { send 1 }
            }

            task Sink(values: chan<Int>) @server {
                for value in values { log(value) }
            }

            pipeline Flow {
                use Source -> Sink
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("junexisTaskContext = ctx", content);
        Assert.Contains("defer close(result)", content);
        Assert.Contains("case value, ok := <-values:", content);
        Assert.Contains("if !ok { return }", content);
    }

    [Fact]
    public async Task Go_ForStmt_EmitsRangeLoopBody()
    {
        const string source = """
            task ProcessItems(readings: [Text]) @server {
                for reading in readings {
                    log(reading)
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("for _, reading := range readings {", content);
        Assert.Contains("log.Println(reading)", content);
        Assert.DoesNotContain("Unsupported statement: IRForStmt", content);
    }

    [Fact]
    public async Task Go_IfStmt_EmitsElseIfChain()
    {
        const string source = """
            task Decide(isHot: Bool, isWarm: Bool) @server {
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
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("if isHot {", content);
        Assert.Contains("} else if isWarm {", content);
        Assert.Contains("log.Println(\"cold\")", content);
    }

    [Fact]
    public async Task Go_MatchStmt_EmitsLiteralWildcardAndBodyExprArms()
    {
        const string source = """
            fn enable() -> Bool {
                return true
            }

            task HandleAction(action: Text) @server {
                match action {
                    "on" -> enable(),
                    _ -> {
                        log("unknown")
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("switch __match := action; __match {", content);
        Assert.Contains("case \"on\":", content);
        Assert.Contains("enable()", content);
        Assert.Contains("default:", content);
        Assert.DoesNotContain("Unsupported statement: IRMatchStmt", content);
    }

    [Fact]
    public async Task Go_MatchStmt_EmitsBindingPatternAssignment()
    {
        const string source = """
            task HandleReading(incoming: Text) @server {
                match incoming {
                    reading -> {
                        log(reading)
                    }
                }
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("reading := __match", content);
        Assert.Contains("log.Println(reading)", content);
    }

    [Fact]
    public async Task Go_SendStmt_WithExplicitTarget_EmitsChannelSend()
    {
        const string source = """
            task Publish(out: chan<Text>) @server {
                let reading: Text = "hello"
                send reading to out
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("junexis_send(out, reading)", content);
        Assert.DoesNotContain("_ = out", content);
        Assert.DoesNotContain("_ = reading", content);
    }

    [Fact]
    public async Task Go_SendStmt_WithoutTarget_EmitsExplicitTodoSkeleton()
    {
        const string source = """
            task Publish() @server {
                let reading: Text = "hello"
                send reading
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("// send: no output type declared for this task", content);
        Assert.DoesNotContain("_ = reading", content);
    }

    [Fact]
    public async Task Go_SendStmt_WithoutTarget_OnChanOutput_EmitsResultChannelSend()
    {
        const string source = """
            task Publish() @server -> chan<Int> {
                let reading: Int = 7
                send reading
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("result := make(chan int64)", content);
        Assert.Contains("result <- reading", content);
        Assert.DoesNotContain("// TODO: send reading to task output", content);
    }

    [Fact]
    public async Task Go_SendStmt_WithoutTarget_OnStreamOutput_EmitsSpecificTodo()
    {
        const string source = """
            task Publish() @server -> stream<Int> {
                let reading: Int = 7
                send reading
            }
            """;

        var ir = await TransformToIrAsync(source);
        var gen = new GoCodeGenerator();
        var content = gen.Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("result := NewStream[int64]()", content);
        Assert.Contains("publishResult(reading)", content);
    }
}

