using JunexisLang.Compiler.LspHost.Handlers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace JunexisLang.Compiler.LspHost.Tests;

public sealed class WorkspaceModuleTests
{
    [Fact]
    public async Task Definition_LoadsImportedDefinitionFromUnopenedWorkspaceModule()
    {
        using var project = TestProject.Create();
        var store = new DocumentStore();
        await store.UpdateAsync(project.MainUri, await File.ReadAllTextAsync(project.MainPath));

        var (_, _, diagnostics) = store.Get(project.MainUri);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "E0021");

        var result = await new DefinitionHandler(store).Handle(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(project.MainUri) },
            Position = new Position(2, 20),
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result, item => string.Equals(
            item.Location?.Uri.ToString(), project.TypesUri, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReferencesAndRenameIncludeUnopenedWorkspaceModule()
    {
        using var project = TestProject.Create();
        var store = new DocumentStore();
        await store.UpdateAsync(project.MainUri, await File.ReadAllTextAsync(project.MainPath));
        var document = new TextDocumentIdentifier { Uri = DocumentUri.From(project.MainUri) };

        var references = await new ReferencesHandler(store).Handle(new ReferenceParams
        {
            TextDocument = document,
            Position = new Position(2, 20),
            Context = new ReferenceContext { IncludeDeclaration = true },
        }, CancellationToken.None);
        var rename = await new RenameHandler(store).Handle(new RenameParams
        {
            TextDocument = document,
            Position = new Position(2, 20),
            NewName = "Reading",
        }, CancellationToken.None);

        Assert.NotNull(references);
        Assert.Contains(references, location => string.Equals(
            location.Uri.ToString(), project.TypesUri, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(rename?.Changes);
        Assert.Contains(rename.Changes!, change => string.Equals(
            change.Key.ToString(), project.TypesUri, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rename.Changes!, change => string.Equals(
            change.Key.ToString(), project.MainUri, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Completion_ContainsAliasedNamedQueryAndSyntheticRowFromUnopenedModule()
    {
        using var project = StorageProject.Create();
        var store = new DocumentStore();
        await store.UpdateAsync(project.MainUri, await File.ReadAllTextAsync(project.MainPath));

        var result = await new CompletionHandler(store).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(project.MainUri) },
            Position = new Position(4, 11)
        }, CancellationToken.None);

        Assert.Contains(result.Items, item =>
            item.Label == "Store_RecentObservations" && item.Kind == CompletionItemKind.Function);
        Assert.Contains(result.Items, item =>
            item.Label == "Store_RecentObservationsRow" && item.Kind == CompletionItemKind.Struct);
    }

    [Fact]
    public async Task Hover_ExplainsAliasedNamedQueryAndCanonicalRuntimeModel()
    {
        using var project = StorageProject.Create();
        var store = new DocumentStore();
        await store.UpdateAsync(project.MainUri, await File.ReadAllTextAsync(project.MainPath));

        var result = await new HoverHandler(store).Handle(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(project.MainUri) },
            Position = new Position(4, 18)
        }, CancellationToken.None);

        Assert.NotNull(result);
        var markdown = result.Contents.ToString();
        Assert.Contains("Store_RecentObservations(homeId: Uuid)", markdown);
        Assert.Contains("HomeData", markdown);
        Assert.Contains("Lokalny alias modelu", markdown);
    }

    [Theory]
    [InlineData(3, 20)]
    [InlineData(4, 18)]
    public async Task Definition_MapsAliasedQueryApiToSourceQuery(int line, int character)
    {
        using var project = StorageProject.Create();
        var store = new DocumentStore();
        await store.UpdateAsync(project.MainUri, await File.ReadAllTextAsync(project.MainPath));

        var result = await new DefinitionHandler(store).Handle(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(project.MainUri) },
            Position = new Position(line, character)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result, item => string.Equals(
            item.Location?.Uri.ToString(), project.StorageUri, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DocumentSymbols_ShowStorageEntitiesAndQueries()
    {
        using var project = StorageProject.Create();
        var store = new DocumentStore();
        await store.UpdateAsync(project.MainUri, await File.ReadAllTextAsync(project.MainPath));

        var result = await new DocumentSymbolHandler(store).Handle(new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(project.StorageUri) }
        }, CancellationToken.None);

        Assert.NotNull(result);
        var storage = Assert.Single(
            result.Select(item => item.DocumentSymbol),
            item => item?.Name == "HomeData");
        Assert.NotNull(storage);
        Assert.Contains(storage.Children!, child => child.Name == "Observation");
        Assert.Contains(storage.Children!, child => child.Name == "RecentObservations");
        Assert.Contains(storage.Children!, child => child.Name == "PurgeObservations");
    }

    private sealed class TestProject : IDisposable
    {
        private TestProject(string root)
        {
            Root = root;
            MainPath = Path.Combine(root, "sources", "main.jnx");
            TypesPath = Path.Combine(root, "sources", "domain", "types.jnx");
        }

        internal string Root { get; }
        internal string MainPath { get; }
        internal string TypesPath { get; }
        internal string MainUri => new Uri(MainPath).AbsoluteUri;
        internal string TypesUri => new Uri(TypesPath).AbsoluteUri;

        internal static TestProject Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"junexis-lsp-workspace-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "sources", "domain"));
            File.WriteAllText(Path.Combine(root, "junexis.project.toml"),
                "name = \"workspace\"\nentry = \"sources/main.jnx\"\nsourceRoots = [\"sources\"]\ntargets = [\"server\"]\noutDir = \"out\"");
            File.WriteAllText(Path.Combine(root, "sources", "main.jnx"),
                "import domain::types\n\ntask Main(value: Observation) @server { log(value) }");
            File.WriteAllText(Path.Combine(root, "sources", "domain", "types.jnx"),
                "struct Observation { value: Float }");
            return new TestProject(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class StorageProject : IDisposable
    {
        private StorageProject(string root)
        {
            Root = root;
            MainPath = Path.Combine(root, "src", "main.jnx");
            StoragePath = Path.Combine(root, "src", "data", "home.jnx");
        }

        internal string Root { get; }
        internal string MainPath { get; }
        internal string StoragePath { get; }
        internal string MainUri => new Uri(MainPath).AbsoluteUri;
        internal string StorageUri => new Uri(StoragePath).AbsoluteUri;

        internal static StorageProject Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"junexis-lsp-storage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "src", "data"));
            File.WriteAllText(Path.Combine(root, "junexis.project.toml"),
                "name = \"storage-lsp\"\nentry = \"src/main.jnx\"\nsourceRoots = [\"src\"]\ntargets = [\"server\"]\noutDir = \"out\"");
            File.WriteAllText(Path.Combine(root, "src", "main.jnx"),
                """
                import data::home { HomeData as Store }

                task ReadHome(homeId: Uuid) @server
                    -> List<Store_RecentObservationsRow> {
                    return Store_RecentObservations(homeId: homeId)
                }
                """);
            File.WriteAllText(Path.Combine(root, "src", "data", "home.jnx"),
                """
                storage HomeData {
                    entity Observation {
                        id: Uuid
                        homeId: Uuid
                        sensor: Text
                        key(id)
                    }
                    query RecentObservations(homeId: Uuid) from Observation {
                        filter homeId == params.homeId
                        project sensor
                    }
                    mutation PurgeObservations(homeId: Uuid) delete many Observation {
                        where homeId == params.homeId
                    }
                }
                """);
            return new StorageProject(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
