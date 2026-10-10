namespace JunexisLang.Compiler.Core;

/// <summary>Stabilne nazwy językowego API named query współdzielone przez semantykę i codegen.</summary>
public static class StorageQueryNames
{
    /// <summary>Zwraca nazwę funkcji wywoływanej bezpośrednio z tasku.</summary>
    public static string Callable(string model, string query)
        => $"{Exported(model)}_{Exported(query)}";

    /// <summary>Zwraca nazwę syntetycznego typu jednego wiersza wyniku.</summary>
    public static string Row(string model, string query)
        => Callable(model, query) + "Row";

    private static string Exported(string value) => value.Length == 0
        ? value
        : char.ToUpperInvariant(value[0]) + value[1..];
}
