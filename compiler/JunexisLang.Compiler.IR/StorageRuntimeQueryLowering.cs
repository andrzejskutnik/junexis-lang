using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.IR;

/// <summary>Wartość parametru dostępna dopiero podczas wywołania operacji.</summary>
public sealed record StorageQueryArgument(string Name, StorageValueType Type, string? CanonicalValue);

/// <summary>Neutralny deskryptor wywołania istniejącego Storage Runtime ABI.</summary>
public sealed record StorageRuntimeQueryInvocation(
    string RuntimeAbi,
    string Action,
    string Operation,
    string Effect,
    IReadOnlyList<string> RequiredCapabilities,
    StorageRuntimeQueryInput Input)
{
    public const string CurrentRuntimeAbi = "junexis.storage.runtime/0.1";
    public const string QueryPlanCapability = "query-plan";
}

/// <summary>Wersjonowane wejście operacji <c>query.plan</c>.</summary>
public sealed record StorageRuntimeQueryInput(
    string SchemaVersion,
    StorageRuntimeModelDocument Model,
    StorageRuntimeQueryDocument Query,
    IReadOnlyList<StorageRuntimeQueryArgument> Arguments)
{
    public const string CurrentVersion = "junexis.storage.query-invocation/0.1";
}

public sealed record StorageRuntimeModelDocument(
    string SchemaVersion,
    string Name,
    IReadOnlyList<StorageRuntimeEntity> Entities);

public sealed record StorageRuntimeEntity(
    string Name,
    IReadOnlyList<StorageRuntimeColumn> Fields,
    IReadOnlyList<string> PrimaryKey);

public sealed record StorageRuntimeColumn(string Name, string Type, bool Nullable);

public sealed record StorageRuntimeQueryDocument(
    string SchemaVersion,
    string Name,
    string Model,
    IReadOnlyList<StorageRuntimeQueryParameter> Parameters,
    string SourceEntity,
    IReadOnlyList<StorageRuntimeQueryStep> Steps,
    IReadOnlyList<StorageRuntimeColumn> Result);

public sealed record StorageRuntimeQueryParameter(string Name, string Type, bool Nullable);
public sealed record StorageRuntimeQueryArgument(
    string Name,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Value);

/// <summary>Jeden krok planu w kolejności wykonania, od źródła do wyniku.</summary>
public sealed record StorageRuntimeQueryStep(
    string Kind,
    StorageRuntimePredicate? Predicate = null,
    IReadOnlyList<StorageRuntimeProjection>? Projections = null,
    IReadOnlyList<StorageRuntimeSortKey>? Keys = null,
    int? Count = null,
    int? Offset = null);

public sealed record StorageRuntimeProjection(string Name, StorageRuntimeExpression Expression);
public sealed record StorageRuntimeSortKey(StorageRuntimeExpression Expression, string Direction);

/// <summary>Jawna unia wyrażenia wire; <c>kind</c> jest stabilnym dyskryminatorem ABI.</summary>
public sealed record StorageRuntimeExpression(
    string Kind,
    string? Name = null,
    StorageRuntimeValue? Literal = null);

public sealed record StorageRuntimeValue(string Type, string? Value);

/// <summary>Jawna unia predykatu wire; nie zawiera składni ani dialektu providera.</summary>
public sealed record StorageRuntimePredicate(
    string Kind,
    StorageRuntimeExpression? Left = null,
    string? Operator = null,
    StorageRuntimeExpression? Right = null,
    IReadOnlyList<StorageRuntimePredicate>? Terms = null,
    StorageRuntimePredicate? Inner = null,
    StorageRuntimeExpression? Expression = null,
    bool? IsNull = null);

public sealed record StorageRuntimeQueryLoweringIssue(string Code, string Path, string Message);

public sealed record StorageRuntimeQueryLoweringResult(
    bool Success,
    StorageRuntimeQueryInvocation? Invocation,
    IReadOnlyList<StorageRuntimeQueryLoweringIssue> Issues);

/// <summary>
/// Obniża sprawdzony Query Plan IR do wejścia operacji <c>query.plan</c> bez
/// wyboru providera, generowania SQL ani uzupełniania deployment bindingu.
/// </summary>
public static class StorageRuntimeQueryLowerer
{
    public static StorageRuntimeQueryLoweringResult Lower(
        StorageDataModel model,
        StorageQueryPlan plan,
        IReadOnlyList<StorageQueryArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(arguments);

        var issues = StorageQueryPlanValidator.Validate(model, plan)
            .Select(issue => new StorageRuntimeQueryLoweringIssue(issue.Code, issue.Path, issue.Message))
            .ToList();
        if (issues.Count > 0)
            return new StorageRuntimeQueryLoweringResult(false, null, issues);

        var boundArguments = BindArguments(plan, arguments, issues);
        if (issues.Count > 0)
            return new StorageRuntimeQueryLoweringResult(false, null, issues);

        try
        {
            var steps = new List<StorageRuntimeQueryStep>();
            var sourceEntity = Flatten(plan.Root, "/query/steps", steps);
            var source = model.Entities.Single(entity => entity.Name == sourceEntity);
            IReadOnlyList<StorageRuntimeColumn> result = source.Fields.Select(Column).ToList();
            foreach (var step in steps)
            {
                if (step.Kind != "project")
                    continue;
                result = step.Projections!
                    .Select(projection => ResolveColumn(
                        projection.Name,
                        projection.Expression,
                        result,
                        plan.Parameters))
                    .ToList();
            }

            var invocation = new StorageRuntimeQueryInvocation(
                StorageRuntimeQueryInvocation.CurrentRuntimeAbi,
                "invoke",
                "query.plan",
                "read",
                [StorageRuntimeQueryInvocation.QueryPlanCapability],
                new StorageRuntimeQueryInput(
                    StorageRuntimeQueryInput.CurrentVersion,
                    new StorageRuntimeModelDocument(
                        model.SchemaVersion,
                        model.Name,
                        model.Entities.Select(entity => new StorageRuntimeEntity(
                            entity.Name,
                            entity.Fields.Select(Column).ToList(),
                            entity.PrimaryKey)).ToList()),
                    new StorageRuntimeQueryDocument(
                        plan.SchemaVersion,
                        plan.Name,
                        plan.Model,
                        plan.Parameters.Select(parameter => new StorageRuntimeQueryParameter(
                            parameter.Name,
                            TypeName(parameter.Type),
                            parameter.Nullable)).ToList(),
                        sourceEntity,
                        steps,
                        result),
                    boundArguments));
            return new StorageRuntimeQueryLoweringResult(true, invocation, []);
        }
        catch (LoweringException ex)
        {
            return new StorageRuntimeQueryLoweringResult(
                false,
                null,
                [new StorageRuntimeQueryLoweringIssue(ex.Code, ex.Path, ex.Message)]);
        }
    }

    private static IReadOnlyList<StorageRuntimeQueryArgument> BindArguments(
        StorageQueryPlan plan,
        IReadOnlyList<StorageQueryArgument> arguments,
        List<StorageRuntimeQueryLoweringIssue> issues)
    {
        var supplied = new Dictionary<string, StorageQueryArgument>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!supplied.TryAdd(argument.Name, argument))
                Add(issues, "SQR011", $"/arguments/{index}/name", $"Argument '{argument.Name}' został przekazany więcej niż raz.");
        }

        var declared = plan.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!declared.ContainsKey(argument.Name))
                Add(issues, "SQR012", $"/arguments/{index}/name", $"Operacja nie deklaruje argumentu '{argument.Name}'.");
        }

        var result = new List<StorageRuntimeQueryArgument>();
        for (var index = 0; index < plan.Parameters.Count; index++)
        {
            var parameter = plan.Parameters[index];
            var path = $"/arguments/{parameter.Name}";
            if (!supplied.TryGetValue(parameter.Name, out var argument))
            {
                Add(issues, "SQR010", path, $"Brakuje argumentu '{parameter.Name}'.");
                continue;
            }
            if (argument.Type != parameter.Type)
            {
                Add(issues, "SQR013", path, $"Argument '{parameter.Name}' ma typ {argument.Type}, oczekiwano {parameter.Type}.");
                continue;
            }
            if (argument.CanonicalValue is null)
            {
                if (!parameter.Nullable)
                    Add(issues, "SQR014", path, $"Argument '{parameter.Name}' nie jest nullable.");
                else
                    result.Add(new StorageRuntimeQueryArgument(parameter.Name, TypeName(parameter.Type), null));
                continue;
            }
            if (!TryNormalize(parameter.Type, argument.CanonicalValue, out var normalized))
            {
                Add(issues, "SQR015", path, $"Argument '{parameter.Name}' nie ma poprawnej kanonicznej wartości typu {parameter.Type}.");
                continue;
            }
            result.Add(new StorageRuntimeQueryArgument(parameter.Name, TypeName(parameter.Type), normalized));
        }
        return result;
    }

    private static string Flatten(
        StorageQueryPlanNode node,
        string path,
        List<StorageRuntimeQueryStep> steps)
    {
        switch (node)
        {
            case StorageScanPlan scan:
                return scan.Entity;
            case StorageFilterPlan filter:
                var filterSource = Flatten(filter.Input, path, steps);
                steps.Add(new StorageRuntimeQueryStep(
                    "filter",
                    Predicate: LowerPredicate(filter.Predicate, $"{path}/{steps.Count}/predicate")));
                return filterSource;
            case StorageProjectPlan project:
                var projectSource = Flatten(project.Input, path, steps);
                steps.Add(new StorageRuntimeQueryStep(
                    "project",
                    Projections: project.Projections.Select((projection, index) => new StorageRuntimeProjection(
                        projection.Name,
                        LowerExpression(projection.Expression, $"{path}/{steps.Count}/projections/{index}/expression"))).ToList()));
                return projectSource;
            case StorageSortPlan sort:
                var sortSource = Flatten(sort.Input, path, steps);
                steps.Add(new StorageRuntimeQueryStep(
                    "sort",
                    Keys: sort.Keys.Select((key, index) => new StorageRuntimeSortKey(
                        LowerExpression(key.Expression, $"{path}/{steps.Count}/keys/{index}/expression"),
                        key.Direction == StorageSortDirection.Ascending ? "asc" : "desc")).ToList()));
                return sortSource;
            case StorageLimitPlan limit:
                var limitSource = Flatten(limit.Input, path, steps);
                steps.Add(new StorageRuntimeQueryStep("limit", Count: limit.Count, Offset: limit.Offset));
                return limitSource;
            default:
                throw new LoweringException("SQR099", path, $"Nieznany węzeł Query Plan IR: {node.GetType().Name}.");
        }
    }

    private static StorageRuntimePredicate LowerPredicate(StorageQueryPredicate predicate, string path) => predicate switch
    {
        StorageComparisonPredicate comparison => new StorageRuntimePredicate(
            "comparison",
            Left: LowerExpression(comparison.Left, path + "/left"),
            Operator: ComparisonName(comparison.Operator),
            Right: LowerExpression(comparison.Right, path + "/right")),
        StorageLogicalPredicate logical => new StorageRuntimePredicate(
            "logical",
            Operator: logical.Operator == StorageLogicalOperator.And ? "and" : "or",
            Terms: logical.Terms.Select((term, index) => LowerPredicate(term, $"{path}/terms/{index}")).ToList()),
        StorageNotPredicate not => new StorageRuntimePredicate(
            "not",
            Inner: LowerPredicate(not.Inner, path + "/inner")),
        StorageNullPredicate nullCheck => new StorageRuntimePredicate(
            "null",
            Expression: LowerExpression(nullCheck.Expression, path + "/expression"),
            IsNull: nullCheck.IsNull),
        _ => throw new LoweringException("SQR098", path, $"Nieznany predykat Query Plan IR: {predicate.GetType().Name}.")
    };

    private static StorageRuntimeExpression LowerExpression(StorageQueryExpression expression, string path) => expression switch
    {
        StorageFieldExpression field => new StorageRuntimeExpression("field", Name: field.Field),
        StorageParameterExpression parameter => new StorageRuntimeExpression("parameter", Name: parameter.Parameter),
        StorageLiteralExpression { Literal.CanonicalValue: null } => throw new LoweringException(
            "SQR020", path, "Literał null musi być opisany jawnym predykatem null."),
        StorageLiteralExpression literal when TryNormalize(
            literal.Literal.Type,
            literal.Literal.CanonicalValue!,
            out var normalized) => new StorageRuntimeExpression(
                "literal",
                Literal: new StorageRuntimeValue(TypeName(literal.Literal.Type), normalized)),
        StorageLiteralExpression literal => throw new LoweringException(
            "SQR021", path, $"Literał nie ma poprawnej kanonicznej wartości typu {literal.Literal.Type}."),
        _ => throw new LoweringException("SQR097", path, $"Nieznane wyrażenie Query Plan IR: {expression.GetType().Name}.")
    };

    private static StorageRuntimeColumn ResolveColumn(
        string name,
        StorageRuntimeExpression expression,
        IReadOnlyList<StorageRuntimeColumn> fields,
        IReadOnlyList<StorageQueryParameter> parameters) => expression.Kind switch
        {
            "field" => fields.Single(field => field.Name == expression.Name) with { Name = name },
            "parameter" => parameters
                .Where(parameter => parameter.Name == expression.Name)
                .Select(parameter => new StorageRuntimeColumn(name, TypeName(parameter.Type), parameter.Nullable))
                .Single(),
            "literal" => new StorageRuntimeColumn(name, expression.Literal!.Type, false),
            _ => throw new LoweringException("SQR096", "/query/result", $"Nieznany rodzaj wyrażenia '{expression.Kind}'.")
        };

    private static StorageRuntimeColumn Column(StorageModelField field)
        => new(field.Name, TypeName(field.Type), field.Nullable);

    private static string TypeName(StorageValueType type) => StorageRuntimeValueCodec.TypeName(type);

    private static string ComparisonName(StorageComparisonOperator op) => op switch
    {
        StorageComparisonOperator.Equal => "eq",
        StorageComparisonOperator.NotEqual => "ne",
        StorageComparisonOperator.LessThan => "lt",
        StorageComparisonOperator.LessThanOrEqual => "le",
        StorageComparisonOperator.GreaterThan => "gt",
        StorageComparisonOperator.GreaterThanOrEqual => "ge",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
    };

    private static bool TryNormalize(StorageValueType type, string value, out string normalized) =>
        StorageRuntimeValueCodec.TryNormalize(type, value, out normalized);

    private static void Add(
        List<StorageRuntimeQueryLoweringIssue> issues,
        string code,
        string path,
        string message)
        => issues.Add(new StorageRuntimeQueryLoweringIssue(code, path, message));

    private sealed class LoweringException(string code, string path, string message) : Exception(message)
    {
        internal string Code { get; } = code;
        internal string Path { get; } = path;
    }
}

/// <summary>Kanoniczna reprezentacja JSON i fingerprint deskryptora M13.3.</summary>
public static class StorageRuntimeQueryInvocationJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize(StorageRuntimeQueryInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return JsonSerializer.Serialize(invocation, Options);
    }

    public static string FingerprintSha256(StorageRuntimeQueryInvocation invocation)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(invocation))));
}
