using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.LspHost;

/// <summary>
/// Indeks symboli najwyższego poziomu z jednego dokumentu:
/// task, struct, enum, fn, pipeline, device, supervisor, circuit_breaker, extern.
/// Obejmuje też zagnieżdżone symbole: pola struktury, warianty enuma, parametry tasków/fn.
/// </summary>
internal sealed class SymbolIndex
{
    private readonly IReadOnlyDictionary<string, AstNode> _map;

    private SymbolIndex(Dictionary<string, AstNode> map) => _map = map;

    // ── Budowanie ─────────────────────────────────────────────────────────

    public static SymbolIndex Build(ProgramNode program)
    {
        var map = new Dictionary<string, AstNode>(StringComparer.Ordinal);
        foreach (var decl in program.Declarations)
        {
            var (name, node) = NameOf(decl);
            if (name is not null) map[name] = node;

            // Zagnieżdżone symbole: pola struct, warianty enum, parametry task/fn.
            switch (decl)
            {
                case StructDeclarationNode s:
                    foreach (var f in s.Fields)
                        map.TryAdd(f.Name, f);
                    break;
                case EnumDeclarationNode e:
                    foreach (var v in e.Variants)
                        map.TryAdd(v.Name, v);
                    break;
                case TaskDeclarationNode t:
                    foreach (var p in t.Params)
                        map.TryAdd(p.Name, p);
                    break;
                case FnDeclarationNode f:
                    foreach (var p in f.Params)
                        map.TryAdd(p.Name, p);
                    break;
            }
        }
        return new SymbolIndex(map);
    }

    // ── Wyszukiwanie ──────────────────────────────────────────────────────

    public AstNode? Find(string name) =>
        _map.TryGetValue(name, out var n) ? n : null;

    public IEnumerable<string> AllNames() => _map.Keys;

    // ── Pomocnicza: wyciąga nazwę z węzła deklaracji ──────────────────────

    private static (string? Name, AstNode Node) NameOf(AstNode n) => n switch
    {
        StructDeclarationNode          s  => (s.Name,  s),
        EnumDeclarationNode            e  => (e.Name,  e),
        FnDeclarationNode              f  => (f.Name,  f),
        TaskDeclarationNode            t  => (t.Name,  t),
        PipelineDeclarationNode        p  => (p.Name,  p),
        DeviceDeclarationNode          dv => (dv.Name, dv),
        SupervisorDeclarationNode      sv => (sv.Name, sv),
        CircuitBreakerDeclarationNode  cb => (cb.Name, cb),
        ExternTaskNode                 et => (et.Name, et),
        ExternTypeNode                 etp => (etp.Name, etp),
        ExternContractNode             ec => (ec.Name, ec),
        _                                 => (null, n)
    };
}
