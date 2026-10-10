namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy hookow stanowych GoCodeGenerator wydzielone z pliku scenariuszowego.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── GoCodeGenerator: state hooks ──────────────────────────────────────

    [Fact]
    public async Task Go_StateInitializerCall_IsPreservedAndGetsTypedStub()
    {
        var ir = await TransformToIrAsync("task Alerts() @server { state last_alert: Time = epoch() }");

        var content = new GoCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("Last_alert: epoch(),", content);
        Assert.Contains("func epoch() time.Time", content);
    }

    [Fact]
    public void Go_StatefulProducer_Hook_EmitsStateAwareBranch()
    {
        var gen = new GoCodeGenerator();
        var task = ServerTask(
            "Publish",
            outputType: new IRCommType(IRCommKind.Chan, new IRNamedType("Int")),
            states: [new IRStateDecl("counter", new IRNamedType("Int"), null)]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("const PublishHasState = true", content);
        Assert.Contains("type PublishState struct {", content);
        Assert.Contains("Counter int64 `json:\"counter\"`", content);
        Assert.Contains("var publishStateCache *PublishState", content);
        Assert.Contains("const publishStateFile = \"Publish.state.json\"", content);
        Assert.Contains("func createDefaultPublishState() PublishState {", content);
        Assert.Contains("func loadPublishState() PublishState {", content);
        Assert.Contains("if publishStateCache != nil {", content);
        Assert.Contains("if data, err := os.ReadFile(publishStateFile); err == nil {", content);
        Assert.Contains("if json.Unmarshal(data, &state) == nil {", content);
        Assert.Contains("publishStateCache = &state", content);
        Assert.Contains("state := createDefaultPublishState()", content);
        Assert.Contains("func savePublishState(state PublishState) {", content);
        Assert.Contains("if data, err := json.Marshal(state); err == nil {", content);
        Assert.Contains("_ = os.WriteFile(publishStateFile, data, 0600)", content);
        Assert.Contains("func readPublishStateValue(state PublishState) int64 {", content);
        Assert.Contains("if PublishHasState {", content);
        Assert.Contains("state := loadPublishState()", content);
        Assert.Contains("return readPublishStateValue(state)", content);
        Assert.Contains("return state.Counter", content);
    }

    [Fact]
    public void Go_StatefulConsumer_Hook_EmitsStateAwareBranch()
    {
        var gen = new GoCodeGenerator();
        var task = ServerTask(
            "Consume",
            @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)],
            states: [new IRStateDecl("lastSeen", new IRNamedType("Text"), null)]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("const ConsumeHasState = true", content);
        Assert.Contains("type ConsumeState struct {", content);
        Assert.Contains("LastSeen string `json:\"lastSeen\"`", content);
        Assert.Contains("var consumeStateCache *ConsumeState", content);
        Assert.Contains("const consumeStateFile = \"Consume.state.json\"", content);
        Assert.Contains("func createDefaultConsumeState() ConsumeState {", content);
        Assert.Contains("func loadConsumeState() ConsumeState {", content);
        Assert.Contains("if consumeStateCache != nil {", content);
        Assert.Contains("if data, err := os.ReadFile(consumeStateFile); err == nil {", content);
        Assert.Contains("func saveConsumeState(state ConsumeState) {", content);
        Assert.Contains("consumeStateCache = &state", content);
        Assert.Contains("_ = os.WriteFile(consumeStateFile, data, 0600)", content);
        Assert.Contains("func writeConsumeStateValue(state *ConsumeState, item string) {", content);
        Assert.Contains("if ConsumeHasState {", content);
        Assert.Contains("state := loadConsumeState()", content);
        Assert.Contains("writeConsumeStateValue(&state, item)", content);
        Assert.Contains("saveConsumeState(state)", content);
        Assert.Contains("state.LastSeen = item", content);
        Assert.Contains("_ = state.LastSeen", content);
    }

    [Fact]
    public void Go_MultiStateConsumer_Hook_WritesAllCompatibleFields()
    {
        var gen = new GoCodeGenerator();
        var task = ServerTask(
            "Consume",
            @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)],
            states:
            [
                new IRStateDecl("lastSeen", new IRNamedType("Text"), null),
                new IRStateDecl("currentValue", new IRNamedType("Text"), null)
            ]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("// Candidate state fields: currentValue, lastSeen", content);
        Assert.Contains("state.CurrentValue = item", content);
        Assert.Contains("state.LastSeen = item", content);
    }

    [Fact]
    public void Go_MultiStateProducer_Hook_UsesPriorityOrderedReadField()
    {
        var gen = new GoCodeGenerator();
        var task = ServerTask(
            "Publish",
            outputType: new IRCommType(IRCommKind.Chan, new IRNamedType("Text")),
            states:
            [
                new IRStateDecl("currentValue", new IRNamedType("Text"), null),
                new IRStateDecl("latestValue", new IRNamedType("Text"), null)
            ]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("// Candidate state fields: latestValue, currentValue", content);
        Assert.Contains("return state.LatestValue", content);
    }

    [Fact]
    public void Go_MultiStateConsumer_Hook_UsesWritePreferredOrdering()
    {
        var gen = new GoCodeGenerator();
        var task = ServerTask(
            "Consume",
            @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)],
            states:
            [
                new IRStateDecl("latestValue", new IRNamedType("Text"), null),
                new IRStateDecl("currentValue", new IRNamedType("Text"), null)
            ]);
        var content = gen.Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("// Candidate state fields: currentValue, latestValue", content);
    }

    [Fact]
    public void Go_TypedArrayState_EmitsTypedEmptySliceInitializer()
    {
        var task = ServerTask(
            "Aggregate",
            states:
            [
                new IRStateDecl(
                    "buffer",
                    new IRArrayType(new IRNamedType("Reading")),
                    new IRCallExpr(new IRIdentExpr("__array__"), []))
            ]);

        var content = new GoCodeGenerator().Generate(DocWith(task), Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("Buffer: []Reading{}", content);
        Assert.DoesNotContain("buffer: []interface{}{}", content);
    }

    [Fact]
    public async Task Go_StatefulChanLoop_PersistsAfterEveryReceivedItem()
    {
        var ir = await TransformToIrAsync("""
            task Remember(input: chan<Text>) @server {
                state last_boot: Text = ""
                for item in input {
                    last_boot = item
                }
            }
            """);

        var content = new GoCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("state.Last_boot = item", content);
        Assert.Contains("saveRememberState(state)", content);
        Assert.True(
            content.IndexOf("state.Last_boot = item", StringComparison.Ordinal) <
            content.IndexOf("saveRememberState(state)", content.IndexOf("state.Last_boot = item", StringComparison.Ordinal), StringComparison.Ordinal));
    }
}
