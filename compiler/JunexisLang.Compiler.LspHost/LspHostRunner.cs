using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using JunexisLang.Compiler.LspHost.Handlers;

namespace JunexisLang.Compiler.LspHost;

/// <summary>
/// Uruchamia serwer Language Server Protocol dla JunexisLang na standardowym wejsciu/wyjsciu.
/// </summary>
public static class LspHostRunner
{
    /// <summary>
    /// Startuje serwer LSP i blokuje do czasu zakonczenia sesji klienta.
    /// </summary>
    /// <returns>Kod wyjscia procesu zgodny z powodzeniem pracy hosta.</returns>
    public static async Task<int> RunAsync()
    {
        // LSP komunikuje się przez stdin/stdout → UTF-8 bez BOM.
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var server = await LanguageServer.From(options => options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .WithServices(services => services.AddSingleton<DocumentStore>())
            .WithHandler<TextDocumentSyncHandler>()
            .WithHandler<HoverHandler>()
            .WithHandler<CompletionHandler>()
            .WithHandler<DefinitionHandler>()
            .WithHandler<ReferencesHandler>()
            .WithHandler<RenameHandler>()
            .WithHandler<CodeActionHandler>()
            .WithHandler<DocumentSymbolHandler>()
            .WithHandler<SemanticTokensHandler>());

        await server.WaitForExit;
        return 0;
    }
}
