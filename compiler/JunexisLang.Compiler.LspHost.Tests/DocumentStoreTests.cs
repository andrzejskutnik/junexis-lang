namespace JunexisLang.Compiler.LspHost.Tests;

public sealed class DocumentStoreTests
{
    [Fact]
    public async Task UpdateAsync_IncludesTopologyDiagnosticsUsedByCli()
    {
        const string uri = "file:///m5-topology.jnx";
        var store = new DocumentStore();

        await store.UpdateAsync(uri, "task A() { } pipeline Broken { use A -> Missing }");

        var (_, ast, diagnostics) = store.Get(uri);
        Assert.NotNull(ast);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "T0002");
    }
}
