using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.IR;

/// <summary>Wartość parametru mutacji dostępna dopiero podczas wywołania.</summary>
public sealed record StorageMutationArgument(string Name, StorageValueType Type, string? CanonicalValue);

/// <summary>Neutralny deskryptor wywołania Mutation Plan przez Storage Runtime ABI.</summary>
public sealed record StorageRuntimeMutationInvocation(
    string RuntimeAbi,
    string Action,
    string Operation,
    string Effect,
    IReadOnlyList<string> RequiredCapabilities,
    StorageRuntimeMutationInput Input)
{
    /// <summary>Wersja istniejącego ABI używana przez M14.3.</summary>
    public const string CurrentRuntimeAbi = "junexis.storage.runtime/0.1";

    /// <summary>Capability wymagane od providera wykonującego Mutation Plan.</summary>
    public const string MutationPlanCapability = "mutation-plan";

    /// <summary>Klasa błędu ABI dla zapisu, którego skutku nie można rozstrzygnąć.</summary>
    public const string UnknownOutcomeErrorClass = "unknown-outcome";
}

/// <summary>Wersjonowane wejście operacji <c>mutation.plan</c>.</summary>
public sealed record StorageRuntimeMutationInput(
    string SchemaVersion,
    StorageRuntimeModelDocument Model,
    StorageRuntimeMutationDocument Mutation,
    IReadOnlyList<StorageRuntimeMutationArgument> Arguments)
{
    /// <summary>Bieżąca wersja wejścia Mutation Runtime.</summary>
    public const string CurrentVersion = "junexis.storage.mutation-invocation/0.1";
}

/// <summary>Wire dokument jednej nazwanej mutacji.</summary>
public sealed record StorageRuntimeMutationDocument(
    string SchemaVersion,
    string Name,
    string Model,
    string Entity,
    string Kind,
    string Cardinality,
    IReadOnlyList<StorageRuntimeMutationParameter> Parameters,
    IReadOnlyList<StorageRuntimeMutationAssignment> Assignments,
    StorageRuntimePredicate? Predicate);

/// <summary>Parametr mutacji w przenośnej postaci wire.</summary>
public sealed record StorageRuntimeMutationParameter(string Name, string Type, bool Nullable);

/// <summary>Argument mutacji w przenośnej postaci wire.</summary>
public sealed record StorageRuntimeMutationArgument(
    string Name,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Value);

/// <summary>Przypisanie logicznego pola w wire dokumencie mutacji.</summary>
public sealed record StorageRuntimeMutationAssignment(string Field, StorageRuntimeExpression Value);

/// <summary>Kontrolowany problem loweringu Mutation Runtime.</summary>
public sealed record StorageRuntimeMutationLoweringIssue(string Code, string Path, string Message);

/// <summary>Wynik loweringu Mutation Plan do deskryptora Runtime.</summary>
public sealed record StorageRuntimeMutationLoweringResult(
    bool Success,
    StorageRuntimeMutationInvocation? Invocation,
    IReadOnlyList<StorageRuntimeMutationLoweringIssue> Issues);

/// <summary>Jawny, wyłącznie pomyślny wynik wykonania <c>mutation.plan</c>.</summary>
public sealed record StorageRuntimeMutationResult(
    string SchemaVersion,
    string Outcome,
    long AffectedRows)
{
    /// <summary>Bieżąca wersja wyniku mutacji.</summary>
    public const string CurrentVersion = "junexis.storage.mutation-result/0.1";

    /// <summary>Co najmniej jeden rekord został logicznie zmieniony.</summary>
    public const string Applied = "applied";

    /// <summary>Poprawna operacja nie znalazła rekordu do zmiany.</summary>
    public const string NotApplied = "not-applied";
}

/// <summary>Problem kontroli jawnego wyniku mutacji.</summary>
public sealed record StorageRuntimeMutationResultIssue(string Code, string Path, string Message);

/// <summary>Obniża sprawdzony Mutation Plan bez wyboru providera, SQL i bindingu.</summary>
public static class StorageRuntimeMutationLowerer
{
    /// <summary>Tworzy kanoniczny deskryptor <c>mutation.plan</c> albo kontrolowane problemy.</summary>
    public static StorageRuntimeMutationLoweringResult Lower(
        StorageDataModel model,
        StorageMutationPlan plan,
        IReadOnlyList<StorageMutationArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(arguments);

        var issues = StorageMutationPlanValidator.Validate(model, plan)
            .Select(issue => new StorageRuntimeMutationLoweringIssue(issue.Code, issue.Path, issue.Message))
            .ToList();
        if (issues.Count > 0)
            return new StorageRuntimeMutationLoweringResult(false, null, issues);

        var boundArguments = BindArguments(plan, arguments, issues);
        if (issues.Count > 0)
            return new StorageRuntimeMutationLoweringResult(false, null, issues);

        try
        {
            var invocation = new StorageRuntimeMutationInvocation(
                StorageRuntimeMutationInvocation.CurrentRuntimeAbi,
                "invoke",
                "mutation.plan",
                "write",
                [StorageRuntimeMutationInvocation.MutationPlanCapability],
                new StorageRuntimeMutationInput(
                    StorageRuntimeMutationInput.CurrentVersion,
                    new StorageRuntimeModelDocument(
                        model.SchemaVersion,
                        model.Name,
                        model.Entities.Select(entity => new StorageRuntimeEntity(
                            entity.Name,
                            entity.Fields.Select(Column).ToList(),
                            entity.PrimaryKey)).ToList()),
                    new StorageRuntimeMutationDocument(
                        plan.SchemaVersion,
                        plan.Name,
                        plan.Model,
                        plan.Entity,
                        KindName(plan.Kind),
                        CardinalityName(plan.Cardinality),
                        plan.Parameters.Select(parameter => new StorageRuntimeMutationParameter(
                            parameter.Name,
                            StorageRuntimeValueCodec.TypeName(parameter.Type),
                            parameter.Nullable)).ToList(),
                        plan.Assignments.Select((assignment, index) => new StorageRuntimeMutationAssignment(
                            assignment.Field,
                            LowerAssignmentValue(assignment.Value, $"/mutation/assignments/{index}/value"))).ToList(),
                        plan.Predicate is null
                            ? null
                            : LowerPredicate(plan.Predicate, "/mutation/predicate")),
                    boundArguments));
            return new StorageRuntimeMutationLoweringResult(true, invocation, []);
        }
        catch (MutationLoweringException exception)
        {
            return new StorageRuntimeMutationLoweringResult(
                false,
                null,
                [new StorageRuntimeMutationLoweringIssue(exception.Code, exception.Path, exception.Message)]);
        }
    }

    /// <summary>
    /// Sprawdza pomyślny wynik providera. Wynik nieznany musi przejść osobną
    /// klasą błędu <c>unknown-outcome</c>, a nie przez ten dokument.
    /// </summary>
    public static IReadOnlyList<StorageRuntimeMutationResultIssue> ValidateResult(
        StorageMutationPlan plan,
        StorageRuntimeMutationResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);

        var issues = new List<StorageRuntimeMutationResultIssue>();
        if (!string.Equals(result.SchemaVersion, StorageRuntimeMutationResult.CurrentVersion, StringComparison.Ordinal))
            AddResult(issues, "SMR030", "/result/schemaVersion", "Nieobsługiwana wersja wyniku mutacji.");
        if (result.AffectedRows < 0)
            AddResult(issues, "SMR031", "/result/affectedRows", "Liczba zmienionych rekordów nie może być ujemna.");
        if (result.Outcome == StorageRuntimeMutationResult.Applied && result.AffectedRows == 0)
            AddResult(issues, "SMR032", "/result", "Wynik applied wymaga co najmniej jednego zmienionego rekordu.");
        else if (result.Outcome == StorageRuntimeMutationResult.NotApplied && result.AffectedRows != 0)
            AddResult(issues, "SMR033", "/result", "Wynik not-applied wymaga affectedRows równego zero.");
        else if (result.Outcome is not StorageRuntimeMutationResult.Applied and not StorageRuntimeMutationResult.NotApplied)
            AddResult(issues, "SMR034", "/result/outcome", "Nieznany skutek zapisu musi być błędem ABI unknown-outcome, nie wynikiem sukcesu.");

        if (plan.Cardinality == StorageMutationCardinality.Single && result.AffectedRows > 1)
            AddResult(issues, "SMR035", "/result/affectedRows", "Mutacja Single nie może zmienić więcej niż jednego rekordu.");
        if (plan.Kind == StorageMutationKind.Insert &&
            (result.Outcome != StorageRuntimeMutationResult.Applied || result.AffectedRows != 1))
        {
            AddResult(issues, "SMR036", "/result", "Pomyślny Insert musi raportować applied i dokładnie jeden rekord.");
        }
        return issues;
    }

    private static IReadOnlyList<StorageRuntimeMutationArgument> BindArguments(
        StorageMutationPlan plan,
        IReadOnlyList<StorageMutationArgument> arguments,
        List<StorageRuntimeMutationLoweringIssue> issues)
    {
        var supplied = new Dictionary<string, StorageMutationArgument>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!supplied.TryAdd(argument.Name, argument))
                Add(issues, "SMR011", $"/arguments/{index}/name", $"Argument '{argument.Name}' został przekazany więcej niż raz.");
        }

        var declared = plan.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!declared.ContainsKey(argument.Name))
                Add(issues, "SMR012", $"/arguments/{index}/name", $"Mutacja nie deklaruje argumentu '{argument.Name}'.");
        }

        var result = new List<StorageRuntimeMutationArgument>();
        foreach (var parameter in plan.Parameters)
        {
            var path = $"/arguments/{parameter.Name}";
            if (!supplied.TryGetValue(parameter.Name, out var argument))
            {
                Add(issues, "SMR010", path, $"Brakuje argumentu '{parameter.Name}'.");
                continue;
            }
            if (argument.Type != parameter.Type)
            {
                Add(issues, "SMR013", path, $"Argument '{parameter.Name}' ma typ {argument.Type}, oczekiwano {parameter.Type}.");
                continue;
            }
            if (argument.CanonicalValue is null)
            {
                if (!parameter.Nullable)
                    Add(issues, "SMR014", path, $"Argument '{parameter.Name}' nie jest nullable.");
                else
                    result.Add(new StorageRuntimeMutationArgument(
                        parameter.Name,
                        StorageRuntimeValueCodec.TypeName(parameter.Type),
                        null));
                continue;
            }
            if (!StorageRuntimeValueCodec.TryNormalize(parameter.Type, argument.CanonicalValue, out var normalized))
            {
                Add(issues, "SMR015", path, $"Argument '{parameter.Name}' nie ma poprawnej kanonicznej wartości typu {parameter.Type}.");
                continue;
            }
            result.Add(new StorageRuntimeMutationArgument(
                parameter.Name,
                StorageRuntimeValueCodec.TypeName(parameter.Type),
                normalized));
        }
        return result;
    }

    private static StorageRuntimeExpression LowerAssignmentValue(StorageQueryExpression expression, string path) => expression switch
    {
        StorageParameterExpression parameter => new StorageRuntimeExpression("parameter", Name: parameter.Parameter),
        StorageLiteralExpression { Literal.CanonicalValue: null } literal => new StorageRuntimeExpression(
            "literal",
            Literal: new StorageRuntimeValue(StorageRuntimeValueCodec.TypeName(literal.Literal.Type), null)),
        StorageLiteralExpression literal when StorageRuntimeValueCodec.TryNormalize(
            literal.Literal.Type,
            literal.Literal.CanonicalValue!,
            out var normalized) => new StorageRuntimeExpression(
                "literal",
                Literal: new StorageRuntimeValue(StorageRuntimeValueCodec.TypeName(literal.Literal.Type), normalized)),
        StorageLiteralExpression literal => throw new MutationLoweringException(
            "SMR021", path, $"Literał nie ma poprawnej kanonicznej wartości typu {literal.Literal.Type}."),
        _ => throw new MutationLoweringException("SMR097", path, $"Nieznana wartość przypisania: {expression.GetType().Name}.")
    };

    private static StorageRuntimePredicate LowerPredicate(StorageQueryPredicate predicate, string path) => predicate switch
    {
        StorageComparisonPredicate comparison => new StorageRuntimePredicate(
            "comparison",
            Left: LowerPredicateValue(comparison.Left, path + "/left"),
            Operator: ComparisonName(comparison.Operator),
            Right: LowerPredicateValue(comparison.Right, path + "/right")),
        StorageLogicalPredicate logical => new StorageRuntimePredicate(
            "logical",
            Operator: logical.Operator == StorageLogicalOperator.And ? "and" : "or",
            Terms: logical.Terms.Select((term, index) => LowerPredicate(term, $"{path}/terms/{index}")).ToList()),
        StorageNotPredicate not => new StorageRuntimePredicate("not", Inner: LowerPredicate(not.Inner, path + "/inner")),
        StorageNullPredicate nullCheck => new StorageRuntimePredicate(
            "null",
            Expression: LowerPredicateValue(nullCheck.Expression, path + "/expression"),
            IsNull: nullCheck.IsNull),
        _ => throw new MutationLoweringException("SMR098", path, $"Nieznany predykat Mutation Plan IR: {predicate.GetType().Name}.")
    };

    private static StorageRuntimeExpression LowerPredicateValue(StorageQueryExpression expression, string path) => expression switch
    {
        StorageFieldExpression field => new StorageRuntimeExpression("field", Name: field.Field),
        StorageParameterExpression parameter => new StorageRuntimeExpression("parameter", Name: parameter.Parameter),
        StorageLiteralExpression { Literal.CanonicalValue: null } => throw new MutationLoweringException(
            "SMR020", path, "Literał null musi być opisany jawnym predykatem null."),
        StorageLiteralExpression literal when StorageRuntimeValueCodec.TryNormalize(
            literal.Literal.Type,
            literal.Literal.CanonicalValue!,
            out var normalized) => new StorageRuntimeExpression(
                "literal",
                Literal: new StorageRuntimeValue(StorageRuntimeValueCodec.TypeName(literal.Literal.Type), normalized)),
        StorageLiteralExpression literal => throw new MutationLoweringException(
            "SMR021", path, $"Literał nie ma poprawnej kanonicznej wartości typu {literal.Literal.Type}."),
        _ => throw new MutationLoweringException("SMR097", path, $"Nieznane wyrażenie Mutation Plan IR: {expression.GetType().Name}.")
    };

    private static StorageRuntimeColumn Column(StorageModelField field) =>
        new(field.Name, StorageRuntimeValueCodec.TypeName(field.Type), field.Nullable);

    private static string KindName(StorageMutationKind kind) => kind switch
    {
        StorageMutationKind.Insert => "insert",
        StorageMutationKind.Update => "update",
        StorageMutationKind.Delete => "delete",
        _ => throw new MutationLoweringException("SMR099", "/mutation/kind", $"Nieznany rodzaj mutacji '{kind}'.")
    };

    private static string CardinalityName(StorageMutationCardinality cardinality) => cardinality switch
    {
        StorageMutationCardinality.Single => "single",
        StorageMutationCardinality.Many => "many",
        _ => throw new MutationLoweringException("SMR096", "/mutation/cardinality", $"Nieznany zasięg mutacji '{cardinality}'.")
    };

    private static string ComparisonName(StorageComparisonOperator comparison) => comparison switch
    {
        StorageComparisonOperator.Equal => "eq",
        StorageComparisonOperator.NotEqual => "ne",
        StorageComparisonOperator.LessThan => "lt",
        StorageComparisonOperator.LessThanOrEqual => "le",
        StorageComparisonOperator.GreaterThan => "gt",
        StorageComparisonOperator.GreaterThanOrEqual => "ge",
        _ => throw new MutationLoweringException("SMR095", "/mutation/predicate", $"Nieznany operator porównania '{comparison}'.")
    };

    private static void Add(
        List<StorageRuntimeMutationLoweringIssue> issues,
        string code,
        string path,
        string message) => issues.Add(new StorageRuntimeMutationLoweringIssue(code, path, message));

    private static void AddResult(
        List<StorageRuntimeMutationResultIssue> issues,
        string code,
        string path,
        string message) => issues.Add(new StorageRuntimeMutationResultIssue(code, path, message));

    private sealed class MutationLoweringException(string code, string path, string message) : Exception(message)
    {
        internal string Code { get; } = code;
        internal string Path { get; } = path;
    }
}

/// <summary>Kanoniczny JSON i fingerprint deskryptora Mutation Runtime.</summary>
public static class StorageRuntimeMutationInvocationJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Serializuje deskryptor do deterministycznego JSON.</summary>
    public static string Serialize(StorageRuntimeMutationInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return JsonSerializer.Serialize(invocation, Options);
    }

    /// <summary>Zwraca lowercase SHA-256 kanonicznego JSON deskryptora.</summary>
    public static string FingerprintSha256(StorageRuntimeMutationInvocation invocation) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(invocation))));
}
