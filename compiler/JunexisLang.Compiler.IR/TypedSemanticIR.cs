namespace JunexisLang.Compiler.IR;

/// <summary>Minimal, backend-independent Typed Semantic IR contract introduced by M8.2.</summary>
public sealed record TypedSemanticDocument(
    string SchemaVersion,
    string SourceFile,
    IReadOnlyList<TypedSemanticTask> Tasks)
{
    /// <summary>Current version of the minimal Typed Semantic IR contract.</summary>
    public const string CurrentVersion = "junexis.typed-semantic-ir/0.1";
}

/// <summary>A task projected into backend-independent types, lifecycle, and observable operations.</summary>
public sealed record TypedSemanticTask(
    string Name,
    IReadOnlyList<TypedSemanticBinding> Inputs,
    TypedSemanticType? Output,
    IReadOnlyList<TypedSemanticBinding> State,
    TypedSemanticLifecycle Lifecycle,
    IReadOnlyList<TypedSemanticOperation> Operations,
    IRSourceSpan? Origin);

/// <summary>A named typed input or state binding.</summary>
public sealed record TypedSemanticBinding(string Name, TypedSemanticType Type, IRSourceSpan? Origin);

/// <summary>A normalized type that does not expose C++, Go, or TypeScript names.</summary>
public sealed record TypedSemanticType(
    TypedSemanticTypeKind Kind,
    string DisplayName,
    IReadOnlyList<TypedSemanticType> Arguments);

/// <summary>Backend-independent type categories needed by the M8 conformance subset.</summary>
public enum TypedSemanticTypeKind
{
    /// <summary>A built-in scalar or unit value.</summary>
    Primitive,
    /// <summary>A user-declared named type.</summary>
    Named,
    /// <summary>A first-class communication type.</summary>
    Communication,
    /// <summary>An indexed sequence.</summary>
    Array,
    /// <summary>A key/value mapping.</summary>
    Map,
    /// <summary>A parameterized named type.</summary>
    Generic,
    /// <summary>A union of alternatives.</summary>
    Union,
    /// <summary>The type could not yet be resolved by the minimal projection.</summary>
    Unknown
}

/// <summary>Backend-independent lifecycle classification for a task.</summary>
public enum TypedSemanticLifecycle
{
    /// <summary>The task body has no repeating or hook-driven construct.</summary>
    OneShot,
    /// <summary>The task contains an <c>every</c> boundary.</summary>
    Periodic,
    /// <summary>The task contains an explicit loop.</summary>
    Looping,
    /// <summary>The task is driven by lifecycle hooks.</summary>
    HookDriven,
    /// <summary>More than one lifecycle model is present.</summary>
    Mixed
}

/// <summary>The primary observable effect of a semantic operation.</summary>
public enum TypedSemanticEffect
{
    /// <summary>Local evaluation without an observable system effect.</summary>
    Pure,
    /// <summary>Read or initialization of task state.</summary>
    State,
    /// <summary>A lifecycle or control-flow boundary.</summary>
    Control,
    /// <summary>A time-related boundary.</summary>
    Time,
    /// <summary>Communication through a task or communication primitive boundary.</summary>
    Communication,
    /// <summary>An explicitly external or host-provided effect.</summary>
    External
}

/// <summary>Operation kinds represented by the minimal M8.2 semantic projection.</summary>
public enum TypedSemanticOperationKind
{
    /// <summary>Initialize a declared task state value.</summary>
    InitializeState,
    /// <summary>Bind an evaluated value.</summary>
    BindValue,
    /// <summary>Return from a task or function.</summary>
    Return,
    /// <summary>Enter a conditional branch.</summary>
    Conditional,
    /// <summary>Iterate over a value sequence.</summary>
    Iterate,
    /// <summary>Enter an explicit loop.</summary>
    Loop,
    /// <summary>Enter a periodic execution boundary.</summary>
    Periodic,
    /// <summary>Delay execution.</summary>
    Delay,
    /// <summary>Send a value.</summary>
    Send,
    /// <summary>Receive a value.</summary>
    Receive,
    /// <summary>Acknowledge a queued message.</summary>
    Ack,
    /// <summary>Negatively acknowledge a queued message.</summary>
    Nack,
    /// <summary>Write a diagnostic log effect.</summary>
    Log,
    /// <summary>Respond across an external protocol boundary.</summary>
    Respond,
    /// <summary>Invoke an explicitly declared extern task.</summary>
    ExternalCall,
    /// <summary>Evaluate an expression not covered by a more specific operation.</summary>
    Evaluate,
    /// <summary>Enter a declared lifecycle hook.</summary>
    LifecycleHook
}

/// <summary>A flattened semantic operation with stable origin and optional communication contract.</summary>
public sealed record TypedSemanticOperation(
    string Id,
    TypedSemanticOperationKind Kind,
    TypedSemanticEffect Effect,
    TypedSemanticType? ValueType,
    TypedSemanticCommunication? Communication,
    IRSourceSpan? Origin);

/// <summary>A communication operation bound to the canonical Junexis runtime semantics.</summary>
public sealed record TypedSemanticCommunication(
    IRCommKind Kind,
    CommunicationRuntimeSemantics Contract);

/// <summary>Projects Structural IR into the minimal backend-independent semantic contract.</summary>
public static class TypedSemanticProjector
{
    private static readonly HashSet<string> PrimitiveNames = new(StringComparer.Ordinal)
    {
        "Int", "Float", "Bool", "Text", "Time", "Duration",
        "i8", "i16", "i32", "i64", "u8", "u16", "u32", "u64",
        "f32", "f64", "Bytes", "Unit", "Void"
    };

    /// <summary>Creates a Typed Semantic IR 0.1 projection for the supported M8 subset.</summary>
    public static TypedSemanticDocument Project(IRDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var origins = document.SourceMap.ToDictionary(item => item.Path, item => item.Source, StringComparer.Ordinal);
        var externalNames = document.ExternTasks.Select(task => task.Name).ToHashSet(StringComparer.Ordinal);
        var tasks = document.Tasks.Select((task, index) => ProjectTask(task, index, origins, externalNames)).ToArray();
        return new TypedSemanticDocument(TypedSemanticDocument.CurrentVersion, document.SourceFile, tasks);
    }

    private static TypedSemanticTask ProjectTask(
        IRTask task,
        int taskIndex,
        IReadOnlyDictionary<string, IRSourceSpan> origins,
        IReadOnlySet<string> externalNames)
    {
        var taskPath = $"/Tasks[{taskIndex}]";
        var scope = task.Params.ToDictionary(item => item.Name, item => item.Type, StringComparer.Ordinal);
        foreach (var state in task.States)
            scope[state.Name] = state.Type;
        if (task.Config is not null)
            foreach (var field in task.Config.Fields)
                scope[$"config.{field.Name}"] = field.Type;

        var operations = new List<TypedSemanticOperation>();
        for (var index = 0; index < task.States.Count; index++)
        {
            var state = task.States[index];
            operations.Add(new TypedSemanticOperation(
                $"task:{task.Name}/state[{index}]:initialize",
                TypedSemanticOperationKind.InitializeState,
                TypedSemanticEffect.State,
                Normalize(state.Type),
                null,
                Origin(origins, $"{taskPath}/States[{index}]")));
        }

        for (var index = 0; index < task.Hooks.Count; index++)
        {
            var hook = task.Hooks[index];
            var hookPath = $"{taskPath}/Hooks[{index}]";
            operations.Add(new TypedSemanticOperation(
                $"task:{task.Name}/hook[{index}]:{hook.Event}",
                TypedSemanticOperationKind.LifecycleHook,
                TypedSemanticEffect.Control,
                null,
                null,
                Origin(origins, hookPath)));
            AddStatements(operations, task, hook.Body, $"task:{task.Name}/hook[{index}]", $"{hookPath}/Body", origins, scope, externalNames);
        }

        AddStatements(operations, task, task.Body, $"task:{task.Name}", $"{taskPath}/Body", origins, scope, externalNames);
        return new TypedSemanticTask(
            task.Name,
            task.Params.Select((item, index) => new TypedSemanticBinding(
                item.Name,
                NormalizeRequired(item.Type),
                Origin(origins, $"{taskPath}/Params[{index}]"))).ToArray(),
            task.OutputType is null ? null : Normalize(task.OutputType),
            task.States.Select((item, index) => new TypedSemanticBinding(
                item.Name,
                NormalizeRequired(item.Type),
                Origin(origins, $"{taskPath}/States[{index}]"))).ToArray(),
            ClassifyLifecycle(task),
            operations,
            Origin(origins, taskPath));
    }

    private static void AddStatements(
        List<TypedSemanticOperation> operations,
        IRTask task,
        IReadOnlyList<IRStmt> statements,
        string idPrefix,
        string pathPrefix,
        IReadOnlyDictionary<string, IRSourceSpan> origins,
        IReadOnlyDictionary<string, IRType> scope,
        IReadOnlySet<string> externalNames)
    {
        for (var index = 0; index < statements.Count; index++)
        {
            var statement = statements[index];
            var id = $"{idPrefix}/body[{index}]";
            var path = $"{pathPrefix}[{index}]";
            var (kind, effect, type, communication) = Describe(statement, task, scope, externalNames);
            if (kind != TypedSemanticOperationKind.ExternalCall && StatementContainsExternalCall(statement, externalNames))
            {
                operations.Add(new TypedSemanticOperation(
                    $"{id}:external",
                    TypedSemanticOperationKind.ExternalCall,
                    TypedSemanticEffect.External,
                    type,
                    null,
                    Origin(origins, path)));
            }
            operations.Add(new TypedSemanticOperation(id, kind, effect, type, communication, Origin(origins, path)));

            switch (statement)
            {
                case IRIfStmt conditional:
                    AddStatements(operations, task, conditional.Then, $"{id}/then", $"{path}/Then", origins, scope, externalNames);
                    if (conditional.Else is IRElseBlock elseBlock)
                        AddStatements(operations, task, elseBlock.Body, $"{id}/else", $"{path}/Else/Body", origins, scope, externalNames);
                    else if (conditional.Else is IRElseIf elseIf)
                        AddStatements(operations, task, [elseIf.IfStmt], $"{id}/else", $"{path}/Else", origins, scope, externalNames);
                    break;
                case IRForStmt loop:
                    AddStatements(operations, task, loop.Body, $"{id}/for", $"{path}/Body", origins, scope, externalNames);
                    break;
                case IREveryStmt periodic:
                    AddStatements(operations, task, periodic.Body, $"{id}/every", $"{path}/Body", origins, scope, externalNames);
                    break;
                case IRLoopStmt loop:
                    AddStatements(operations, task, loop.Body, $"{id}/loop", $"{path}/Body", origins, scope, externalNames);
                    break;
                case IRMatchStmt match:
                    for (var armIndex = 0; armIndex < match.Arms.Count; armIndex++)
                        AddStatements(operations, task, match.Arms[armIndex].Body, $"{id}/arm[{armIndex}]", $"{path}/Arms[{armIndex}]/Body", origins, scope, externalNames);
                    break;
            }
        }
    }

    private static (TypedSemanticOperationKind Kind, TypedSemanticEffect Effect, TypedSemanticType? Type, TypedSemanticCommunication? Communication) Describe(
        IRStmt statement,
        IRTask task,
        IReadOnlyDictionary<string, IRType> scope,
        IReadOnlySet<string> externalNames)
        => statement switch
        {
            IRLetStmt let when let.Init is IRRecvExpr receive => (
                TypedSemanticOperationKind.Receive,
                TypedSemanticEffect.Communication,
                Normalize(ReceiveType(receive.Source, scope)),
                CommunicationFor(ExpressionType(receive.Source, scope))),
            IRLetStmt let when ContainsExternalCall(let.Init, externalNames) => (
                TypedSemanticOperationKind.ExternalCall,
                TypedSemanticEffect.External,
                Normalize(let.Type ?? ExpressionType(let.Init, scope)),
                null),
            IRLetStmt let => (
                TypedSemanticOperationKind.BindValue,
                TypedSemanticEffect.Pure,
                Normalize(let.Type ?? ExpressionType(let.Init, scope)),
                null),
            IRReturnStmt result => (TypedSemanticOperationKind.Return, TypedSemanticEffect.Control, Normalize(ExpressionType(result.Value, scope)), null),
            IRDelayStmt => (TypedSemanticOperationKind.Delay, TypedSemanticEffect.Time, Normalize(new IRNamedType("Duration")), null),
            IRLogStmt => (TypedSemanticOperationKind.Log, TypedSemanticEffect.External, null, null),
            IRSendStmt send => (
                TypedSemanticOperationKind.Send,
                TypedSemanticEffect.Communication,
                Normalize(ExpressionType(send.Value, scope)),
                CommunicationFor(send.Target is null ? task.OutputType : ExpressionType(send.Target, scope))),
            IRRecvStmt receive => (
                TypedSemanticOperationKind.Receive,
                TypedSemanticEffect.Communication,
                Normalize(ReceiveType(receive.Source, scope)),
                CommunicationFor(ExpressionType(receive.Source, scope))),
            IRAckStmt => (TypedSemanticOperationKind.Ack, TypedSemanticEffect.Communication, null, CommunicationFor(new IRCommType(IRCommKind.Queue, new IRNamedType("Unknown")))),
            IRNackStmt => (TypedSemanticOperationKind.Nack, TypedSemanticEffect.Communication, null, CommunicationFor(new IRCommType(IRCommKind.Queue, new IRNamedType("Unknown")))),
            IRRespondStmt respond => (TypedSemanticOperationKind.Respond, TypedSemanticEffect.Communication, Normalize(ExpressionType(respond.Value, scope)), null),
            IRIfStmt => (TypedSemanticOperationKind.Conditional, TypedSemanticEffect.Control, null, null),
            IRForStmt => (TypedSemanticOperationKind.Iterate, TypedSemanticEffect.Control, null, null),
            IREveryStmt => (TypedSemanticOperationKind.Periodic, TypedSemanticEffect.Time, Normalize(new IRNamedType("Duration")), null),
            IRLoopStmt => (TypedSemanticOperationKind.Loop, TypedSemanticEffect.Control, null, null),
            IRMatchStmt => (TypedSemanticOperationKind.Conditional, TypedSemanticEffect.Control, null, null),
            IRExprStmt expression when ContainsExternalCall(expression.Expr, externalNames) => (TypedSemanticOperationKind.ExternalCall, TypedSemanticEffect.External, Normalize(ExpressionType(expression.Expr, scope)), null),
            IRExprStmt => (TypedSemanticOperationKind.Evaluate, TypedSemanticEffect.Pure, null, null),
            _ => (TypedSemanticOperationKind.Evaluate, TypedSemanticEffect.Pure, null, null)
        };

    private static TypedSemanticLifecycle ClassifyLifecycle(IRTask task)
    {
        var periodic = ContainsStatement<IREveryStmt>(task.Body);
        var looping = ContainsStatement<IRLoopStmt>(task.Body);
        var hooked = task.Hooks.Count > 0;
        var count = (periodic ? 1 : 0) + (looping ? 1 : 0) + (hooked ? 1 : 0);
        if (count > 1) return TypedSemanticLifecycle.Mixed;
        if (periodic) return TypedSemanticLifecycle.Periodic;
        if (looping) return TypedSemanticLifecycle.Looping;
        if (hooked) return TypedSemanticLifecycle.HookDriven;
        return TypedSemanticLifecycle.OneShot;
    }

    private static bool ContainsStatement<T>(IReadOnlyList<IRStmt> statements) where T : IRStmt
        => statements.Any(statement => statement is T || statement switch
        {
            IRIfStmt conditional => ContainsStatement<T>(conditional.Then)
                || conditional.Else is IRElseBlock elseBlock && ContainsStatement<T>(elseBlock.Body)
                || conditional.Else is IRElseIf elseIf && ContainsStatement<T>([elseIf.IfStmt]),
            IRForStmt loop => ContainsStatement<T>(loop.Body),
            IREveryStmt periodic => ContainsStatement<T>(periodic.Body),
            IRLoopStmt loop => ContainsStatement<T>(loop.Body),
            IRMatchStmt match => match.Arms.Any(arm => ContainsStatement<T>(arm.Body)),
            _ => false
        });

    private static bool ContainsExternalCall(IRExpr expression, IReadOnlySet<string> externalNames)
        => expression switch
        {
            IRCallExpr { Callee: IRIdentExpr callee } call => externalNames.Contains(callee.Name)
                || call.Args.Any(argument => ContainsExternalCall(argument.Value, externalNames)),
            IRBinaryExpr binary => ContainsExternalCall(binary.Left, externalNames) || ContainsExternalCall(binary.Right, externalNames),
            IRUnaryExpr unary => ContainsExternalCall(unary.Operand, externalNames),
            IRFieldAccessExpr field => ContainsExternalCall(field.Object, externalNames),
            IRIndexExpr index => ContainsExternalCall(index.Object, externalNames) || ContainsExternalCall(index.Index, externalNames),
            IRTernaryExpr ternary => ContainsExternalCall(ternary.Cond, externalNames)
                || ContainsExternalCall(ternary.Then, externalNames)
                || ContainsExternalCall(ternary.Else, externalNames),
            IRSendExpr send => ContainsExternalCall(send.Value, externalNames)
                || send.Target is not null && ContainsExternalCall(send.Target, externalNames),
            IRRecvExpr receive => ContainsExternalCall(receive.Source, externalNames),
            IRAwaitExpr awaitExpression => ContainsExternalCall(awaitExpression.Inner, externalNames),
            _ => false
        };

    private static bool StatementContainsExternalCall(IRStmt statement, IReadOnlySet<string> externalNames)
        => statement switch
        {
            IRLetStmt let => ContainsExternalCall(let.Init, externalNames),
            IRReturnStmt { Value: not null } result => ContainsExternalCall(result.Value, externalNames),
            IRDelayStmt delay => ContainsExternalCall(delay.Duration, externalNames),
            IRLogStmt log => log.Args.Any(argument => ContainsExternalCall(argument, externalNames)),
            IRSendStmt send => ContainsExternalCall(send.Value, externalNames)
                || send.Target is not null && ContainsExternalCall(send.Target, externalNames),
            IRRecvStmt receive => ContainsExternalCall(receive.Source, externalNames),
            IRRespondStmt respond => ContainsExternalCall(respond.Value, externalNames),
            IRExprStmt expression => ContainsExternalCall(expression.Expr, externalNames),
            _ => false
        };

    private static IRType? ReceiveType(IRExpr source, IReadOnlyDictionary<string, IRType> scope)
        => ExpressionType(source, scope) is IRCommType communication ? communication.Element : null;

    private static IRType? ExpressionType(IRExpr? expression, IReadOnlyDictionary<string, IRType> scope)
        => expression switch
        {
            null => null,
            IRLiteralExpr { Value: IRIntValue } => new IRNamedType("Int"),
            IRLiteralExpr { Value: IRFloatValue } => new IRNamedType("Float"),
            IRLiteralExpr { Value: IRBoolValue } => new IRNamedType("Bool"),
            IRLiteralExpr { Value: IRTextValue } => new IRNamedType("Text"),
            IRLiteralExpr { Value: IRUnitValue { Unit: var unit } } => new IRNamedType(IsTimeUnit(unit) ? "Duration" : "Unit"),
            IRLiteralExpr { Value: IRNullValue } => new IRNamedType("Null"),
            IRIdentExpr identifier when scope.TryGetValue(identifier.Name, out var type) => type,
            IRRecvExpr receive => ReceiveType(receive.Source, scope),
            IRBinaryExpr binary when binary.Op is "==" or "!=" or ">" or ">=" or "<" or "<=" or "and" or "or" => new IRNamedType("Bool"),
            IRBinaryExpr binary => ExpressionType(binary.Left, scope),
            IRUnaryExpr unary => ExpressionType(unary.Operand, scope),
            IRTernaryExpr ternary => ExpressionType(ternary.Then, scope),
            IRAwaitExpr awaitExpression => ExpressionType(awaitExpression.Inner, scope),
            _ => null
        };

    private static TypedSemanticCommunication? CommunicationFor(IRType? type)
        => type is IRCommType communication
            ? new TypedSemanticCommunication(communication.Kind, CommunicationRuntimeContractCatalog.For(communication.Kind))
            : null;

    private static TypedSemanticType? Normalize(IRType? type)
        => type switch
        {
            null => null,
            IRNamedType named => new TypedSemanticType(
                PrimitiveNames.Contains(named.Name) ? TypedSemanticTypeKind.Primitive : TypedSemanticTypeKind.Named,
                named.Name,
                []),
            IRCommType communication => new TypedSemanticType(
                TypedSemanticTypeKind.Communication,
                $"{communication.Kind.ToString().ToLowerInvariant()}<{Display(communication.Element)}>",
                [Normalize(communication.Element)!]),
            IRArrayType array => new TypedSemanticType(TypedSemanticTypeKind.Array, $"[{Display(array.Element)}]", [Normalize(array.Element)!]),
            IRMapType map => new TypedSemanticType(TypedSemanticTypeKind.Map, $"Map<{Display(map.Key)},{Display(map.Value)}>", [Normalize(map.Key)!, Normalize(map.Value)!]),
            IRGenericType generic => new TypedSemanticType(TypedSemanticTypeKind.Generic, $"{generic.Name}<{string.Join(',', generic.TypeArgs.Select(Display))}>", generic.TypeArgs.Select(item => Normalize(item)!).ToArray()),
            IRUnionType union => new TypedSemanticType(TypedSemanticTypeKind.Union, string.Join('|', union.Variants.Select(Display)), union.Variants.Select(item => Normalize(item)!).ToArray()),
            _ => new TypedSemanticType(TypedSemanticTypeKind.Unknown, "Unknown", [])
        };

    private static TypedSemanticType NormalizeRequired(IRType type)
        => Normalize(type) ?? throw new InvalidOperationException("A declared IR type cannot normalize to null.");

    private static string Display(IRType type) => Normalize(type)?.DisplayName ?? "Unknown";

    private static IRSourceSpan? Origin(IReadOnlyDictionary<string, IRSourceSpan> origins, string path)
        => origins.TryGetValue(path, out var source) ? source : null;

    private static bool IsTimeUnit(string unit)
        => unit is "ns" or "us" or "ms" or "s" or "m" or "h" or "d";
}
