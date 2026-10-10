using System.Diagnostics;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class ParseHandler
{
    internal static async Task<int> HandleAsync(FileInfo input, bool json)
    {
        var sw = Stopwatch.StartNew();
        var result = await new JunexisCompiler().ParseAsync(new ParseOptions(input.FullName));
        sw.Stop();

        if (json)
            DiagnosticFormatter.WriteJson("parse", result.Success, sw.ElapsedMilliseconds, input.FullName, result.Diagnostics);
        else
            DiagnosticFormatter.Write(result.Diagnostics);

        return result.Success ? 0 : 1;
    }
}
