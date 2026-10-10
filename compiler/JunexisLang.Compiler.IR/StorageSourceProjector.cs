using System.Globalization;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.IR;

/// <summary>Wynik projekcji źródła storage do neutralnych kontraktów M13.1/M14.1.</summary>
public sealed record StorageSourceProjectionResult(
    bool Success,
    IReadOnlyList<StorageDataModel> Models,
    IReadOnlyList<StorageQueryPlan> Plans,
    IReadOnlyList<StorageMutationPlan> MutationPlans,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>Projektuje query i mutation bez wiedzy o providerze, SQL ani backendzie targetu.</summary>
public static class StorageSourceProjector
{
    public static StorageSourceProjectionResult Project(IRDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var models = new List<StorageDataModel>();
        var plans = new List<StorageQueryPlan>();
        var mutationPlans = new List<StorageMutationPlan>();
        var diagnostics = new List<Diagnostic>();
        var modelNames = new HashSet<string>(StringComparer.Ordinal);

        for (var storageIndex = 0; storageIndex < document.StorageDeclarations.Count; storageIndex++)
        {
            var declaration = document.StorageDeclarations[storageIndex];
            var storagePath = $"/StorageDeclarations[{storageIndex}]";
            if (!modelNames.Add(declaration.Name))
            {
                diagnostics.Add(Error(
                    "SQP080",
                    $"Logiczny model storage '{declaration.Name}' jest zadeklarowany więcej niż raz.",
                    Origin(document, storagePath)));
                continue;
            }

            var queryNames = new HashSet<string>(StringComparer.Ordinal);
            var duplicateQueries = new HashSet<int>();
            for (var queryIndex = 0; queryIndex < declaration.Queries.Count; queryIndex++)
            {
                var query = declaration.Queries[queryIndex];
                var queryPath = $"{storagePath}/Queries[{queryIndex}]";
                if (!queryNames.Add(query.Name))
                {
                    duplicateQueries.Add(queryIndex);
                    diagnostics.Add(Error(
                        "SQP081",
                        $"Operacja storage '{query.Name}' jest zadeklarowana więcej niż raz w modelu '{declaration.Name}'.",
                        Origin(document, queryPath)));
                }
            }

            var duplicateMutations = new HashSet<int>();
            for (var mutationIndex = 0; mutationIndex < declaration.Mutations.Count; mutationIndex++)
            {
                var mutation = declaration.Mutations[mutationIndex];
                var mutationPath = $"{storagePath}/Mutations[{mutationIndex}]";
                if (!queryNames.Add(mutation.Name))
                {
                    duplicateMutations.Add(mutationIndex);
                    diagnostics.Add(Error(
                        "SMP080",
                        $"Operacja storage '{mutation.Name}' jest zadeklarowana więcej niż raz w modelu '{declaration.Name}'.",
                        Origin(document, mutationPath),
                        "StorageMutation"));
                }
            }

            var model = ProjectModel(document, declaration, storagePath, diagnostics);
            if (model is null)
                continue;
            models.Add(model);

            for (var queryIndex = 0; queryIndex < declaration.Queries.Count; queryIndex++)
            {
                if (duplicateQueries.Contains(queryIndex))
                    continue;
                var query = declaration.Queries[queryIndex];
                var queryPath = $"{storagePath}/Queries[{queryIndex}]";
                var plan = ProjectQuery(document, model, query, queryPath, diagnostics);
                if (plan is not null)
                    plans.Add(plan);
            }

            for (var mutationIndex = 0; mutationIndex < declaration.Mutations.Count; mutationIndex++)
            {
                if (duplicateMutations.Contains(mutationIndex))
                    continue;
                var mutation = declaration.Mutations[mutationIndex];
                var mutationPath = $"{storagePath}/Mutations[{mutationIndex}]";
                var plan = ProjectMutation(document, model, mutation, mutationPath, diagnostics);
                if (plan is not null)
                    mutationPlans.Add(plan);
            }
        }

        return new StorageSourceProjectionResult(
            diagnostics.Count == 0,
            models,
            plans,
            mutationPlans,
            diagnostics);
    }

    private static StorageDataModel? ProjectModel(
        IRDocument document,
        IRStorageDeclaration declaration,
        string storagePath,
        List<Diagnostic> diagnostics)
    {
        var before = diagnostics.Count;
        var entities = new List<StorageModelEntity>();
        for (var entityIndex = 0; entityIndex < declaration.Entities.Count; entityIndex++)
        {
            var entity = declaration.Entities[entityIndex];
            var entityPath = $"{storagePath}/Entities[{entityIndex}]";
            if (entity.PrimaryKeys.Count > 1)
            {
                diagnostics.Add(Error(
                    "SQP071",
                    $"Encja '{entity.Name}' ma więcej niż jedną deklarację key(...).",
                    Origin(document, entityPath + "/PrimaryKeys[1]")));
                continue;
            }

            var fields = new List<StorageModelField>();
            for (var fieldIndex = 0; fieldIndex < entity.Fields.Count; fieldIndex++)
            {
                var field = entity.Fields[fieldIndex];
                var fieldPath = $"{entityPath}/Fields[{fieldIndex}]";
                if (!TryProjectType(field.Type, out var type, out var nullable))
                {
                    diagnostics.Add(Error(
                        "SQP072",
                        $"Pole '{entity.Name}.{field.Name}' używa typu, którego Storage Model 0.1 nie potrafi zachować.",
                        Origin(document, fieldPath)));
                    continue;
                }
                fields.Add(new StorageModelField(field.Name, type, nullable));
            }

            entities.Add(new StorageModelEntity(
                entity.Name,
                fields,
                entity.PrimaryKeys.SingleOrDefault()?.Fields ?? []));
        }

        var model = new StorageDataModel(StorageDataModel.CurrentVersion, declaration.Name, entities);
        foreach (var issue in StorageQueryPlanValidator.ValidateModel(model))
            diagnostics.Add(Error(issue.Code, issue.Message, Origin(document, storagePath)));

        return diagnostics.Count == before ? model : null;
    }

    private static StorageQueryPlan? ProjectQuery(
        IRDocument document,
        StorageDataModel model,
        IRStorageQuery query,
        string queryPath,
        List<Diagnostic> diagnostics)
    {
        var before = diagnostics.Count;
        var parameters = new List<StorageQueryParameter>();
        for (var parameterIndex = 0; parameterIndex < query.Params.Count; parameterIndex++)
        {
            var parameter = query.Params[parameterIndex];
            if (!TryProjectType(parameter.Type, out var type, out var nullable))
            {
                diagnostics.Add(Error(
                    "SQP073",
                    $"Parametr '{query.Name}.{parameter.Name}' używa nieprzenośnego typu storage.",
                    Origin(document, $"{queryPath}/Params[{parameterIndex}]")));
                continue;
            }
            parameters.Add(new StorageQueryParameter(parameter.Name, type, nullable));
        }

        StorageQueryPlanNode root = new StorageScanPlan(query.SourceEntity);
        for (var stepIndex = 0; stepIndex < query.Steps.Count; stepIndex++)
        {
            var step = query.Steps[stepIndex];
            var stepPath = $"{queryPath}/Steps[{stepIndex}]";
            try
            {
                root = step switch
                {
                    IRStorageFilterStep filter => new StorageFilterPlan(root, ProjectPredicate(filter.Predicate)),
                    IRStorageProjectStep project => new StorageProjectPlan(
                        root,
                        project.Projections.Select(item => new StorageProjection(
                            item.Alias ?? item.Field,
                            new StorageFieldExpression(item.Field))).ToList()),
                    IRStorageSortStep sort => new StorageSortPlan(
                        root,
                        sort.Keys.Select(key => new StorageSortKey(
                            new StorageFieldExpression(key.Field),
                            key.Descending ? StorageSortDirection.Descending : StorageSortDirection.Ascending)).ToList()),
                    IRStorageLimitStep limit when limit.Count <= int.MaxValue && limit.Offset <= int.MaxValue =>
                        new StorageLimitPlan(root, (int)limit.Count, (int)limit.Offset),
                    IRStorageLimitStep => throw new SourceProjectionException(
                        "SQP074", "Limit i offset muszą mieścić się w zakresie Int32."),
                    _ => throw new SourceProjectionException(
                        "SQP075", $"Nieobsługiwany krok operacji storage: {step.GetType().Name}.")
                };
            }
            catch (SourceProjectionException ex)
            {
                diagnostics.Add(Error(ex.Code, ex.Message, Origin(document, stepPath)));
            }
        }

        var plan = new StorageQueryPlan(
            StorageQueryPlan.CurrentVersion,
            query.Name,
            model.Name,
            parameters,
            root);
        foreach (var issue in StorageQueryPlanValidator.Validate(model, plan))
            diagnostics.Add(Error(issue.Code, issue.Message, Origin(document, queryPath)));

        return diagnostics.Count == before ? plan : null;
    }

    private static StorageQueryPredicate ProjectPredicate(IRExpr expression) => expression switch
    {
        IRBinaryExpr { Op: "==" or "!=" or "<" or "<=" or ">" or ">=" } comparison =>
            new StorageComparisonPredicate(
                ProjectValue(comparison.Left),
                Comparison(comparison.Op),
                ProjectValue(comparison.Right)),
        IRBinaryExpr { Op: "and" or "&&" or "or" or "||" } logical =>
            new StorageLogicalPredicate(
                logical.Op is "and" or "&&" ? StorageLogicalOperator.And : StorageLogicalOperator.Or,
                [ProjectPredicate(logical.Left), ProjectPredicate(logical.Right)]),
        IRUnaryExpr { Op: "not" or "!" } not => new StorageNotPredicate(ProjectPredicate(not.Operand)),
        _ => throw new SourceProjectionException(
            "SQP076",
            "Filter obsługuje porównania, and, or, not, pola, params.<nazwa> i literały.")
    };

    private static StorageMutationPlan? ProjectMutation(
        IRDocument document,
        StorageDataModel model,
        IRStorageMutation mutation,
        string mutationPath,
        List<Diagnostic> diagnostics)
    {
        var before = diagnostics.Count;
        var parameters = new List<StorageQueryParameter>();
        for (var parameterIndex = 0; parameterIndex < mutation.Params.Count; parameterIndex++)
        {
            var parameter = mutation.Params[parameterIndex];
            if (!TryProjectType(parameter.Type, out var type, out var nullable))
            {
                diagnostics.Add(Error(
                    "SMP070",
                    $"Parametr '{mutation.Name}.{parameter.Name}' używa nieprzenośnego typu storage.",
                    Origin(document, $"{mutationPath}/Params[{parameterIndex}]"),
                    "StorageMutation"));
                continue;
            }
            parameters.Add(new StorageQueryParameter(parameter.Name, type, nullable));
        }

        var assignments = new List<StorageMutationAssignment>();
        for (var assignmentIndex = 0; assignmentIndex < mutation.Assignments.Count; assignmentIndex++)
        {
            var assignment = mutation.Assignments[assignmentIndex];
            try
            {
                assignments.Add(new StorageMutationAssignment(
                    assignment.Field,
                    ProjectMutationInput(assignment.Value)));
            }
            catch (SourceProjectionException exception)
            {
                diagnostics.Add(Error(
                    exception.Code,
                    exception.Message,
                    Origin(document, $"{mutationPath}/Assignments[{assignmentIndex}]"),
                    "StorageMutation"));
            }
        }

        StorageQueryPredicate? predicate = null;
        if (mutation.Predicate is not null)
        {
            try
            {
                predicate = ProjectMutationPredicate(mutation.Predicate);
            }
            catch (SourceProjectionException exception)
            {
                diagnostics.Add(Error(
                    exception.Code,
                    exception.Message,
                    Origin(document, mutationPath + "/Predicate"),
                    "StorageMutation"));
            }
        }

        // Nie uruchamiaj walidatora planu na niepełnej projekcji. Pierwotna
        // diagnostyka źródłowa jest bardziej użyteczna niż kaskada wtórnych
        // komunikatów o brakującym przypisaniu albo parametrze.
        if (diagnostics.Count != before)
            return null;

        var plan = new StorageMutationPlan(
            StorageMutationPlan.CurrentVersion,
            mutation.Name,
            model.Name,
            mutation.Entity,
            mutation.Kind switch
            {
                IRStorageMutationKind.Insert => StorageMutationKind.Insert,
                IRStorageMutationKind.Update => StorageMutationKind.Update,
                IRStorageMutationKind.Delete => StorageMutationKind.Delete,
                _ => throw new SourceProjectionException("SMP071", $"Nieznany rodzaj mutacji '{mutation.Kind}'.")
            },
            mutation.Cardinality == IRStorageMutationCardinality.Single
                ? StorageMutationCardinality.Single
                : StorageMutationCardinality.Many,
            parameters,
            assignments,
            predicate);

        foreach (var issue in StorageMutationPlanValidator.Validate(model, plan))
        {
            diagnostics.Add(Error(
                issue.Code,
                issue.Message,
                MutationOrigin(document, mutationPath, issue.Path),
                "StorageMutation"));
        }
        return diagnostics.Count == before ? plan : null;
    }

    private static StorageQueryPredicate ProjectMutationPredicate(IRExpr expression) => expression switch
    {
        IRBinaryExpr { Op: "==" or "!=" or "<" or "<=" or ">" or ">=" } comparison =>
            new StorageComparisonPredicate(
                ProjectMutationPredicateValue(comparison.Left),
                Comparison(comparison.Op),
                ProjectMutationPredicateValue(comparison.Right)),
        IRBinaryExpr { Op: "and" or "&&" or "or" or "||" } logical =>
            new StorageLogicalPredicate(
                logical.Op is "and" or "&&" ? StorageLogicalOperator.And : StorageLogicalOperator.Or,
                [ProjectMutationPredicate(logical.Left), ProjectMutationPredicate(logical.Right)]),
        IRUnaryExpr { Op: "not" or "!" } not => new StorageNotPredicate(ProjectMutationPredicate(not.Operand)),
        _ => throw new SourceProjectionException(
            "SMP072",
            "Where obsługuje porównania, and, or, not, pola, params.<nazwa> i literały.")
    };

    private static StorageQueryExpression ProjectMutationPredicateValue(IRExpr expression) => expression switch
    {
        IRIdentExpr field => new StorageFieldExpression(field.Name),
        IRFieldAccessExpr { Object: IRIdentExpr { Name: "params" }, Field: var parameter } =>
            new StorageParameterExpression(parameter),
        IRLiteralExpr literal => new StorageLiteralExpression(ProjectMutationLiteral(literal.Value)),
        _ => throw new SourceProjectionException(
            "SMP073",
            "Wartość where musi być polem, params.<nazwa> albo literałem.")
    };

    private static StorageQueryExpression ProjectMutationInput(IRExpr expression) => expression switch
    {
        IRFieldAccessExpr { Object: IRIdentExpr { Name: "params" }, Field: var parameter } =>
            new StorageParameterExpression(parameter),
        IRLiteralExpr literal => new StorageLiteralExpression(ProjectMutationLiteral(literal.Value)),
        _ => throw new SourceProjectionException(
            "SMP074",
            "Wartość set musi być params.<nazwa> albo literałem.")
    };

    private static StorageQueryLiteral ProjectMutationLiteral(IRValue value) => value switch
    {
        IRIntValue integer => new StorageQueryLiteral(StorageValueType.Int64, integer.Value.ToString(CultureInfo.InvariantCulture)),
        IRFloatValue number => new StorageQueryLiteral(StorageValueType.Float64, number.Value.ToString("R", CultureInfo.InvariantCulture)),
        IRBoolValue boolean => new StorageQueryLiteral(StorageValueType.Bool, boolean.Value ? "true" : "false"),
        IRTextValue text => new StorageQueryLiteral(StorageValueType.Text, text.Value),
        IRNullValue => new StorageQueryLiteral(StorageValueType.Json, null),
        _ => throw new SourceProjectionException(
            "SMP075",
            "Ten rodzaj literału nie ma kanonicznej reprezentacji Storage Mutation Plan 0.1.")
    };

    private static StorageQueryExpression ProjectValue(IRExpr expression) => expression switch
    {
        IRIdentExpr field => new StorageFieldExpression(field.Name),
        IRFieldAccessExpr { Object: IRIdentExpr { Name: "params" }, Field: var parameter } =>
            new StorageParameterExpression(parameter),
        IRLiteralExpr literal => new StorageLiteralExpression(ProjectLiteral(literal.Value)),
        _ => throw new SourceProjectionException(
            "SQP077",
            "Wartość filtra musi być polem, params.<nazwa> albo literałem.")
    };

    private static StorageQueryLiteral ProjectLiteral(IRValue value) => value switch
    {
        IRIntValue integer => new StorageQueryLiteral(StorageValueType.Int64, integer.Value.ToString(CultureInfo.InvariantCulture)),
        IRFloatValue number => new StorageQueryLiteral(StorageValueType.Float64, number.Value.ToString("R", CultureInfo.InvariantCulture)),
        IRBoolValue boolean => new StorageQueryLiteral(StorageValueType.Bool, boolean.Value ? "true" : "false"),
        IRTextValue text => new StorageQueryLiteral(StorageValueType.Text, text.Value),
        IRNullValue => new StorageQueryLiteral(StorageValueType.Json, null),
        _ => throw new SourceProjectionException(
            "SQP078",
            "Ten rodzaj literału nie ma kanonicznej reprezentacji Storage Query Plan 0.1.")
    };

    private static StorageComparisonOperator Comparison(string op) => op switch
    {
        "==" => StorageComparisonOperator.Equal,
        "!=" => StorageComparisonOperator.NotEqual,
        "<" => StorageComparisonOperator.LessThan,
        "<=" => StorageComparisonOperator.LessThanOrEqual,
        ">" => StorageComparisonOperator.GreaterThan,
        ">=" => StorageComparisonOperator.GreaterThanOrEqual,
        _ => throw new SourceProjectionException("SQP079", $"Nieznany operator porównania '{op}'.")
    };

    private static bool TryProjectType(IRType source, out StorageValueType type, out bool nullable)
    {
        nullable = false;
        if (source is IRUnionType union)
        {
            var concrete = union.Variants
                .Where(item => item is not IRNamedType { Name: "Null" })
                .ToList();
            var nullCount = union.Variants.Count(item => item is IRNamedType { Name: "Null" });
            if (nullCount != 1 || concrete.Count != 1)
            {
                type = default;
                return false;
            }
            nullable = true;
            source = concrete[0];
        }

        if (source is not IRNamedType named)
        {
            type = default;
            return false;
        }

        var projected = named.Name switch
        {
            "Int" or "i8" or "i16" or "i32" or "i64" or "u8" or "u16" or "u32" => StorageValueType.Int64,
            "Float" or "f32" or "f64" => StorageValueType.Float64,
            "Bool" => StorageValueType.Bool,
            "Text" => StorageValueType.Text,
            "Bytes" => StorageValueType.Bytes,
            "Decimal" => StorageValueType.Decimal,
            "Time" => StorageValueType.Timestamp,
            "Uuid" => StorageValueType.Uuid,
            "Json" => StorageValueType.Json,
            _ => (StorageValueType?)null
        };
        type = projected.GetValueOrDefault();
        return projected.HasValue;
    }

    private static IRSourceSpan? Origin(IRDocument document, string path)
        => document.SourceMap.FirstOrDefault(entry => entry.Path == path)?.Source;

    private static IRSourceSpan? MutationOrigin(IRDocument document, string mutationPath, string issuePath)
    {
        var relativePath = issuePath switch
        {
            var path when path.StartsWith("/mutation/assignments/", StringComparison.Ordinal) =>
                AssignmentOriginPath(path),
            var path when path.StartsWith("/mutation/parameters/", StringComparison.Ordinal) =>
                ParameterOriginPath(path),
            "/mutation/predicate" => "/Predicate",
            _ => string.Empty
        };
        return Origin(document, mutationPath + relativePath) ?? Origin(document, mutationPath);
    }

    private static string AssignmentOriginPath(string issuePath)
    {
        var index = issuePath["/mutation/assignments/".Length..].Split('/')[0];
        return $"/Assignments[{index}]";
    }

    private static string ParameterOriginPath(string issuePath)
    {
        var index = issuePath["/mutation/parameters/".Length..].Split('/')[0];
        return $"/Params[{index}]";
    }

    private static Diagnostic Error(
        string code,
        string message,
        IRSourceSpan? origin,
        string category = "StorageQuery") => new(
        code,
        DiagnosticLevel.Error,
        category,
        message,
        origin?.File,
        origin?.Line ?? 0,
        origin?.Column ?? 0);

    private sealed class SourceProjectionException(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }
}
