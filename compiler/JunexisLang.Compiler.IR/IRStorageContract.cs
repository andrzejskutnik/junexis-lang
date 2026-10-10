namespace JunexisLang.Compiler.IR;

/// <summary>Znaczenie operacji storage niezależne od języka providera.</summary>
public enum IRStorageOperationKind
{
    /// <summary>Otwarcie zasobu.</summary>
    Open,
    /// <summary>Kontrolowane zamknięcie zasobu.</summary>
    Close,
    /// <summary>Odczyt bez zmiany trwałego stanu.</summary>
    Read,
    /// <summary>Zapis lub usunięcie zmieniające trwały stan.</summary>
    Write,
    /// <summary>Rozpoczęcie transakcji.</summary>
    Begin,
    /// <summary>Zatwierdzenie transakcji.</summary>
    Commit,
    /// <summary>Wycofanie transakcji.</summary>
    Rollback,
    /// <summary>Zmiana wersji schematu.</summary>
    Migrate,
    /// <summary>Sprawdzenie gotowości zasobu.</summary>
    Health,
    /// <summary>Operacja domenowa, której nazwa nie określa jeszcze standardowego rodzaju.</summary>
    Custom
}

/// <summary>Klasa efektu widoczna dla analizy, generatorów i narzędzi.</summary>
public enum IRStorageEffect
{
    /// <summary>Operacja lifecycle.</summary>
    Lifecycle,
    /// <summary>Operacja tylko do odczytu.</summary>
    Read,
    /// <summary>Operacja zmieniająca stan.</summary>
    Write,
    /// <summary>Operacja sterująca transakcją.</summary>
    Transaction,
    /// <summary>Operacja migracji schematu.</summary>
    Migration,
    /// <summary>Operacja diagnostyczna.</summary>
    Health,
    /// <summary>Efekt wymaga jawnego doprecyzowania w przyszłej składni resource.</summary>
    Custom
}

/// <summary>Pojedyncza operacja providera po projekcji kontraktu bootstrapowego.</summary>
public sealed record IRStorageOperation(
    string Name,
    IRStorageOperationKind Kind,
    IRStorageEffect Effect,
    IReadOnlyList<IRParam> Params,
    IRType? ReturnType);

/// <summary>Neutralny opis providera storage wyprowadzony z <c>extern storage</c>.</summary>
public sealed record IRStorageContract(
    string Name,
    string? Target,
    string? Language,
    string? UserFile,
    IReadOnlyList<IRStorageOperation> Operations);

/// <summary>Buduje kontrakt <c>junexis.storage/0.1</c> bez zależności od SQLite ani Go.</summary>
public static class IRStorageContractProjection
{
    /// <summary>Wersja zamrożonego kontraktu semantycznego.</summary>
    public const string SchemaVersion = "junexis.storage/0.1";

    /// <summary>Projektuje wszystkie granice <c>extern storage</c> z dokumentu IR.</summary>
    public static IReadOnlyList<IRStorageContract> Project(IRDocument document) =>
        document.ExternContracts
            .Where(contract => contract.Role.Equals("storage", StringComparison.OrdinalIgnoreCase))
            .Select(contract => new IRStorageContract(
                contract.Name,
                contract.Target,
                contract.Language,
                contract.UserFile,
                contract.Functions.Select(ProjectOperation).ToList()))
            .ToList();

    private static IRStorageOperation ProjectOperation(IRExternContractFunction function)
    {
        var kind = Classify(function.Name);
        var effect = kind switch
        {
            IRStorageOperationKind.Open or IRStorageOperationKind.Close => IRStorageEffect.Lifecycle,
            IRStorageOperationKind.Read => IRStorageEffect.Read,
            IRStorageOperationKind.Write => IRStorageEffect.Write,
            IRStorageOperationKind.Begin or IRStorageOperationKind.Commit or IRStorageOperationKind.Rollback => IRStorageEffect.Transaction,
            IRStorageOperationKind.Migrate => IRStorageEffect.Migration,
            IRStorageOperationKind.Health => IRStorageEffect.Health,
            _ => IRStorageEffect.Custom
        };

        return new IRStorageOperation(function.Name, kind, effect, function.Params, function.ReturnType);
    }

    private static IRStorageOperationKind Classify(string name)
    {
        var normalized = name.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        if (Matches(normalized, "open", "connect", "initialize")) return IRStorageOperationKind.Open;
        if (Matches(normalized, "close", "disconnect", "shutdown")) return IRStorageOperationKind.Close;
        if (Matches(normalized, "begin", "begin_transaction")) return IRStorageOperationKind.Begin;
        if (Matches(normalized, "commit")) return IRStorageOperationKind.Commit;
        if (Matches(normalized, "rollback")) return IRStorageOperationKind.Rollback;
        if (Matches(normalized, "migrate", "migration")) return IRStorageOperationKind.Migrate;
        if (Matches(normalized, "health", "health_check", "ping")) return IRStorageOperationKind.Health;
        if (StartsWithAny(normalized, "get", "read", "find", "list", "latest", "recent", "count", "exists", "query")) return IRStorageOperationKind.Read;
        if (StartsWithAny(normalized, "save", "write", "insert", "create", "upsert", "update", "delete", "remove")) return IRStorageOperationKind.Write;
        return IRStorageOperationKind.Custom;
    }

    private static bool Matches(string value, params string[] candidates) =>
        candidates.Contains(value, StringComparer.Ordinal);

    private static bool StartsWithAny(string value, params string[] prefixes) =>
        prefixes.Any(prefix => value.Equals(prefix, StringComparison.Ordinal) ||
            value.StartsWith(prefix + "_", StringComparison.Ordinal));
}
