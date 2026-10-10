using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Semantics;
using JunexisLang.Compiler.Core.Parsing;
using JunexisLang.Compiler.Core.Ast;
using Antlr4.Runtime;

namespace JunexisLang.Compiler.Tests.Semantics;

/// <summary>
/// Unit tests for <see cref="SemanticAnalyzer"/>.
/// </summary>
public sealed class SemanticAnalyzerTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static ProgramNode Parse(string source)
    {
        var inputStream = CharStreams.fromString(source);
        var lexer = new JunexisLangLexer(inputStream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new JunexisLangParser(tokens);
        parser.RemoveErrorListeners();
        var ctx = parser.program();
        return AstBuilder.Build(ctx, tokens, "<test>");
    }

    private static IReadOnlyList<Diagnostic> Analyze(string source)
        => SemanticAnalyzer.Analyze(Parse(source));

    // ── duplicate declarations ─────────────────────────────────────────────────

    [Fact]
    public void Duplicate_Task_EmitsE0001()
    {
        const string src = """
            task Foo() @server { }
            task Foo() @server { }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0001" && d.Message.Contains("Foo"));
    }

    [Fact]
    public void Duplicate_Struct_EmitsE0001()
    {
        const string src = """
            struct Point { x: Float }
            struct Point { y: Float }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0001" && d.Message.Contains("Point"));
    }

    [Fact]
    public void Unique_Declarations_NoDuplicate()
    {
        const string src = """
            struct Point { x: Float }
            task Foo() @server { }
            """;
        var diags = Analyze(src);
        Assert.DoesNotContain(diags, d => d.Code == "E0001");
    }

    // ── undeclared callable (E0020 / Warning) ──────────────────────────────────

    [Fact]
    public void Undeclared_Function_EmitsE0020_Warning()
    {
        const string src = """
            task Worker() @server {
                every 1s {
                    var result = undeclared_fn();
                }
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0020"
            && d.Level == DiagnosticLevel.Warning
            && d.Message.Contains("undeclared_fn"));
    }

    [Fact]
    public void Supported_RecvAny_DoesNotEmitRuntimeWarning()
    {
        const string src = """
            struct MachineState { status: Text }
            struct SafetyEvent { level: Text }

            task Dashboard(states: chan<MachineState>, safety: chan<SafetyEvent>) @report {
                loop {
                    let event = recv_any(states, safety)
                }
            }
            """;
        var diags = Analyze(src);
        Assert.DoesNotContain(diags, d => d.Code == "R0001"
            && d.Message.Contains("recv_any"));
    }

    [Fact]
    public void NumericCastPrimitive_InfersConcreteNumericSubtype()
    {
        const string src = """
            task Worker() @server {
                let value: f32 = f32(1.0)
            }
            """;

        var diags = Analyze(src);

        Assert.DoesNotContain(diags, d => d.Code == "E0020"
            && d.Message.Contains("f32"));
        Assert.DoesNotContain(diags, d => d.Code == "E0044"
            && d.Message.Contains("value"));
    }

    [Fact]
    public void Ternary_WithNumericCastBranches_InfersBranchType()
    {
        const string src = """
            task Worker(flag: Bool) @server {
                let value: f32 = flag ? f32(1.0) : f32(0.0)
            }
            """;

        var diags = Analyze(src);

        Assert.DoesNotContain(diags, d => d.Code == "E0044"
            && d.Message.Contains("value"));
    }

    [Fact]
    public void RecvAny_WithSingleSource_EmitsSemanticError()
    {
        const string src = """
            struct MachineState { status: Text }

            task Dashboard(states: chan<MachineState>) @report {
                loop {
                    let event = recv_any(states)
                }
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0045"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("at least two"));
    }

    [Fact]
    public void RecvAny_WithNonCommunicationSource_EmitsSemanticError()
    {
        const string src = """
            struct MachineState { status: Text }

            task Dashboard(states: chan<MachineState>) @report {
                let plain = 42
                loop {
                    let event = recv_any(states, plain)
                }
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0046"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("recv_any source must be"));
    }

    [Fact]
    public void RecvAny_OnEmbeddedTarget_EmitsRuntimeError()
    {
        const string src = """
            struct MachineState { status: Text }

            task Controller(a: chan<MachineState>, b: chan<MachineState>) @embedded {
                loop {
                    let event = recv_any(a, b)
                }
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "R0002"
            && d.Level == DiagnosticLevel.Error
            && d.Category == "Runtime"
            && d.Message.Contains("recv_any")
            && d.Message.Contains("not supported"));
    }

    [Fact]
    public void Latest_OnStream_InfersElementType()
    {
        const string src = """
            task Dashboard(metrics: stream<Float>) @report {
                let current: Float = latest(metrics)
            }
            """;

        var diags = Analyze(src);

        Assert.DoesNotContain(diags, d => d.Code == "E0020"
            && d.Message.Contains("latest"));
        Assert.DoesNotContain(diags, d => d.Code == "E0044"
            && d.Message.Contains("current"));
        Assert.DoesNotContain(diags, d => d.Code == "R0003"
            && d.Message.Contains("latest"));
        Assert.Contains(diags, d => d.Code == "R0001"
            && d.Message.Contains("latest"));
    }

    [Theory]
    [InlineData("chan<Float>", "chan<Float>")]
    [InlineData("queue<Float>", "queue<Float>")]
    public void Latest_OnNonStreamCommunication_EmitsSemanticError(string sourceType, string expectedType)
    {
        var src = $$"""
            task Dashboard(metrics: {{sourceType}}) @report {
                let current = latest(metrics)
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, d => d.Code == "E0048"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("latest source must be stream<T>")
            && d.Message.Contains(expectedType));
    }

    [Fact]
    public void Latest_OnEmbeddedTarget_EmitsRuntimeError()
    {
        const string src = """
            task Controller(metrics: stream<Float>) @embedded {
                let current = latest(metrics)
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, d => d.Code == "R0002"
            && d.Level == DiagnosticLevel.Error
            && d.Category == "Runtime"
            && d.Message.Contains("latest")
            && d.Message.Contains("not supported"));
    }

    [Fact]
    public void Batch_OnQueue_InfersListOfElementType()
    {
        const string src = """
            struct Job { id: Text }

            task Worker(jobs: queue<Job>) @server {
                let items: List<Job> = batch(jobs, 10)
            }
            """;

        var diags = Analyze(src);

        Assert.DoesNotContain(diags, d => d.Code == "E0044"
            && d.Message.Contains("items"));
        Assert.DoesNotContain(diags, d => d.Code == "R0003"
            && d.Message.Contains("batch"));
        Assert.Contains(diags, d => d.Code == "R0001"
            && d.Message.Contains("batch"));
    }

    [Fact]
    public void Batch_OnStream_EmitsSemanticError()
    {
        const string src = """
            task Dashboard(metrics: stream<Float>) @report {
                let items = batch(metrics, 10)
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, d => d.Code == "E0049"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("batch(stream<T>"));
    }

    [Fact]
    public void Batch_WithNonIntLimit_EmitsSemanticError()
    {
        const string src = """
            struct Job { id: Text }

            task Worker(jobs: queue<Job>) @server {
                let items = batch(jobs, "10")
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, d => d.Code == "E0049"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("batch limit must be Int"));
    }

    [Fact]
    public void RuntimePrimitive_WithoutTargetImplementation_EmitsRuntimeError()
    {
        const string src = """
            task Worker() @server {
                let value = pobierz("temperature")
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, d => d.Code == "R0001"
            && d.Level == DiagnosticLevel.Warning
            && d.Category == "Runtime"
            && d.Message.Contains("pobierz")
            && d.Message.Contains("proposed"));
        Assert.Contains(diags, d => d.Code == "R0003"
            && d.Level == DiagnosticLevel.Error
            && d.Category == "Runtime"
            && d.Message.Contains("pobierz")
            && d.Message.Contains("GoCodegen"));
    }

    [Fact]
    public void RuntimePrimitive_WithTargetImplementation_DoesNotEmitImplementationError()
    {
        const string src = """
            task Worker() @report {
                let value: f32 = f32(1.0)
            }
            """;

        var diags = Analyze(src);

        Assert.DoesNotContain(diags, d => d.Code == "R0003"
            && d.Message.Contains("f32"));
    }

    [Fact]
    public void ProposedAckStatement_ForQueueReceive_EmitsRuntimeWarningWithoutImplementationError()
    {
        const string src = """
            struct Order { id: Text }

            task Worker(orders: queue<Order>) @server {
                let order = recv orders
                ack order
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, d => d.Code == "R0001"
            && d.Level == DiagnosticLevel.Warning
            && d.Category == "Runtime"
            && d.Message.Contains("ack")
            && d.Message.Contains("proposed"));
        Assert.DoesNotContain(diags, d => d.Code == "R0003"
            && d.Message.Contains("ack"));
        Assert.DoesNotContain(diags, d => d.Code == "E0047"
            && d.Message.Contains("ack"));
    }

    [Fact]
    public void AckStatement_ForNonQueueReceiveValue_EmitsSemanticError()
    {
        const string src = """
            task Worker(value: Text) @server {
                ack value
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, d => d.Code == "E0047"
            && d.Level == DiagnosticLevel.Error
            && d.Category == "Semantic"
            && d.Message.Contains("ack target"));
    }

    [Fact]
    public void NackStatement_ForQueueReceive_AllowsReasonExpression()
    {
        const string src = """
            struct Order { id: Text }

            task Worker(orders: queue<Order>) @server {
                let order = recv orders
                nack order reason "retry later"
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, d => d.Code == "R0001"
            && d.Level == DiagnosticLevel.Warning
            && d.Category == "Runtime"
            && d.Message.Contains("nack")
            && d.Message.Contains("proposed"));
        Assert.DoesNotContain(diags, d => d.Code == "E0047"
            && d.Message.Contains("nack"));
    }

    [Fact]
    public void Declared_Task_AsCallee_NoWarning()
    {
        const string src = """
            task Helper() @server -> chan<Int> { }
            task Worker() @server {
                every 1s {
                    var ch = Helper();
                }
            }
            """;
        var diags = Analyze(src);
        Assert.DoesNotContain(diags, d => d.Code == "E0020");
    }

    [Fact]
    public void ExternTask_AsCallee_NoWarning()
    {
        const string src = """
            extern task compute(x: Int) @server
            task Worker() @server {
                every 1s {
                    compute(42);
                }
            }
            """;
        var diags = Analyze(src);
        Assert.DoesNotContain(diags, d => d.Code == "E0020");
    }

    // ── unknown type (E0021 / Warning) ────────────────────────────────────────

    [Fact]
    public void Unknown_Type_In_StateDecl_EmitsE0021_Warning()
    {
        const string src = """
            task Worker() @server {
                state foo: UndeclaredType = 0;
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0021"
            && d.Level == DiagnosticLevel.Warning
            && d.Message.Contains("UndeclaredType"));
    }

    [Fact]
    public void Known_Struct_Type_NoWarning()
    {
        const string src = """
            struct Payload { value: Int }
            task Worker() @server {
                state p: Payload = Payload { value: 0 };
            }
            """;
        var diags = Analyze(src);
        Assert.DoesNotContain(diags, d => d.Code == "E0021");
    }

    [Fact]
    public void Builtin_Types_NoWarning()
    {
        const string src = """
            task Worker() @server {
                state x: Int = 0;
                state y: Float = 0.0;
                state s: Text = "";
                state symbol: Symbol = "EURUSD";
                state started: Time = epoch();
                state timeout: Duration = 5s;
            }
            """;
        var diags = Analyze(src);
        Assert.DoesNotContain(diags, d => d.Code == "E0021");
        Assert.DoesNotContain(diags, d => d.Code == "E0020");
    }

    [Fact]
    public void DeclaredCallable_WithWrongArgumentCount_EmitsE0023()
    {
        const string src = """
            extern task render(id: Text, value: Int) @report
            task Dashboard() @report {
                render("only-one")
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, diagnostic => diagnostic.Code == "E0023"
            && diagnostic.Level == DiagnosticLevel.Error
            && diagnostic.Message.Contains("expected 2..2, got 1"));
    }

    [Fact]
    public void ExternContract_FunctionTypes_AreValidated()
    {
        const string src = """
            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn save(reading: MissingReading)
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, diagnostic => diagnostic.Code == "E0021"
            && diagnostic.Message.Contains("MissingReading"));
    }

    [Fact]
    public void Duplicate_ExternContract_EmitsE0001()
    {
        const string src = """
            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"
            }

            extern storage TemperatureStore {
                target server
                language go
                file "user/other_storage.go"
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, diagnostic => diagnostic.Code == "E0001"
            && diagnostic.Message.Contains("TemperatureStore"));
    }

    [Fact]
    public void ExternContract_WithMatchingExternTasks_HasNoBindingDiagnostic()
    {
        const string src = """
            struct TempReading { celsius: Float }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn save(reading: TempReading)
                fn recent(limit: Int) -> List<TempReading>
            }

            extern task TemperatureStore_save(reading: TempReading) @server
            extern task TemperatureStore_recent(limit: Int) @server -> List<TempReading>
            """;

        var diags = Analyze(src);

        Assert.DoesNotContain(diags, diagnostic => diagnostic.Code is "W0030" or "E0031");
    }

    [Fact]
    public void ExternContract_WithoutMatchingExternTask_EmitsW0030()
    {
        const string src = """
            struct TempReading { celsius: Float }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn save(reading: TempReading)
            }
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, diagnostic => diagnostic.Code == "W0030"
            && diagnostic.Level == DiagnosticLevel.Warning
            && diagnostic.Message.Contains("TemperatureStore_save"));
    }

    [Fact]
    public void ExternContract_WithMismatchedExternTaskSignature_EmitsE0031()
    {
        const string src = """
            struct TempReading { celsius: Float }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn recent(limit: Int) -> List<TempReading>
            }

            extern task TemperatureStore_recent(limit: Text) @report -> List<Text>
            """;

        var diags = Analyze(src);

        Assert.Contains(diags, diagnostic => diagnostic.Code == "E0031"
            && diagnostic.Level == DiagnosticLevel.Error
            && diagnostic.Message.Contains("TemperatureStore.recent")
            && diagnostic.Message.Contains("target expected @server")
            && diagnostic.Message.Contains("parameter 'limit' type expected Int, got Text")
            && diagnostic.Message.Contains("return type expected List<TempReading>, got List<Text>"));
    }

    // ── struct literal with unknown type (E0021) ──────────────────────────────

    [Fact]
    public void StructLiteral_UndeclaredType_EmitsE0021()
    {
        const string src = """
            task Worker() @server {
                every 1s {
                    var x = UndeclaredStruct { field: 1 };
                }
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0021"
            && d.Message.Contains("UndeclaredStruct"));
    }

    [Fact]
    public void StructLiteral_DeclaredType_NoWarning()
    {
        const string src = """
            struct Point { x: Float, y: Float }
            task Worker() @server {
                every 1s {
                    var p = Point { x: 1.0, y: 2.0 };
                }
            }
            """;
        var diags = Analyze(src);
        Assert.DoesNotContain(diags, d => d.Code == "E0021");
    }

    // ── field access on opaque type (W0022) ───────────────────────────────────

    [Fact]
    public void FieldAccess_Directly_On_UndeclaredCall_EmitsW0022()
    {
        // W0022 fires when field access is directly on a call expression
        // e.g. undeclared_fn().field — not on a variable binding
        const string src = """
            task Worker(input: chan<Int>) @server {
                for x in input {
                    if undeclared_fn(x).flag > 0 { }
                }
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "W0022"
            && d.Level == DiagnosticLevel.Warning);
    }

    // ── no false positives on real-world fragment ─────────────────────────────

    [Fact]
    public void Valid_TaskWithForLoop_NoDiagnostics()
    {
        const string src = """
            struct Order { id: Int, amount: Float }
            task OrderProcessor(orders: queue<Order>) @server -> queue<Order> {
                for order in orders {
                    send order;
                }
            }
            """;
        var diags = Analyze(src);
        Assert.Empty(diags);
    }

    [Fact]
    public void Send_ToImplicitCommunicationOutput_TypeChecksValue()
    {
        const string src = """
            task Producer() @server -> chan<Int> {
                send "wrong";
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0042"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("expected Int")
            && d.Message.Contains("got Text"));
    }

    [Fact]
    public void Send_ToExplicitCommunicationTarget_TypeChecksValue()
    {
        const string src = """
            task Worker(outbox: queue<Text>) @server {
                send 42 to outbox;
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0042"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("expected Text")
            && d.Message.Contains("got Int"));
    }

    [Fact]
    public void Send_ToNonCommunicationTarget_EmitsError()
    {
        const string src = """
            task Worker(counter: Int) @server {
                send 42 to counter;
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0040"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("send target must be"));
    }

    [Fact]
    public void Recv_FromNonCommunicationSource_EmitsError()
    {
        const string src = """
            task Worker(counter: Int) @server {
                let item = recv counter;
            }
            """;
        var diags = Analyze(src);
        Assert.Contains(diags, d => d.Code == "E0041"
            && d.Level == DiagnosticLevel.Error
            && d.Message.Contains("recv source must be"));
    }

    [Fact]
    public void For_OverCommunicationSource_InferElementTypeForSend()
    {
        const string src = """
            struct Reading { value: Float }
            task Worker(input: chan<Reading>) @server -> chan<Reading> {
                for reading in input {
                    send reading;
                }
            }
            """;
        var diags = Analyze(src);
        Assert.Empty(diags);
    }

    [Fact]
    public void CanonicalExample_08_EmitsW0020_ForAnalizuj()
    {
        // Minimal reproduction of the 08-hft-trading undeclared-extern pattern
        const string src = """
            struct DaneRynkowe { symbol: Text, cena: Float }
            struct Zlecenie { id: Int }
            task SilnikStrategii(wejscie: chan<DaneRynkowe>) @server -> chan<Zlecenie> {
                for dane in wejscie {
                    var sygnal = analizuj(dane);
                    if sygnal.sila > 0.8 {
                        send generuj_zlecenie(dane, sygnal);
                    }
                }
            }
            """;
        var diags = Analyze(src);
        // analizuj is undeclared → E0020 warning
        Assert.Contains(diags, d => d.Code == "E0020" && d.Message.Contains("analizuj"));

        // Neither should be an Error (should not block compilation)
        Assert.DoesNotContain(diags, d => d.Level == DiagnosticLevel.Error);
    }
}
