namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>
/// Testy hookow stanowych TypeScriptCodeGenerator.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    // ─── TypeScriptCodeGenerator: hooki stanowe ─────────────────────────────

    [Fact]
    public void TS_StatefulProducer_Hook_EmitsStateFactoryAndReadPath()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "Publish",
            outputType: new IRCommType(IRCommKind.Chan, new IRNamedType("Int")),
            states: [new IRStateDecl("counter", new IRNamedType("Int"), null)]);
        var index = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("export const PublishHasState = true;", index.Content);
        Assert.Contains("type PublishState = {", index.Content);
        Assert.Contains("counter: number;", index.Content);
        Assert.Contains("let __publishStateCache: PublishState | undefined;", index.Content);
        Assert.Contains("function createDefaultPublishState(): PublishState {", index.Content);
        Assert.Contains("function loadPublishState(): PublishState {", index.Content);
        Assert.Contains("const persisted = localStorage.getItem('junexis.state.Publish');", index.Content);
        Assert.Contains("return __publishStateCache ? { ...__publishStateCache } : createDefaultPublishState();", index.Content);
        Assert.Contains("function savePublishState(state: PublishState): void {", index.Content);
        Assert.Contains("__publishStateCache = { ...state };", index.Content);
        Assert.Contains("localStorage.setItem('junexis.state.Publish', JSON.stringify(state));", index.Content);
        Assert.Contains("function readPublishStateValue(state: PublishState): number {", index.Content);
        Assert.Contains("if (PublishHasState) {", index.Content);
        Assert.Contains("const state = loadPublishState();", index.Content);
        Assert.Contains("return readPublishStateValue(state);", index.Content);
        Assert.Contains("return state.counter;", index.Content);
    }

    [Fact]
    public void TS_StatefulConsumer_Hook_EmitsStateFactoryAndWritePath()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "Consume",
            @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)],
            states: [new IRStateDecl("lastSeen", new IRNamedType("Text"), null)]);
        var index = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("export const ConsumeHasState = true;", index.Content);
        Assert.Contains("type ConsumeState = {", index.Content);
        Assert.Contains("lastSeen: string;", index.Content);
        Assert.Contains("let __consumeStateCache: ConsumeState | undefined;", index.Content);
        Assert.Contains("function createDefaultConsumeState(): ConsumeState {", index.Content);
        Assert.Contains("function loadConsumeState(): ConsumeState {", index.Content);
        Assert.Contains("const persisted = localStorage.getItem('junexis.state.Consume');", index.Content);
        Assert.Contains("return __consumeStateCache ? { ...__consumeStateCache } : createDefaultConsumeState();", index.Content);
        Assert.Contains("function saveConsumeState(state: ConsumeState): void {", index.Content);
        Assert.Contains("__consumeStateCache = { ...state };", index.Content);
        Assert.Contains("localStorage.setItem('junexis.state.Consume', JSON.stringify(state));", index.Content);
        Assert.Contains("function writeConsumeStateValue(state: ConsumeState, item: string): void {", index.Content);
        Assert.Contains("if (ConsumeHasState) {", index.Content);
        Assert.Contains("const state = loadConsumeState();", index.Content);
        Assert.Contains("writeConsumeStateValue(state, item);", index.Content);
        Assert.Contains("saveConsumeState(state);", index.Content);
        Assert.Contains("state.lastSeen = item;", index.Content);
        Assert.Contains("void state.lastSeen;", index.Content);
    }

    [Fact]
    public void TS_MultiStateConsumer_Hook_WritesAllCompatibleFields()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "Consume",
            @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)],
            states:
            [
                new IRStateDecl("lastSeen", new IRNamedType("Text"), null),
                new IRStateDecl("currentValue", new IRNamedType("Text"), null)
            ]);
        var index = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("// Candidate state fields: currentValue, lastSeen", index.Content);
        Assert.Contains("state.currentValue = item;", index.Content);
        Assert.Contains("state.lastSeen = item;", index.Content);
    }

    [Fact]
    public void TS_MultiStateProducer_Hook_UsesPriorityOrderedReadField()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "Publish",
            outputType: new IRCommType(IRCommKind.Chan, new IRNamedType("Text")),
            states:
            [
                new IRStateDecl("currentValue", new IRNamedType("Text"), null),
                new IRStateDecl("latestValue", new IRNamedType("Text"), null)
            ]);
        var index = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("// Candidate state fields: latestValue, currentValue", index.Content);
        Assert.Contains("return state.latestValue;", index.Content);
    }

    [Fact]
    public void TS_MultiStateConsumer_Hook_UsesWritePreferredOrdering()
    {
        var gen = new TypeScriptCodeGenerator();
        var task = ReportTask(
            "Consume",
            @params: [new IRParam("input", new IRCommType(IRCommKind.Chan, new IRNamedType("Text")), null)],
            states:
            [
                new IRStateDecl("latestValue", new IRNamedType("Text"), null),
                new IRStateDecl("currentValue", new IRNamedType("Text"), null)
            ]);
        var index = gen.Generate(DocWith(task), Opts)
            .Single(f => f.FilePath == "report/index.ts");

        Assert.Contains("// Candidate state fields: currentValue, latestValue", index.Content);
    }
}