namespace JunexisLang.Compiler.IR;

/// <summary>Structural IR jawnej deklaracji storage M13.2/M14.2.</summary>
public sealed record IRStorageDeclaration(
    string Name,
    IReadOnlyList<IRStorageEntity> Entities,
    IReadOnlyList<IRStorageQuery> Queries,
    IReadOnlyList<IRStorageMutation> Mutations);

public sealed record IRStorageEntity(
    string Name,
    IReadOnlyList<IRStorageField> Fields,
    IReadOnlyList<IRStoragePrimaryKey> PrimaryKeys);

public sealed record IRStorageField(string Name, IRType Type);
public sealed record IRStoragePrimaryKey(IReadOnlyList<string> Fields);

public sealed record IRStorageQuery(
    string Name,
    IReadOnlyList<IRParam> Params,
    string SourceEntity,
    IReadOnlyList<IRStorageQueryStep> Steps);

public abstract record IRStorageQueryStep;
public sealed record IRStorageFilterStep(IRExpr Predicate) : IRStorageQueryStep;
public sealed record IRStorageProjectStep(IReadOnlyList<IRStorageProjection> Projections) : IRStorageQueryStep;
public sealed record IRStorageProjection(string Field, string? Alias);
public sealed record IRStorageSortStep(IReadOnlyList<IRStorageSortKey> Keys) : IRStorageQueryStep;
public sealed record IRStorageSortKey(string Field, bool Descending);
public sealed record IRStorageLimitStep(long Count, long Offset) : IRStorageQueryStep;

/// <summary>Rodzaj mutacji zachowany w Structural IR.</summary>
public enum IRStorageMutationKind
{
    Insert,
    Update,
    Delete
}

/// <summary>Jawny zasięg mutacji zachowany w Structural IR.</summary>
public enum IRStorageMutationCardinality
{
    Single,
    Many
}

/// <summary>Nazwana mutacja storage zachowana bez utraty intencji autora.</summary>
public sealed record IRStorageMutation(
    string Name,
    IReadOnlyList<IRParam> Params,
    string Entity,
    IRStorageMutationKind Kind,
    IRStorageMutationCardinality Cardinality,
    IReadOnlyList<IRStorageMutationAssignment> Assignments,
    IRExpr? Predicate);

/// <summary>Przypisanie pola w Structural IR mutacji.</summary>
public sealed record IRStorageMutationAssignment(string Field, IRExpr Value);
