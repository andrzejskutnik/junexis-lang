namespace JunexisLang.Compiler.Core;

/// <summary>Stabilne nazwy generowanego API named mutation.</summary>
public static class StorageMutationNames
{
    /// <summary>Zwraca nazwę typowanej funkcji mutacji kwalifikowaną modelem.</summary>
    public static string Callable(string model, string mutation)
        => $"{Exported(model)}_{Exported(mutation)}";

    /// <summary>Zwraca nazwę jawnego wyniku typowanej mutacji.</summary>
    public static string Result(string model, string mutation)
        => Callable(model, mutation) + "Result";

    private static string Exported(string value) => value.Length == 0
        ? value
        : char.ToUpperInvariant(value[0]) + value[1..];
}
