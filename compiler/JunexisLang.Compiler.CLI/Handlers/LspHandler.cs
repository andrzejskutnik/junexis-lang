namespace JunexisLang.Compiler.CLI.Handlers;

internal static class LspHandler
{
    internal static Task<int> HandleAsync()
    {
        return JunexisLang.Compiler.LspHost.LspHostRunner.RunAsync();
    }
}
