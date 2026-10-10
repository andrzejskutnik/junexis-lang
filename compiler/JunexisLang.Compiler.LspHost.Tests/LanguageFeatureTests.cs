using JunexisLang.Compiler.LspHost.Handlers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace JunexisLang.Compiler.LspHost.Tests;

public sealed class LanguageFeatureTests
{
    private const string Uri = "file:///m5-features.jnx";
    private const string Source = """
        struct Payload { value: Int }
        extern storage PayloadStore {
            fn save(value: Payload)
        }
        task Worker(input: Payload) @server { }
        struct MotionCommand { job_id: Text }
        struct MachinePosition { x: Float }
        extern task read_position() @embedded -> MachinePosition
        extern task limit_switch_active() @embedded -> Bool
        task Controller(commands: chan<MotionCommand>) @embedded {
            for cmd in commands {
                let pos = read_position()
                let limit_hit = limit_switch_active()
                let z_ok = pos.x <= 5.0
                log(cmd.job_id)
                log(pos)
                log(limit_hit)
                log(z_ok)
            }
        }
        """;

    [Fact]
    public async Task Hover_DescribesReferencedDeclaration()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(4, 22)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("struct Payload", result.Contents.ToString());
    }

    [Fact]
    public async Task Hover_DescribesLanguageKeyword()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(4, 1)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Defines what the system does", result.Contents.ToString());
    }

    [Fact]
    public async Task Hover_DescribesBuiltInType()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(0, 24)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Built-in integer", result.Contents.ToString());
    }

    [Theory]
    [InlineData(9, 30, "chan", "Backpressured", "ProducerWaits", "UntilConsumed", "Fifo")]
    [InlineData(0, 0, "queue", "AtLeastOnce", "ProducerContinues", "UntilAck", "Fifo")]
    [InlineData(0, 0, "stream", "LatestWins", "ProducerContinues", "LatestOnly", "LatestOnly")]
    public async Task Hover_DescribesCommunicationRuntimeContract(
        int line,
        int character,
        string word,
        string delivery,
        string backpressure,
        string retention,
        string ordering)
    {
        const string communicationSource = """
            task QueueWorker(input: queue<Payload>) @server { }
            task StreamWorker(input: stream<Payload>) @report { }
            task ChanWorker(input: chan<Payload>) @server { }
            struct Payload { value: Int }
            """;
        var actualLine = word switch
        {
            "queue" => 0,
            "stream" => 1,
            "chan" => 2,
            _ => line
        };
        var actualCharacter = word switch
        {
            "queue" => 24,
            "stream" => 25,
            "chan" => 23,
            _ => character
        };

        var store = await CreateStoreAsync(communicationSource);
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(actualLine, actualCharacter)
        }, CancellationToken.None);

        var contents = result?.Contents.ToString();
        Assert.NotNull(contents);
        Assert.Contains(word, contents);
        Assert.Contains(delivery, contents);
        Assert.Contains(backpressure, contents);
        Assert.Contains(retention, contents);
        Assert.Contains(ordering, contents);
    }

    [Fact]
    public async Task Hover_DescribesPlatformAnnotation()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(4, 30)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("@server", result.Contents.ToString());
        Assert.Contains("server-side", result.Contents.ToString());
    }

    [Fact]
    public async Task Hover_InfersForLoopVariableFromChannelElement()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(14, 13)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("cmd: MotionCommand", result.Contents.ToString());
    }

    [Fact]
    public async Task Hover_InfersForLoopVariableAtBindingSite()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(10, 9)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("cmd: MotionCommand", result.Contents.ToString());
    }

    [Fact]
    public async Task Hover_InfersLetVariableFromCallReturnType()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(15, 13)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("pos: MachinePosition", result.Contents.ToString());
    }

    [Fact]
    public async Task Hover_InfersLetVariableFromBooleanCallReturnType()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(16, 13)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("limit_hit: Bool", result.Contents.ToString());
    }

    [Fact]
    public async Task Hover_InfersLetVariableFromComparisonExpression()
    {
        var store = await CreateStoreAsync();
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(17, 13)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("z_ok: Bool", result.Contents.ToString());
    }

    [Theory]
    [InlineData("latest", 2, 19, "latest(stream<T>) -> T", "proposed")]
    [InlineData("batch", 3, 19, "batch(queue<T>, Int) -> List<T>", "snapshot")]
    [InlineData("ack", 6, 9, "ack message", "Queue acknowledgement")]
    [InlineData("nack", 7, 9, "nack message reason text", "negative acknowledgement")]
    public async Task Hover_DescribesRuntimePrimitive(
        string word,
        int line,
        int character,
        string expectedSignature,
        string expectedText)
    {
        const string source = """
            struct Payload { value: Int }
            task Dashboard(metrics: stream<Float>, jobs: queue<Payload>) @report {
                let current = latest(metrics)
                let pending = batch(jobs, 10)
                for job in pending {
                    log(job.value)
                    ack job
                    nack job reason "retry"
                }
            }
            """;

        var store = await CreateStoreAsync(source);
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(line, character)
        }, CancellationToken.None);

        var contents = result?.Contents.ToString();
        Assert.NotNull(contents);
        Assert.Contains(word, contents);
        Assert.Contains(expectedSignature, contents);
        Assert.Contains(expectedText, contents);
    }

    [Fact]
    public async Task Hover_InfersLetVariableFromBatchPrimitive()
    {
        const string source = """
            struct Payload { value: Int }
            task Dashboard(jobs: queue<Payload>) @server {
                let pending = batch(jobs, 10)
            }
            """;

        var store = await CreateStoreAsync(source);
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(2, 9)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("pending: List<Payload>", result.Contents.ToString());
    }

    [Fact]
    public async Task Hover_InfersForLoopVariableFromBatchList()
    {
        const string source = """
            struct Payload { value: Int }
            task Dashboard(jobs: queue<Payload>) @server {
                let pending = batch(jobs, 10)
                for job in pending {
                    log(job.value)
                }
            }
            """;

        var store = await CreateStoreAsync(source);
        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = Document(),
            Position = new Position(3, 9)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("job: Payload", result.Contents.ToString());
    }

    [Fact]
    public async Task Completion_ContainsKeywordsTypesAndDocumentSymbols()
    {
        var store = await CreateStoreAsync();
        var result = await new CompletionHandler(store).Handle(new CompletionParams
        {
            TextDocument = Document(),
            Position = new Position(1, 0)
        }, CancellationToken.None);

        Assert.Contains(result.Items, item => item.Label == "task");
        Assert.Contains(result.Items, item => item.Label == "Int");
        Assert.Contains(result.Items, item => item.Label == "server");
        Assert.Contains(result.Items, item => item.Label == "Payload");
        Assert.Contains(result.Items, item => item.Label == "storage");
        Assert.Contains(result.Items, item => item.Label == "mutation");
        Assert.Contains(result.Items, item => item.Label == "Uuid");
    }

    [Fact]
    public async Task Definition_ResolvesTypeReference()
    {
        var store = await CreateStoreAsync();
        var result = await new DefinitionHandler(store).Handle(new DefinitionParams
        {
            TextDocument = Document(),
            Position = new Position(4, 22)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result, location => location.Location?.Range.Start.Line == 0);
    }

    [Fact]
    public async Task DocumentSymbol_ReturnsTopLevelDeclarations()
    {
        var store = await CreateStoreAsync();
        var result = await new DocumentSymbolHandler(store).Handle(new DocumentSymbolParams
        {
            TextDocument = Document()
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(8, result.Count());

        var documentSymbols = result
            .Select(symbol => symbol.DocumentSymbol)
            .Where(symbol => symbol is not null)
            .Select(symbol => symbol!)
            .ToList();

        var payload = Assert.Single(documentSymbols, symbol => symbol.Name == "Payload");
        Assert.NotNull(payload.Children);
        Assert.Contains(payload.Children, child => child.Name == "value");

        var payloadStore = Assert.Single(documentSymbols, symbol => symbol.Name == "PayloadStore");
        Assert.NotNull(payloadStore.Children);
        Assert.Contains(payloadStore.Children, child => child.Name == "save");

        var worker = Assert.Single(documentSymbols, symbol => symbol.Name == "Worker");
        Assert.NotNull(worker.Children);
        Assert.Contains(worker.Children, child => child.Name == "input");
    }

    [Fact]
    public async Task SemanticTokens_ClassifyOpenedDocument()
    {
        var store = await CreateStoreAsync();
        var result = await new SemanticTokensHandler(store).Handle(new SemanticTokensParams
        {
            TextDocument = Document()
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Data);
        Assert.Equal(0, result.Data.Length % 5);
    }

    [Fact]
    public async Task Rename_ReturnsAllMatchingIdentifierEdits()
    {
        const string source = "task Worker() { let sensorValue = 1 log(sensorValue) }";
        var store = await CreateStoreAsync(source);

        var result = await new RenameHandler(store).Handle(new RenameParams
        {
            TextDocument = Document(),
            Position = new Position(0, 23),
            NewName = "temperature",
        }, CancellationToken.None);

        Assert.NotNull(result);
        var edits = Assert.Single(result.Changes!).Value.ToArray();
        Assert.Equal(2, edits.Length);
        Assert.All(edits, edit => Assert.Equal("temperature", edit.NewText));
    }

    [Fact]
    public async Task CodeAction_RewritesNarrativeOperatorOutsideStringAndComment()
    {
        const string source = """
            task Worker() {
                let active = true && false
                log("true && false") // true && false
            }
            """;
        var store = await CreateStoreAsync(source);

        var result = await new CodeActionHandler(store).Handle(new CodeActionParams
        {
            TextDocument = Document(),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(0, 0),
                new Position(2, 40)),
            Context = new CodeActionContext { Diagnostics = [] },
        }, CancellationToken.None);

        var action = Assert.Single(result!).CodeAction;
        Assert.NotNull(action);
        Assert.Contains("'and'", action.Title);
        var edits = Assert.Single(action.Edit!.Changes!).Value.ToArray();
        var edit = Assert.Single(edits);
        Assert.Equal("and", edit.NewText);
        Assert.Equal(1, edit.Range.Start.Line);
    }

    private static TextDocumentIdentifier Document() => new()
    {
        Uri = DocumentUri.From(Uri)
    };

    private static async Task<DocumentStore> CreateStoreAsync(string source = Source)
    {
        var store = new DocumentStore();
        await store.UpdateAsync(Uri, source);
        return store;
    }
}
