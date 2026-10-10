namespace JunexisLang.Compiler.IR;

/// <summary>Przenośne typy wartości logicznego modelu storage.</summary>
public enum StorageValueType
{
    Int64,
    Float64,
    Bool,
    Text,
    Bytes,
    Decimal,
    Timestamp,
    Uuid,
    Json
}

/// <summary>Pole encji logicznej, niezależne od kolumny i typu konkretnej bazy.</summary>
public sealed record StorageModelField(string Name, StorageValueType Type, bool Nullable = false);

/// <summary>Encja logiczna wraz z kluczem identyfikującym rekord.</summary>
public sealed record StorageModelEntity(
    string Name,
    IReadOnlyList<StorageModelField> Fields,
    IReadOnlyList<string> PrimaryKey);

/// <summary>Abstrakcyjny model danych M13.1.</summary>
public sealed record StorageDataModel(
    string SchemaVersion,
    string Name,
    IReadOnlyList<StorageModelEntity> Entities)
{
    public const string CurrentVersion = "junexis.storage.model/0.1";
}

/// <summary>Parametr planu przekazywany oddzielnie od jego struktury.</summary>
public sealed record StorageQueryParameter(string Name, StorageValueType Type, bool Nullable = false);

/// <summary>Wartość zapisana w przenośnej postaci kanonicznej.</summary>
public sealed record StorageQueryLiteral(StorageValueType Type, string? CanonicalValue);

/// <summary>Wyrażenie zwracające pojedynczą wartość.</summary>
public abstract record StorageQueryExpression;

public sealed record StorageFieldExpression(string Field) : StorageQueryExpression;
public sealed record StorageParameterExpression(string Parameter) : StorageQueryExpression;
public sealed record StorageLiteralExpression(StorageQueryLiteral Literal) : StorageQueryExpression;

/// <summary>Operator porównania niezależny od dialektu SQL.</summary>
public enum StorageComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

public enum StorageLogicalOperator
{
    And,
    Or
}

/// <summary>Warunek logiczny planu odczytu.</summary>
public abstract record StorageQueryPredicate;

public sealed record StorageComparisonPredicate(
    StorageQueryExpression Left,
    StorageComparisonOperator Operator,
    StorageQueryExpression Right) : StorageQueryPredicate;

public sealed record StorageLogicalPredicate(
    StorageLogicalOperator Operator,
    IReadOnlyList<StorageQueryPredicate> Terms) : StorageQueryPredicate;

public sealed record StorageNotPredicate(StorageQueryPredicate Inner) : StorageQueryPredicate;
public sealed record StorageNullPredicate(StorageQueryExpression Expression, bool IsNull) : StorageQueryPredicate;

/// <summary>Pojedyncza kolumna logicznego wyniku.</summary>
public sealed record StorageProjection(string Name, StorageQueryExpression Expression);

public enum StorageSortDirection
{
    Ascending,
    Descending
}

public sealed record StorageSortKey(StorageQueryExpression Expression, StorageSortDirection Direction);

/// <summary>Węzeł przenośnej algebry planu zapytania.</summary>
public abstract record StorageQueryPlanNode;

public sealed record StorageScanPlan(string Entity) : StorageQueryPlanNode;
public sealed record StorageFilterPlan(StorageQueryPlanNode Input, StorageQueryPredicate Predicate) : StorageQueryPlanNode;
public sealed record StorageProjectPlan(StorageQueryPlanNode Input, IReadOnlyList<StorageProjection> Projections) : StorageQueryPlanNode;
public sealed record StorageSortPlan(StorageQueryPlanNode Input, IReadOnlyList<StorageSortKey> Keys) : StorageQueryPlanNode;
public sealed record StorageLimitPlan(StorageQueryPlanNode Input, int Count, int Offset = 0) : StorageQueryPlanNode;

/// <summary>Dokument Query Plan IR dla przenośnego odczytu M13.1.</summary>
public sealed record StorageQueryPlan(
    string SchemaVersion,
    string Name,
    string Model,
    IReadOnlyList<StorageQueryParameter> Parameters,
    StorageQueryPlanNode Root)
{
    public const string CurrentVersion = "junexis.storage.query-plan/0.1";
}

/// <summary>Kontrolowany problem walidacji modelu lub planu.</summary>
public sealed record StorageQueryPlanIssue(string Code, string Path, string Message);

/// <summary>Waliduje M13.1 przed obniżeniem planu do providera lub natywnego SQL.</summary>
public static class StorageQueryPlanValidator
{
    /// <summary>Waliduje sam logiczny model przed utworzeniem jakiejkolwiek operacji.</summary>
    public static IReadOnlyList<StorageQueryPlanIssue> ValidateModel(StorageDataModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var issues = new List<StorageQueryPlanIssue>();
        if (!string.Equals(model.SchemaVersion, StorageDataModel.CurrentVersion, StringComparison.Ordinal))
            Add(issues, "SQP001", "/model/schemaVersion", "Nieobsługiwana wersja logicznego modelu danych.");

        var entities = new HashSet<string>(StringComparer.Ordinal);
        for (var entityIndex = 0; entityIndex < model.Entities.Count; entityIndex++)
        {
            var entity = model.Entities[entityIndex];
            var entityPath = $"/model/entities/{entityIndex}";
            if (!entities.Add(entity.Name))
                Add(issues, "SQP010", entityPath + "/name", $"Encja '{entity.Name}' jest zadeklarowana więcej niż raz.");

            var fields = new Dictionary<string, StorageModelField>(StringComparer.Ordinal);
            for (var fieldIndex = 0; fieldIndex < entity.Fields.Count; fieldIndex++)
            {
                var field = entity.Fields[fieldIndex];
                if (!fields.TryAdd(field.Name, field))
                    Add(issues, "SQP011", $"{entityPath}/fields/{fieldIndex}/name", $"Pole '{field.Name}' jest zadeklarowane więcej niż raz.");
            }

            if (entity.PrimaryKey.Count == 0)
                Add(issues, "SQP012", entityPath + "/primaryKey", "Encja musi mieć jawny klucz główny.");
            foreach (var keyField in entity.PrimaryKey)
            {
                if (!fields.TryGetValue(keyField, out var field))
                    Add(issues, "SQP013", entityPath + "/primaryKey", $"Klucz odwołuje się do nieznanego pola '{keyField}'.");
                else if (field.Nullable)
                    Add(issues, "SQP014", entityPath + "/primaryKey", $"Pole klucza '{keyField}' nie może być nullable.");
            }
        }

        return issues;
    }

    public static IReadOnlyList<StorageQueryPlanIssue> Validate(
        StorageDataModel model,
        StorageQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);

        var issues = new List<StorageQueryPlanIssue>(ValidateModel(model));
        if (!string.Equals(plan.SchemaVersion, StorageQueryPlan.CurrentVersion, StringComparison.Ordinal))
            Add(issues, "SQP002", "/plan/schemaVersion", "Nieobsługiwana wersja Query Plan IR.");
        if (!string.Equals(plan.Model, model.Name, StringComparison.Ordinal))
            Add(issues, "SQP003", "/plan/model", "Plan wskazuje inny logiczny model danych.");

        var entities = new Dictionary<string, StorageModelEntity>(StringComparer.Ordinal);
        foreach (var entity in model.Entities)
            entities.TryAdd(entity.Name, entity);

        var parameters = new Dictionary<string, StorageQueryParameter>(StringComparer.Ordinal);
        for (var index = 0; index < plan.Parameters.Count; index++)
        {
            var parameter = plan.Parameters[index];
            if (!parameters.TryAdd(parameter.Name, parameter))
                Add(issues, "SQP020", $"/plan/parameters/{index}/name", $"Parametr '{parameter.Name}' jest zadeklarowany więcej niż raz.");
        }

        var scan = FindScan(plan.Root);
        if (scan is null)
        {
            Add(issues, "SQP030", "/plan/root", "Plan odczytu musi kończyć się pojedynczym węzłem scan.");
            return issues;
        }
        if (!entities.TryGetValue(scan.Entity, out var source))
        {
            Add(issues, "SQP031", "/plan/root/scan/entity", $"Plan odwołuje się do nieznanej encji '{scan.Entity}'.");
            return issues;
        }

        var sourceFields = new Dictionary<string, StorageModelField>(StringComparer.Ordinal);
        foreach (var field in source.Fields)
            sourceFields.TryAdd(field.Name, field);
        ValidateNode(plan.Root, "/plan/root", sourceFields, parameters, issues);
        return issues;
    }

    private static StorageScanPlan? FindScan(StorageQueryPlanNode node) => node switch
    {
        StorageScanPlan scan => scan,
        StorageFilterPlan filter => FindScan(filter.Input),
        StorageProjectPlan project => FindScan(project.Input),
        StorageSortPlan sort => FindScan(sort.Input),
        StorageLimitPlan limit => FindScan(limit.Input),
        _ => null
    };

    private static IReadOnlyDictionary<string, StorageModelField> ValidateNode(
        StorageQueryPlanNode node,
        string path,
        IReadOnlyDictionary<string, StorageModelField> sourceFields,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageQueryPlanIssue> issues)
    {
        switch (node)
        {
            case StorageScanPlan:
                return sourceFields;
            case StorageFilterPlan filter:
                var filteredFields = ValidateNode(filter.Input, path + "/input", sourceFields, parameters, issues);
                ValidatePredicate(filter.Predicate, path + "/predicate", filteredFields, parameters, issues);
                return filteredFields;
            case StorageProjectPlan project:
                var inputFields = ValidateNode(project.Input, path + "/input", sourceFields, parameters, issues);
                var names = new HashSet<string>(StringComparer.Ordinal);
                var projectedFields = new Dictionary<string, StorageModelField>(StringComparer.Ordinal);
                for (var index = 0; index < project.Projections.Count; index++)
                {
                    var projection = project.Projections[index];
                    if (!names.Add(projection.Name))
                        Add(issues, "SQP040", $"{path}/projections/{index}/name", $"Nazwa wyniku '{projection.Name}' występuje więcej niż raz.");
                    var type = ResolveType(projection.Expression, $"{path}/projections/{index}/expression", inputFields, parameters, issues);
                    if (type.HasValue)
                        projectedFields.TryAdd(projection.Name, new StorageModelField(projection.Name, type.Value));
                }
                return projectedFields;
            case StorageSortPlan sort:
                var sortableFields = ValidateNode(sort.Input, path + "/input", sourceFields, parameters, issues);
                if (sort.Keys.Count == 0)
                    Add(issues, "SQP041", path + "/keys", "Węzeł sort wymaga co najmniej jednego klucza.");
                for (var index = 0; index < sort.Keys.Count; index++)
                    ResolveType(sort.Keys[index].Expression, $"{path}/keys/{index}/expression", sortableFields, parameters, issues);
                return sortableFields;
            case StorageLimitPlan limit:
                var limitedFields = ValidateNode(limit.Input, path + "/input", sourceFields, parameters, issues);
                if (limit.Count <= 0)
                    Add(issues, "SQP042", path + "/count", "Limit musi być większy od zera.");
                if (limit.Offset < 0)
                    Add(issues, "SQP043", path + "/offset", "Offset nie może być ujemny.");
                return limitedFields;
            default:
                Add(issues, "SQP099", path, $"Nieznany węzeł Query Plan IR: {node.GetType().Name}.");
                return sourceFields;
        }
    }

    private static void ValidatePredicate(
        StorageQueryPredicate predicate,
        string path,
        IReadOnlyDictionary<string, StorageModelField> fields,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageQueryPlanIssue> issues)
    {
        switch (predicate)
        {
            case StorageComparisonPredicate comparison:
                var left = ResolveType(comparison.Left, path + "/left", fields, parameters, issues);
                var right = ResolveType(comparison.Right, path + "/right", fields, parameters, issues);
                if (left.HasValue && right.HasValue && left != right)
                    Add(issues, "SQP050", path, $"Nie można porównać wartości {left} i {right}.");
                break;
            case StorageLogicalPredicate logical:
                if (logical.Terms.Count < 2)
                    Add(issues, "SQP051", path + "/terms", "Operator logiczny wymaga co najmniej dwóch warunków.");
                for (var index = 0; index < logical.Terms.Count; index++)
                    ValidatePredicate(logical.Terms[index], $"{path}/terms/{index}", fields, parameters, issues);
                break;
            case StorageNotPredicate not:
                ValidatePredicate(not.Inner, path + "/inner", fields, parameters, issues);
                break;
            case StorageNullPredicate nullCheck:
                ResolveType(nullCheck.Expression, path + "/expression", fields, parameters, issues);
                break;
            default:
                Add(issues, "SQP098", path, $"Nieznany warunek Query Plan IR: {predicate.GetType().Name}.");
                break;
        }
    }

    private static StorageValueType? ResolveType(
        StorageQueryExpression expression,
        string path,
        IReadOnlyDictionary<string, StorageModelField> fields,
        IReadOnlyDictionary<string, StorageQueryParameter> parameters,
        List<StorageQueryPlanIssue> issues)
    {
        switch (expression)
        {
            case StorageFieldExpression field:
                if (fields.TryGetValue(field.Field, out var modelField))
                    return modelField.Type;
                Add(issues, "SQP060", path, $"Nieznane pole '{field.Field}'.");
                return null;
            case StorageParameterExpression parameter:
                if (parameters.TryGetValue(parameter.Parameter, out var queryParameter))
                    return queryParameter.Type;
                Add(issues, "SQP061", path, $"Nieznany parametr '{parameter.Parameter}'.");
                return null;
            case StorageLiteralExpression literal:
                return literal.Literal.Type;
            default:
                Add(issues, "SQP097", path, $"Nieznane wyrażenie Query Plan IR: {expression.GetType().Name}.");
                return null;
        }
    }

    private static void Add(List<StorageQueryPlanIssue> issues, string code, string path, string message)
        => issues.Add(new StorageQueryPlanIssue(code, path, message));
}
