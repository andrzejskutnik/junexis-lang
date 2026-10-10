using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

/// <summary>Wspolne helpery dla testow transformacji AST → IR.</summary>
public sealed partial class IrTransformerTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<IRDocument> TransformAsync(string source)
    {
        await Task.Yield();
        var result = new JunexisCompiler().ParseSource(source, "ir-test.jnx");
        Assert.True(result.Success,
            $"Parse failed:\n{string.Join('\n', result.Diagnostics.Select(d => d.Message))}");
        return new AstToIrTransformer().Transform(result.Ast!);
    }
}
