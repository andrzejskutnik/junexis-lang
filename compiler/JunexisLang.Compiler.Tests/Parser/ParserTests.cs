using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Tests.Parser;

/// <summary>Wspolne helpery dla testow parsera.</summary>
public sealed partial class ParserTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static async Task<CompilerResult> Check(string source)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, source);
            var compiler = new JunexisCompiler();
            return await compiler.CheckAsync(new CheckOptions(tmp));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static Task<ProgramNode> Parse(string source)
    {
        var result = new JunexisCompiler().ParseSource(source);
        Assert.True(result.Success,
            $"Parse failed:\n{string.Join('\n', result.Diagnostics.Select(d => d.Message))}");
        return Task.FromResult(result.Ast!);
    }
}
