namespace JunexisLang.Compiler.IR;

/// <summary>Rodzaj przenośnej zmiany logicznej encji storage.</summary>
public enum StorageMutationKind
{
    /// <summary>Tworzy jeden nowy rekord logiczny.</summary>
    Insert,
    /// <summary>Zmienia pola istniejącego rekordu albo jawnego zbioru rekordów.</summary>
    Update,
    /// <summary>Usuwa istniejący rekord albo jawny zbiór rekordów.</summary>
    Delete
}

/// <summary>Jawna deklaracja oczekiwanego zasięgu zmiany.</summary>
public enum StorageMutationCardinality
{
    /// <summary>Operacja ograniczona pełnym kluczem pojedynczego rekordu.</summary>
    Single,
    /// <summary>Jawnie dopuszczona operacja obejmująca wiele rekordów.</summary>
    Many
}

/// <summary>Przypisanie wartości do logicznego pola encji.</summary>
public sealed record StorageMutationAssignment(string Field, StorageQueryExpression Value);

/// <summary>
/// Provider-neutralny plan pojedynczej nazwanej mutacji storage. Sam plan nie
/// zezwala na automatyczne ponowienie operacji.
/// </summary>
public sealed record StorageMutationPlan(
    string SchemaVersion,
    string Name,
    string Model,
    string Entity,
    StorageMutationKind Kind,
    StorageMutationCardinality Cardinality,
    IReadOnlyList<StorageQueryParameter> Parameters,
    IReadOnlyList<StorageMutationAssignment> Assignments,
    StorageQueryPredicate? Predicate)
{
    /// <summary>Bieżąca wersja przenośnego kontraktu Mutation Plan.</summary>
    public const string CurrentVersion = "junexis.storage.mutation-plan/0.1";
}

/// <summary>Kontrolowany problem walidacji Mutation Plan.</summary>
public sealed record StorageMutationPlanIssue(string Code, string Path, string Message);

/// <summary>Waliduje przenośną intencję zapisu przed Runtime i providerem.</summary>
public static class StorageMutationPlanValidator
{
    /// <summary>Sprawdza model, kształt, typy oraz bezpieczny zasięg mutacji.</summary>
    public static IReadOnlyList<StorageMutationPlanIssue> Validate(
        StorageDataModel model,
        StorageMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);

        var issues = StorageQueryPlanValidator.ValidateModel(model)
            .Select(issue => new StorageMutationPlanIssue(issue.Code, issue.Path, issue.Message))
            .ToList();
        if (!string.Equals(plan.SchemaVersion, StorageMutationPlan.CurrentVersion, StringComparison.Ordinal))
            Add(issues, "SMP001", "/mutation/schemaVersion", "Nieobsługiwana wersja Mutation Plan IR.");
        if (!string.Equals(plan.Model, model.Name, StringComparison.Ordinal))
            Add(issues, "SMP002", "/mutation/model", "Mutacja wskazuje inny logiczny model danych.");

        var parameters = new Dictionary<string, StorageQueryParameter>(StringComparer.Ordinal);
        for (var index = 0; index < plan.Parameters.Count; index++)
        {
            var parameter = plan.Parameters[index];
            if (!parameters.TryAdd(parameter.Name, parameter))
                Add(issues, "SMP010", $"/mutation/parameters/{index}/name", $"Parametr '{parameter.Name}' jest zadeklarowany więcej niż raz.");
        }

        var entity = model.Entities.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, plan.Entity, StringComparison.Ordinal));
        if (entity is null)
        {
            Add(issues, "SMP020", "/mutation/entity", $"Mutacja odwołuje się do nieznanej encji '{plan.Entity}'.");
            return issues;
        }

        var fields = new Dictionary<string, StorageModelField>(StringComparer.Ordinal);
        foreach (var field in entity.Fields)
            fields.TryAdd(field.Name, field);
        ValidateShape(plan, entity, fields, parameters, issues);
        return issues;
    }

    private static void ValidateShape(
        StorageMutationPlan plan,
        StorageModelEntity entity,
        IReadOnlyDictionary<string, StorageModelField> fields,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageMutationPlanIssue> issues)
    {
        switch (plan.Kind)
        {
            case StorageMutationKind.Insert:
                if (plan.Cardinality != StorageMutationCardinality.Single)
                    Add(issues, "SMP030", "/mutation/cardinality", "Insert tworzy jeden rekord i wymaga cardinality Single.");
                if (plan.Predicate is not null)
                    Add(issues, "SMP031", "/mutation/predicate", "Insert nie może zawierać predykatu.");
                ValidateAssignments(plan, entity, fields, parameters, requireAllNonNullable: true, issues);
                break;
            case StorageMutationKind.Update:
                ValidateAssignments(plan, entity, fields, parameters, requireAllNonNullable: false, issues);
                ValidatePredicateAndCardinality(plan, entity, fields, parameters, issues);
                break;
            case StorageMutationKind.Delete:
                if (plan.Assignments.Count > 0)
                    Add(issues, "SMP032", "/mutation/assignments", "Delete nie może zawierać przypisań.");
                ValidatePredicateAndCardinality(plan, entity, fields, parameters, issues);
                break;
            default:
                Add(issues, "SMP099", "/mutation/kind", $"Nieznany rodzaj mutacji: {plan.Kind}.");
                break;
        }
    }

    private static void ValidateAssignments(
        StorageMutationPlan plan,
        StorageModelEntity entity,
        IReadOnlyDictionary<string, StorageModelField> fields,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        bool requireAllNonNullable,
        List<StorageMutationPlanIssue> issues)
    {
        if (plan.Assignments.Count == 0)
            Add(issues, "SMP033", "/mutation/assignments", $"{plan.Kind} wymaga co najmniej jednego przypisania.");

        var assigned = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < plan.Assignments.Count; index++)
        {
            var assignment = plan.Assignments[index];
            var path = $"/mutation/assignments/{index}";
            if (!assigned.Add(assignment.Field))
                Add(issues, "SMP034", path + "/field", $"Pole '{assignment.Field}' jest przypisane więcej niż raz.");
            if (!fields.TryGetValue(assignment.Field, out var field))
            {
                Add(issues, "SMP035", path + "/field", $"Nieznane pole '{assignment.Field}'.");
                continue;
            }
            if (plan.Kind == StorageMutationKind.Update && entity.PrimaryKey.Contains(field.Name, StringComparer.Ordinal))
                Add(issues, "SMP036", path + "/field", $"Update nie może zmieniać pola klucza '{field.Name}'.");

            var value = ResolveValue(assignment.Value, path + "/value", parameters, issues);
            if (value is { } resolved &&
                (resolved.Type != field.Type || resolved.Nullable && !field.Nullable))
            {
                Add(issues, "SMP037", path + "/value", $"Wartość pola '{field.Name}' nie jest zgodna z typem {field.Type}{(field.Nullable ? "?" : string.Empty)}.");
            }
        }

        if (!requireAllNonNullable)
            return;
        foreach (var field in entity.Fields.Where(field => !field.Nullable))
        {
            if (!assigned.Contains(field.Name))
                Add(issues, "SMP038", "/mutation/assignments", $"Insert wymaga wartości nie-nullable pola '{field.Name}'.");
        }
    }

    private static void ValidatePredicateAndCardinality(
        StorageMutationPlan plan,
        StorageModelEntity entity,
        IReadOnlyDictionary<string, StorageModelField> fields,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageMutationPlanIssue> issues)
    {
        if (plan.Predicate is null)
        {
            Add(issues, "SMP040", "/mutation/predicate", $"{plan.Kind} wymaga jawnego predykatu.");
            return;
        }

        ValidatePredicate(plan.Predicate, "/mutation/predicate", fields, parameters, issues);
        if (plan.Cardinality == StorageMutationCardinality.Single &&
            !IsBoundByCompleteKey(plan.Predicate, entity.PrimaryKey, parameters))
        {
            Add(issues, "SMP041", "/mutation/predicate", "Mutacja Single musi ograniczać wszystkie pola klucza przez równość z parametrem albo literałem.");
        }
    }

    private static void ValidatePredicate(
        StorageQueryPredicate predicate,
        string path,
        IReadOnlyDictionary<string, StorageModelField> fields,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageMutationPlanIssue> issues)
    {
        switch (predicate)
        {
            case StorageComparisonPredicate comparison:
                var left = ResolvePredicateValue(comparison.Left, path + "/left", fields, parameters, issues);
                var right = ResolvePredicateValue(comparison.Right, path + "/right", fields, parameters, issues);
                if (left is { } leftType && right is { } rightType && leftType.Type != rightType.Type)
                    Add(issues, "SMP050", path, $"Nie można porównać wartości {leftType.Type} i {rightType.Type}.");
                break;
            case StorageLogicalPredicate logical:
                if (logical.Terms.Count < 2)
                    Add(issues, "SMP051", path + "/terms", "Operator logiczny wymaga co najmniej dwóch warunków.");
                for (var index = 0; index < logical.Terms.Count; index++)
                    ValidatePredicate(logical.Terms[index], $"{path}/terms/{index}", fields, parameters, issues);
                break;
            case StorageNotPredicate not:
                ValidatePredicate(not.Inner, path + "/inner", fields, parameters, issues);
                break;
            case StorageNullPredicate nullCheck:
                ResolvePredicateValue(nullCheck.Expression, path + "/expression", fields, parameters, issues);
                break;
            default:
                Add(issues, "SMP098", path, $"Nieznany predykat Mutation Plan IR: {predicate.GetType().Name}.");
                break;
        }
    }

    private static ResolvedValue? ResolveValue(
        StorageQueryExpression expression,
        string path,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageMutationPlanIssue> issues) => expression switch
        {
            StorageParameterExpression parameter => ResolveParameter(parameter, path, parameters, issues),
            StorageLiteralExpression literal => new ResolvedValue(literal.Literal.Type, literal.Literal.CanonicalValue is null),
            StorageFieldExpression field => AddAndReturnNull(issues, "SMP052", path, $"Przypisanie nie może odczytywać pola '{field.Field}'."),
            _ => AddAndReturnNull(issues, "SMP097", path, $"Nieznane wyrażenie Mutation Plan IR: {expression.GetType().Name}.")
        };

    private static ResolvedValue? ResolvePredicateValue(
        StorageQueryExpression expression,
        string path,
        IReadOnlyDictionary<string, StorageModelField> fields,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageMutationPlanIssue> issues) => expression switch
        {
            StorageFieldExpression field when fields.TryGetValue(field.Field, out var modelField) =>
                new ResolvedValue(modelField.Type, modelField.Nullable),
            StorageFieldExpression field => AddAndReturnNull(issues, "SMP053", path, $"Nieznane pole '{field.Field}'."),
            StorageParameterExpression parameter => ResolveParameter(parameter, path, parameters, issues),
            StorageLiteralExpression literal => new ResolvedValue(literal.Literal.Type, literal.Literal.CanonicalValue is null),
            _ => AddAndReturnNull(issues, "SMP097", path, $"Nieznane wyrażenie Mutation Plan IR: {expression.GetType().Name}.")
        };

    private static ResolvedValue? ResolveParameter(
        StorageParameterExpression expression,
        string path,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageMutationPlanIssue> issues)
    {
        if (parameters.TryGetValue(expression.Parameter, out var parameter))
            return new ResolvedValue(parameter.Type, parameter.Nullable);
        return AddAndReturnNull(issues, "SMP054", path, $"Nieznany parametr '{expression.Parameter}'.");
    }

    private static bool IsBoundByCompleteKey(
        StorageQueryPredicate predicate,
        IReadOnlyList<string> primaryKey,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters)
    {
        var bound = new HashSet<string>(StringComparer.Ordinal);
        if (!CollectKeyEqualities(predicate, bound, parameters))
            return false;
        return primaryKey.All(bound.Contains);
    }

    private static bool CollectKeyEqualities(
        StorageQueryPredicate predicate,
        HashSet<string> bound,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters)
    {
        switch (predicate)
        {
            case StorageLogicalPredicate { Operator: StorageLogicalOperator.And } logical:
                return logical.Terms.All(term => CollectKeyEqualities(term, bound, parameters));
            case StorageComparisonPredicate { Operator: StorageComparisonOperator.Equal } comparison:
                if (TryFieldAndNonNullInput(comparison.Left, comparison.Right, parameters, out var field) ||
                    TryFieldAndNonNullInput(comparison.Right, comparison.Left, parameters, out field))
                    bound.Add(field);
                return true;
            case StorageComparisonPredicate:
            case StorageNullPredicate:
                return true;
            default:
                return false;
        }
    }

    private static bool TryFieldAndNonNullInput(
        StorageQueryExpression possibleField,
        StorageQueryExpression possibleInput,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        out string field)
    {
        var inputIsNonNull = possibleInput switch
        {
            StorageParameterExpression parameter =>
                parameters.TryGetValue(parameter.Parameter, out var declaration) && !declaration.Nullable,
            StorageLiteralExpression literal => literal.Literal.CanonicalValue is not null,
            _ => false
        };
        if (possibleField is StorageFieldExpression fieldExpression && inputIsNonNull)
        {
            field = fieldExpression.Field;
            return true;
        }
        field = string.Empty;
        return false;
    }

    private static ResolvedValue? AddAndReturnNull(
        List<StorageMutationPlanIssue> issues,
        string code,
        string path,
        string message)
    {
        Add(issues, code, path, message);
        return null;
    }

    private static void Add(List<StorageMutationPlanIssue> issues, string code, string path, string message) =>
        issues.Add(new StorageMutationPlanIssue(code, path, message));

    private sealed record ResolvedValue(StorageValueType Type, bool Nullable);
}
