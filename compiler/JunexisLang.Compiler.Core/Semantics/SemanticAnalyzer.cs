using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.Core.Runtime;
using JunexisLang.Compiler.Core.Projects;

namespace JunexisLang.Compiler.Core.Semantics;

/// <summary>
/// Semantic analysis pass over the AST.
/// Runs after parsing and before IR transformation.
/// <para>
/// Pass 1 — symbol collection: collects all top-level declared names
///   (structs, enums, tasks, fn, extern tasks, extern types, extern contracts).
/// </para>
/// <para>
/// Pass 2 — usage validation: inside task and fn bodies, checks that
///   every called identifier is declared (task, fn, extern task, or
///   a known built-in), and that every referenced type name is declared.
/// </para>
/// </summary>
public static class SemanticAnalyzer
{
    // ── Built-in type names that are always in scope ──────────────────────────
    private static readonly HashSet<string> s_builtinTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // spec types (PascalCase)
        "Int", "Float", "Bool", "Text", "Symbol", "Time", "Duration", "Null",
        // numeric subtypes allowed in practice
        "u8", "u16", "u32", "u64", "i8", "i16", "i32", "i64", "f32", "f64",
        // comm type heads (not actual named types, but referenced as such)
        "chan", "queue", "stream",
        // stdlib-ish
        "Map", "List", "Option", "Result",
        // portable storage model values (D22)
        "Bytes", "Decimal", "Uuid", "Json",
    };

    /// <summary>
    /// Runs semantic analysis on a parsed program and returns diagnostics.
    /// An empty list means no semantic errors were found.
    /// </summary>
    /// <param name="program">Root AST node.</param>
    /// <returns>List of semantic diagnostics (errors and warnings).</returns>
    public static IReadOnlyList<Diagnostic> Analyze(ProgramNode program)
        => Analyze(program, []);

    /// <summary>Runs semantic analysis with declarations imported by the project symbol binder.</summary>
    /// <param name="program">The consuming module program.</param>
    /// <param name="visibleSymbols">Its complete bound symbol scope.</param>
    /// <returns>Semantic diagnostics for the module.</returns>
    public static IReadOnlyList<Diagnostic> Analyze(
        ProgramNode program,
        IEnumerable<ProjectVisibleSymbol> visibleSymbols)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(visibleSymbols);

        var diagnostics = new List<Diagnostic>();

        // ── Pass 1: collect global symbol table ───────────────────────────────
        var symbols = new GlobalSymbolTable();
        foreach (var symbol in visibleSymbols.Where(symbol => !symbol.IsLocal))
            RegisterVisibleSymbol(symbol, symbols, diagnostics);
        CollectSymbols(program, symbols, diagnostics);

        // ── Pass 2: validate usages ───────────────────────────────────────────
        ValidateUsages(program, symbols, diagnostics);

        return diagnostics;
    }

    private static void RegisterVisibleSymbol(
        ProjectVisibleSymbol symbol,
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        switch (symbol.Declaration)
        {
            case StructDeclarationNode or EnumDeclarationNode or ExternTypeNode:
                RegisterType(symbols, symbol.VisibleName, symbol.Declaration, diagnostics);
                break;
            case TaskDeclarationNode or FnDeclarationNode or ExternTaskNode:
                RegisterCallable(symbols, symbol.VisibleName, symbol.Declaration, diagnostics);
                break;
            case ExternContractNode:
                RegisterExternContract(symbols, symbol.VisibleName, symbol.Declaration, diagnostics);
                break;
            case StorageDeclarationNode storage when !symbol.VisibleName.Contains("::", StringComparison.Ordinal):
                RegisterStorageOperations(storage, symbol.VisibleName, symbols, diagnostics);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pass 1 — symbol collection
    // ─────────────────────────────────────────────────────────────────────────

    private static void CollectSymbols(
        ProgramNode program,
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        foreach (var decl in program.Declarations)
        {
            switch (decl)
            {
                case StructDeclarationNode s:
                    RegisterType(symbols, s.Name, s, diagnostics);
                    break;

                case EnumDeclarationNode e:
                    RegisterType(symbols, e.Name, e, diagnostics);
                    break;

                case ExternTypeNode et:
                    RegisterType(symbols, et.Name, et, diagnostics);
                    break;

                case TaskDeclarationNode t:
                    RegisterCallable(symbols, t.Name, t, diagnostics);
                    break;

                case FnDeclarationNode fn:
                    RegisterCallable(symbols, fn.Name, fn, diagnostics);
                    break;

                case ExternTaskNode ext:
                    RegisterCallable(symbols, ext.Name, ext, diagnostics);
                    break;

                case ExternContractNode contract:
                    RegisterExternContract(symbols, contract.Name, contract, diagnostics);
                    break;

                case StorageDeclarationNode storage:
                    RegisterStorageOperations(storage, storage.Name, symbols, diagnostics);
                    break;

                // opaque / pipeline / device / narrative — no symbols to collect
            }
        }
    }

    private static void RegisterStorageOperations(
        StorageDeclarationNode storage,
        string visibleModelName,
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        var entities = new Dictionary<string, StorageEntityNode>(StringComparer.Ordinal);
        foreach (var entity in storage.Entities)
            entities.TryAdd(entity.Name, entity);
        foreach (var query in storage.Queries)
        {
            if (!entities.TryGetValue(query.SourceEntity, out var entity))
                continue;

            IReadOnlyList<StructFieldNode> fields = entity.Fields
                .Select(field => new StructFieldNode(
                    field.File, field.Line, field.Column, field.Name, field.Type, null))
                .ToList();
            foreach (var project in query.Steps.OfType<StorageProjectStepNode>())
            {
                var projected = new List<StructFieldNode>();
                foreach (var item in project.Projections)
                {
                    var source = fields.FirstOrDefault(field => field.Name == item.Field);
                    if (source is null)
                        continue;
                    projected.Add(new StructFieldNode(
                        item.File,
                        item.Line,
                        item.Column,
                        item.Alias ?? item.Field,
                        source.Type,
                        null));
                }
                fields = projected;
            }

            var callableName = StorageQueryNames.Callable(visibleModelName, query.Name);
            var row = new StructDeclarationNode(
                query.File,
                query.Line,
                query.Column,
                StorageQueryNames.Row(visibleModelName, query.Name),
                fields);
            RegisterType(symbols, row.Name, row, diagnostics);
            RegisterCallable(symbols, callableName, query, diagnostics);
            symbols.StorageQueryRows.TryAdd(callableName, row);
        }

        foreach (var mutation in storage.Mutations)
        {
            var callableName = StorageMutationNames.Callable(visibleModelName, mutation.Name);
            var result = new StructDeclarationNode(
                mutation.File,
                mutation.Line,
                mutation.Column,
                StorageMutationNames.Result(visibleModelName, mutation.Name),
                [
                    new StructFieldNode(mutation.File, mutation.Line, mutation.Column, "outcome",
                        new NamedTypeNode(mutation.File, mutation.Line, mutation.Column, "Text"), null),
                    new StructFieldNode(mutation.File, mutation.Line, mutation.Column, "affectedRows",
                        new NamedTypeNode(mutation.File, mutation.Line, mutation.Column, "Int"), null)
                ]);
            RegisterType(symbols, result.Name, result, diagnostics);
            RegisterCallable(symbols, callableName, mutation, diagnostics);
            symbols.StorageMutationResults.TryAdd(callableName, result);
        }
    }

    private static void RegisterType(
        GlobalSymbolTable symbols,
        string name,
        AstNode node,
        List<Diagnostic> diagnostics)
    {
        if (!symbols.Types.TryAdd(name, node))
        {
            diagnostics.Add(new Diagnostic(
                "E0001", DiagnosticLevel.Error, "Semantic",
                $"duplicate declaration: '{name}'",
                node.File, node.Line, node.Column));
        }
    }

    private static void RegisterCallable(
        GlobalSymbolTable symbols,
        string name,
        AstNode node,
        List<Diagnostic> diagnostics)
    {
        if (!symbols.Callables.TryAdd(name, node))
        {
            diagnostics.Add(new Diagnostic(
                "E0001", DiagnosticLevel.Error, "Semantic",
                $"duplicate declaration: '{name}'",
                node.File, node.Line, node.Column));
        }
    }

    private static void RegisterExternContract(
        GlobalSymbolTable symbols,
        string name,
        AstNode node,
        List<Diagnostic> diagnostics)
    {
        if (!symbols.ExternContracts.TryAdd(name, node))
        {
            diagnostics.Add(new Diagnostic(
                "E0001", DiagnosticLevel.Error, "Semantic",
                $"duplicate declaration: '{name}'",
                node.File, node.Line, node.Column));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pass 2 — usage validation
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateUsages(
        ProgramNode program,
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        foreach (var decl in program.Declarations)
        {
            switch (decl)
            {
                case TaskDeclarationNode task:
                    ValidateParameters(task.Params, symbols, diagnostics);
                    if (task.OutputType is not null)
                        ValidateTypeRef(task.OutputType, symbols, diagnostics);
                    ValidateTaskBody(task, symbols, diagnostics);
                    break;

                case FnDeclarationNode fn:
                    ValidateParameters(fn.Params, symbols, diagnostics);
                    ValidateFnBody(fn, symbols, diagnostics);
                    break;

                case ExternTaskNode external:
                    ValidateParameters(external.Params, symbols, diagnostics);
                    if (external.OutputType is not null)
                        ValidateTypeRef(external.OutputType, symbols, diagnostics);
                    break;

                case ExternContractNode contract:
                    foreach (var function in contract.Functions)
                    {
                        ValidateParameters(function.Params, symbols, diagnostics);
                        if (function.ReturnType is not null)
                            ValidateTypeRef(function.ReturnType, symbols, diagnostics);
                    }
                    break;
            }
        }

        ValidateExternContractBindings(symbols, diagnostics);
    }

    private static void ValidateExternContractBindings(
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        foreach (var contract in symbols.ExternContracts.Values.OfType<ExternContractNode>())
        {
            foreach (var function in contract.Functions)
            {
                var expectedTaskName = $"{contract.Name}_{function.Name}";
                if (!symbols.Callables.TryGetValue(expectedTaskName, out var callable) ||
                    callable is not ExternTaskNode externTask)
                {
                    diagnostics.Add(new Diagnostic(
                        "W0030", DiagnosticLevel.Warning, "Semantic",
                        $"extern contract '{contract.Name}.{function.Name}' has no matching extern task '{expectedTaskName}'",
                        function.File, function.Line, function.Column));
                    continue;
                }

                ValidateExternBindingSignature(contract, function, externTask, diagnostics);
            }
        }
    }

    private static void ValidateExternBindingSignature(
        ExternContractNode contract,
        ExternContractFunctionNode function,
        ExternTaskNode externTask,
        List<Diagnostic> diagnostics)
    {
        var issues = new List<string>();
        var expectedPlatform = contract.Target;
        if (!string.IsNullOrWhiteSpace(expectedPlatform) &&
            !externTask.Platforms.Any(platform =>
                platform.Target.Equals(expectedPlatform, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add($"target expected @{expectedPlatform}");
        }

        if (function.Params.Count != externTask.Params.Count)
        {
            issues.Add($"parameter count expected {function.Params.Count}, got {externTask.Params.Count}");
        }
        else
        {
            for (var index = 0; index < function.Params.Count; index++)
            {
                var expected = function.Params[index];
                var actual = externTask.Params[index];
                if (!TypeEquals(expected.Type, actual.Type))
                    issues.Add($"parameter '{expected.Name}' type expected {FormatType(expected.Type)}, got {FormatType(actual.Type)}");
            }
        }

        if (!NullableTypeEquals(function.ReturnType, externTask.OutputType))
        {
            issues.Add($"return type expected {FormatNullableType(function.ReturnType)}, got {FormatNullableType(externTask.OutputType)}");
        }

        if (issues.Count == 0)
            return;

        diagnostics.Add(new Diagnostic(
            "E0031", DiagnosticLevel.Error, "Semantic",
            $"extern binding mismatch for '{contract.Name}.{function.Name}' -> '{externTask.Name}': {string.Join("; ", issues)}",
            externTask.File, externTask.Line, externTask.Column));
    }

    private static void ValidateParameters(
        IReadOnlyList<ParamNode> parameters,
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        foreach (var parameter in parameters)
            ValidateTypeRef(parameter.Type, symbols, diagnostics);
    }

    private static void ValidateTaskBody(
        TaskDeclarationNode task,
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        // Build local scope: task params + state vars + config
        var localScope = new LocalScope(GetRuntimePrimitiveTarget(task.Platforms));
        foreach (var p in task.Params)
            localScope.Declare(p.Name, p.Type);

        foreach (var member in task.Body)
        {
            switch (member)
            {
                case StateDeclarationNode state:
                    localScope.Declare(state.Name, state.Type);
                    ValidateTypeRef(state.Type, symbols, diagnostics);
                    if (state.Init is not null)
                        ValidateExpr(state.Init, symbols, localScope, diagnostics);
                    break;

                case ConfigBlockNode config:
                    foreach (var field in config.Fields)
                        ValidateTypeRef(field.Type, symbols, diagnostics);
                    break;

                case LifecycleHookNode hook:
                    ValidateStmts(hook.Body, symbols, localScope, diagnostics, task.OutputType);
                    break;

                case StmtNode stmt:
                    ValidateStmt(stmt, symbols, localScope, diagnostics, task.OutputType);
                    break;
            }
        }
    }

    private static void ValidateFnBody(
        FnDeclarationNode fn,
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        var localScope = new LocalScope();
        foreach (var p in fn.Params)
            localScope.Declare(p.Name, p.Type);

        if (fn.ReturnType is not null)
            ValidateTypeRef(fn.ReturnType, symbols, diagnostics);

        ValidateStmts(fn.Body, symbols, localScope, diagnostics, fn.ReturnType);
    }

    // ── Statement visitors ────────────────────────────────────────────────────

    private static void ValidateStmts(
        IReadOnlyList<StmtNode> stmts,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics,
        TypeRefNode? implicitSendTargetType)
    {
        foreach (var stmt in stmts)
            ValidateStmt(stmt, symbols, scope, diagnostics, implicitSendTargetType);
    }

    private static void ValidateStmt(
        StmtNode stmt,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics,
        TypeRefNode? implicitSendTargetType)
    {
        switch (stmt)
        {
            case LetStmtNode let:
                if (let.Type is not null)
                    ValidateTypeRef(let.Type, symbols, diagnostics);
                ValidateExpr(let.Init, symbols, scope, diagnostics);
                var inferredType = InferExprType(let.Init, symbols, scope);
                if (let.Type is not null &&
                    inferredType is not null &&
                    !TypeEquals(let.Type, inferredType))
                {
                    diagnostics.Add(new Diagnostic(
                        "E0044", DiagnosticLevel.Error, "Semantic",
                        $"let type mismatch for '{let.Name}': expected {FormatType(let.Type)}, got {FormatType(inferredType)}",
                        let.File, let.Line, let.Column));
                }
                // Declare after init so self-reference is an error
                var ackable = let.Init is RecvExprNode recvInit &&
                    InferExprType(recvInit.Source, symbols, scope) is CommTypeNode { Kind: CommKind.Queue };
                scope.Declare(let.Name, let.Type ?? inferredType, ackable);
                break;

            case ReturnStmtNode ret:
                if (ret.Value is not null)
                    ValidateExpr(ret.Value, symbols, scope, diagnostics);
                break;

            case SendStmtNode send:
                ValidateSend(send.Value, send.Target, implicitSendTargetType, symbols, scope, diagnostics, send.File, send.Line, send.Column);
                break;

            case ExprStmtNode exprStmt:
                ValidateExpr(exprStmt.Expr, symbols, scope, diagnostics);
                break;

            case IfStmtNode ifStmt:
                ValidateExpr(ifStmt.Condition, symbols, scope, diagnostics);
                var thenScope = scope.Child();
                ValidateStmts(ifStmt.Then, symbols, thenScope, diagnostics, implicitSendTargetType);
                if (ifStmt.Else is ElseBlockNode elseBlock)
                {
                    var elseScope = scope.Child();
                    ValidateStmts(elseBlock.Body, symbols, elseScope, diagnostics, implicitSendTargetType);
                }
                else if (ifStmt.Else is IfStmtNode elseIf)
                {
                    ValidateStmt(elseIf, symbols, scope.Child(), diagnostics, implicitSendTargetType);
                }
                break;

            case ForStmtNode forStmt:
                ValidateExpr(forStmt.Iter, symbols, scope, diagnostics);
                var iterType = InferExprType(forStmt.Iter, symbols, scope);
                var forScope = scope.Child();
                forScope.Declare(forStmt.Var, InferIterationElementType(iterType));
                if (iterType is not null && InferIterationElementType(iterType) is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "E0043", DiagnosticLevel.Error, "Semantic",
                        $"for source must be iterable or communication type, got {FormatType(iterType)}",
                        forStmt.Iter.File, forStmt.Iter.Line, forStmt.Iter.Column));
                }
                ValidateStmts(forStmt.Body, symbols, forScope, diagnostics, implicitSendTargetType);
                break;

            case EveryStmtNode every:
                ValidateExpr(every.Interval, symbols, scope, diagnostics);
                ValidateStmts(every.Body, symbols, scope.Child(), diagnostics, implicitSendTargetType);
                break;

            case LoopStmtNode loop:
                ValidateStmts(loop.Body, symbols, scope.Child(), diagnostics, implicitSendTargetType);
                break;

            case MatchStmtNode match:
                ValidateExpr(match.Subject, symbols, scope, diagnostics);
                foreach (var arm in match.Arms)
                {
                    var armScope = scope.Child();
                    // Binding patterns introduce a local variable
                    if (arm.Pattern is BindingPatternNode binding)
                        armScope.Declare(binding.Name);
                    ValidateStmts(arm.Body, symbols, armScope, diagnostics, implicitSendTargetType);
                    if (arm.BodyExpr is not null)
                        ValidateExpr(arm.BodyExpr, symbols, armScope, diagnostics);
                }
                break;

            case DelayStmtNode delay:
                ValidateExpr(delay.Duration, symbols, scope, diagnostics);
                break;

            case LogStmtNode log:
                foreach (var arg in log.Args)
                    ValidateExpr(arg, symbols, scope, diagnostics);
                break;

            case RecvStmtNode recv:
                ValidateRecv(recv.Source, symbols, scope, diagnostics, recv.File, recv.Line, recv.Column);
                break;

            case AckStmtNode ack:
                ValidateAckNack("ack", ack.Message, null, symbols, scope, diagnostics, ack.File, ack.Line, ack.Column);
                break;

            case NackStmtNode nack:
                ValidateAckNack("nack", nack.Message, nack.Reason, symbols, scope, diagnostics, nack.File, nack.Line, nack.Column);
                break;

            case RespondStmtNode respond:
                ValidateExpr(respond.Value, symbols, scope, diagnostics);
                break;
        }
    }

    // ── Expression visitors ───────────────────────────────────────────────────

    private static void ValidateExpr(
        ExprNode expr,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        switch (expr)
        {
            case LiteralNode:
            case ConfigAccessNode:
            case EnumAccessNode:
                break;

            case IdentifierExprNode id:
                // Only report error if this identifier appears standalone (not as callee)
                // as a callee it will be validated by CallExprNode
                // here we validate it as a value reference
                if (!scope.Contains(id.Name) &&
                    !symbols.Callables.ContainsKey(id.Name) &&
                    !RuntimePrimitiveRegistry.IsKnown(id.Name))
                {
                    // Don't report — identifiers could be config fields, enum variants etc.
                    // The call site check (CallExprNode) is the authoritative check.
                }
                break;

            case CallExprNode call:
                ValidateCall(call, symbols, scope, diagnostics);
                break;

            case BinaryExprNode bin:
                ValidateExpr(bin.Left, symbols, scope, diagnostics);
                ValidateExpr(bin.Right, symbols, scope, diagnostics);
                break;

            case UnaryExprNode unary:
                ValidateExpr(unary.Operand, symbols, scope, diagnostics);
                break;

            case FieldAccessNode field:
                ValidateFieldAccess(field, symbols, scope, diagnostics);
                break;

            case IndexExprNode idx:
                ValidateExpr(idx.Base, symbols, scope, diagnostics);
                ValidateExpr(idx.Index, symbols, scope, diagnostics);
                break;

            case StructLiteralNode structLit:
                ValidateStructLiteral(structLit, symbols, scope, diagnostics);
                break;

            case ArrayLiteralNode arr:
                foreach (var el in arr.Elements)
                    ValidateExpr(el, symbols, scope, diagnostics);
                break;

            case JsonLiteralNode json:
                foreach (var f in json.Fields)
                    ValidateExpr(f.Value, symbols, scope, diagnostics);
                break;

            case RecvExprNode recv:
                ValidateRecv(recv.Source, symbols, scope, diagnostics, recv.File, recv.Line, recv.Column);
                break;

            case SendExprNode send:
                ValidateSend(send.Value, send.Target, null, symbols, scope, diagnostics, send.File, send.Line, send.Column);
                break;

            case ParenExprNode paren:
                ValidateExpr(paren.Inner, symbols, scope, diagnostics);
                break;
        }
    }

    private static void ValidateSend(
        ExprNode value,
        ExprNode? target,
        TypeRefNode? implicitTargetType,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics,
        string file,
        int line,
        int column)
    {
        ValidateExpr(value, symbols, scope, diagnostics);
        if (target is not null)
            ValidateExpr(target, symbols, scope, diagnostics);

        var targetType = target is not null
            ? InferExprType(target, symbols, scope)
            : implicitTargetType;

        if (targetType is null)
            return;

        if (targetType is not CommTypeNode comm)
        {
            diagnostics.Add(new Diagnostic(
                "E0040", DiagnosticLevel.Error, "Semantic",
                $"send target must be chan<T>, queue<T> or stream<T>, got {FormatType(targetType)}",
                file, line, column));
            return;
        }

        var valueType = InferExprType(value, symbols, scope);
        if (valueType is not null && !TypeEquals(comm.Element, valueType))
        {
            diagnostics.Add(new Diagnostic(
                "E0042", DiagnosticLevel.Error, "Semantic",
                $"send value type mismatch: expected {FormatType(comm.Element)}, got {FormatType(valueType)}",
                value.File, value.Line, value.Column));
        }
    }

    private static void ValidateRecv(
        ExprNode source,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics,
        string file,
        int line,
        int column)
    {
        ValidateExpr(source, symbols, scope, diagnostics);
        var sourceType = InferExprType(source, symbols, scope);
        if (sourceType is null)
            return;

        if (sourceType is not CommTypeNode)
        {
            diagnostics.Add(new Diagnostic(
                "E0041", DiagnosticLevel.Error, "Semantic",
                $"recv source must be chan<T>, queue<T> or stream<T>, got {FormatType(sourceType)}",
                file, line, column));
        }
    }

    private static void ValidateAckNack(
        string primitiveName,
        ExprNode message,
        ExprNode? reason,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics,
        string file,
        int line,
        int column)
    {
        ValidateExpr(message, symbols, scope, diagnostics);
        if (reason is not null)
            ValidateExpr(reason, symbols, scope, diagnostics);

        ValidateRuntimePrimitiveUse(primitiveName, scope, diagnostics, file, line, column);

        if (message is IdentifierExprNode id && scope.IsAckable(id.Name))
            return;

        diagnostics.Add(new Diagnostic(
            "E0047", DiagnosticLevel.Error, "Semantic",
            $"{primitiveName} target must be a value received from queue<T>",
            message.File, message.Line, message.Column));
    }

    private static void ValidateCall(
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        // Validate callee
        if (call.Callee is IdentifierExprNode callee)
        {
            var name = callee.Name;
            var isRuntimePrimitive = RuntimePrimitiveRegistry.TryGet(name, out var primitive);
            if (!scope.Contains(name) &&
                !symbols.Callables.ContainsKey(name) &&
                !isRuntimePrimitive)
            {
                diagnostics.Add(new Diagnostic(
                    "E0020", DiagnosticLevel.Warning, "Semantic",
                    $"undeclared identifier: '{name}'",
                    callee.File, callee.Line, callee.Column));
            }
            else if (isRuntimePrimitive)
            {
                ValidateRuntimePrimitiveCall(name, call, symbols, scope, diagnostics);
            }
            else if (symbols.Callables.TryGetValue(name, out var declaration))
            {
                if (declaration is StorageQueryNode storageQuery)
                {
                    if (call.Args.Count != storageQuery.Params.Count)
                    {
                        diagnostics.Add(new Diagnostic(
                            "E0023", DiagnosticLevel.Error, "Semantic",
                            $"argument count mismatch for '{name}': expected {storageQuery.Params.Count}, got {call.Args.Count}",
                            call.File, call.Line, call.Column));
                    }
                    ValidateStorageQueryCall(name, storageQuery, call, symbols, scope, diagnostics);
                }
                else if (declaration is StorageMutationNode storageMutation)
                {
                    if (call.Args.Count != storageMutation.Params.Count)
                    {
                        diagnostics.Add(new Diagnostic(
                            "E0023", DiagnosticLevel.Error, "Semantic",
                            $"argument count mismatch for '{name}': expected {storageMutation.Params.Count}, got {call.Args.Count}",
                            call.File, call.Line, call.Column));
                    }
                    ValidateStorageMutationCall(name, storageMutation, call, symbols, scope, diagnostics);
                }
                else
                {
                    var parameters = declaration switch
                    {
                        TaskDeclarationNode task => task.Params,
                        FnDeclarationNode function => function.Params,
                        ExternTaskNode external => external.Params,
                        _ => []
                    };
                    var required = parameters.Count(parameter => parameter.Default is null);
                    if (call.Args.Count < required || call.Args.Count > parameters.Count)
                    {
                        diagnostics.Add(new Diagnostic(
                            "E0023", DiagnosticLevel.Error, "Semantic",
                            $"argument count mismatch for '{name}': expected {required}..{parameters.Count}, got {call.Args.Count}",
                            call.File, call.Line, call.Column));
                    }
                }
            }
        }
        else
        {
            ValidateExpr(call.Callee, symbols, scope, diagnostics);
        }

        // Validate arguments
        foreach (var arg in call.Args)
            ValidateExpr(arg.Value, symbols, scope, diagnostics);
    }

    private static void ValidateStorageQueryCall(
        string callableName,
        StorageQueryNode query,
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        if (scope.RuntimeTarget != RuntimePrimitiveTarget.Server)
        {
            diagnostics.Add(new Diagnostic(
                "SQC001", DiagnosticLevel.Error, "StorageQuery",
                $"named query '{callableName}' może być wywołane wyłącznie w tasku @server",
                call.File, call.Line, call.Column));
        }

        var namedCount = call.Args.Count(argument => argument.Name is not null);
        if (namedCount != 0 && namedCount != call.Args.Count)
        {
            diagnostics.Add(new Diagnostic(
                "SQC002", DiagnosticLevel.Error, "StorageQuery",
                $"wywołanie '{callableName}' nie może mieszać argumentów pozycyjnych i nazwanych",
                call.File, call.Line, call.Column));
            return;
        }

        if (namedCount == 0)
        {
            for (var index = 0; index < Math.Min(call.Args.Count, query.Params.Count); index++)
                ValidateStorageQueryArgument(callableName, query.Params[index], call.Args[index], symbols, scope, diagnostics);
            return;
        }

        var supplied = new Dictionary<string, CallArgNode>(StringComparer.Ordinal);
        foreach (var argument in call.Args)
        {
            var name = argument.Name!;
            if (!supplied.TryAdd(name, argument))
            {
                diagnostics.Add(new Diagnostic(
                    "SQC003", DiagnosticLevel.Error, "StorageQuery",
                    $"argument '{name}' został przekazany więcej niż raz do '{callableName}'",
                    argument.Value.File, argument.Value.Line, argument.Value.Column));
            }
            else if (!query.Params.Any(parameter => parameter.Name == name))
            {
                diagnostics.Add(new Diagnostic(
                    "SQC004", DiagnosticLevel.Error, "StorageQuery",
                    $"named query '{callableName}' nie deklaruje argumentu '{name}'",
                    argument.Value.File, argument.Value.Line, argument.Value.Column));
            }
        }

        foreach (var parameter in query.Params)
        {
            if (supplied.TryGetValue(parameter.Name, out var argument))
                ValidateStorageQueryArgument(callableName, parameter, argument, symbols, scope, diagnostics);
        }
    }

    private static void ValidateStorageQueryArgument(
        string callableName,
        ParamNode parameter,
        CallArgNode argument,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        var actual = InferExprType(argument.Value, symbols, scope);
        if (actual is null || TypeEquals(parameter.Type, actual))
            return;

        diagnostics.Add(new Diagnostic(
            "SQC005", DiagnosticLevel.Error, "StorageQuery",
            $"argument '{parameter.Name}' w '{callableName}' ma typ {FormatType(actual)}, oczekiwano {FormatType(parameter.Type)}",
            argument.Value.File, argument.Value.Line, argument.Value.Column));
    }

    private static void ValidateStorageMutationCall(
        string callableName,
        StorageMutationNode mutation,
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        if (scope.RuntimeTarget != RuntimePrimitiveTarget.Server)
        {
            diagnostics.Add(new Diagnostic(
                "SMC001", DiagnosticLevel.Error, "StorageMutationCall",
                $"named mutation '{callableName}' może być wywołana wyłącznie w tasku @server",
                call.File, call.Line, call.Column));
        }

        var namedCount = call.Args.Count(argument => argument.Name is not null);
        if (namedCount != 0 && namedCount != call.Args.Count)
        {
            diagnostics.Add(new Diagnostic(
                "SMC002", DiagnosticLevel.Error, "StorageMutationCall",
                $"wywołanie '{callableName}' nie może mieszać argumentów pozycyjnych i nazwanych",
                call.File, call.Line, call.Column));
            return;
        }

        if (namedCount == 0)
        {
            for (var index = 0; index < Math.Min(call.Args.Count, mutation.Params.Count); index++)
                ValidateStorageMutationArgument(callableName, mutation.Params[index], call.Args[index], symbols, scope, diagnostics);
            return;
        }

        var supplied = new Dictionary<string, CallArgNode>(StringComparer.Ordinal);
        foreach (var argument in call.Args)
        {
            var name = argument.Name!;
            if (!supplied.TryAdd(name, argument))
            {
                diagnostics.Add(new Diagnostic(
                    "SMC003", DiagnosticLevel.Error, "StorageMutationCall",
                    $"argument '{name}' został przekazany więcej niż raz do '{callableName}'",
                    argument.Value.File, argument.Value.Line, argument.Value.Column));
            }
            else if (!mutation.Params.Any(parameter => parameter.Name == name))
            {
                diagnostics.Add(new Diagnostic(
                    "SMC004", DiagnosticLevel.Error, "StorageMutationCall",
                    $"named mutation '{callableName}' nie deklaruje argumentu '{name}'",
                    argument.Value.File, argument.Value.Line, argument.Value.Column));
            }
        }

        foreach (var parameter in mutation.Params)
        {
            if (supplied.TryGetValue(parameter.Name, out var argument))
                ValidateStorageMutationArgument(callableName, parameter, argument, symbols, scope, diagnostics);
        }
    }

    private static void ValidateStorageMutationArgument(
        string callableName,
        ParamNode parameter,
        CallArgNode argument,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        var actual = InferExprType(argument.Value, symbols, scope);
        if (actual is null || TypeEquals(parameter.Type, actual))
            return;

        diagnostics.Add(new Diagnostic(
            "SMC005", DiagnosticLevel.Error, "StorageMutationCall",
            $"argument '{parameter.Name}' w '{callableName}' ma typ {FormatType(actual)}, oczekiwano {FormatType(parameter.Type)}",
            argument.Value.File, argument.Value.Line, argument.Value.Column));
    }

    private static void ValidateRuntimePrimitiveCall(
        string name,
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        if (!RuntimePrimitiveRegistry.TryGet(name, out var primitive))
            return;

        ValidateRuntimePrimitiveUse(name, scope, diagnostics, call.File, call.Line, call.Column);

        if (name.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            ValidateLatestCall(call, symbols, scope, diagnostics);
            return;
        }

        if (name.Equals("batch", StringComparison.OrdinalIgnoreCase))
        {
            ValidateBatchCall(call, symbols, scope, diagnostics);
            return;
        }

        if (!name.Equals("recv_any", StringComparison.OrdinalIgnoreCase))
            return;

        if (call.Args.Count < 2)
        {
            diagnostics.Add(new Diagnostic(
                "E0045", DiagnosticLevel.Error, "Semantic",
                "recv_any requires at least two communication sources",
                call.File, call.Line, call.Column));
            return;
        }

        foreach (var arg in call.Args)
        {
            var sourceType = InferExprType(arg.Value, symbols, scope);
            if (sourceType is CommTypeNode)
                continue;

            var got = sourceType is null ? "unknown" : FormatType(sourceType);
            diagnostics.Add(new Diagnostic(
                "E0046", DiagnosticLevel.Error, "Semantic",
                $"recv_any source must be chan<T>, queue<T> or stream<T>, got {got}",
                arg.Value.File, arg.Value.Line, arg.Value.Column));
        }
    }

    private static void ValidateLatestCall(
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        if (call.Args.Count != 1)
        {
            diagnostics.Add(new Diagnostic(
                "E0048", DiagnosticLevel.Error, "Semantic",
                $"latest requires exactly one stream source, got {call.Args.Count}",
                call.File, call.Line, call.Column));
            return;
        }

        var source = call.Args[0].Value;
        var sourceType = InferExprType(source, symbols, scope);
        if (sourceType is CommTypeNode { Kind: CommKind.Stream })
            return;

        var got = sourceType is null ? "unknown" : FormatType(sourceType);
        diagnostics.Add(new Diagnostic(
            "E0048", DiagnosticLevel.Error, "Semantic",
            $"latest source must be stream<T>, got {got}",
            source.File, source.Line, source.Column));
    }

    private static void ValidateBatchCall(
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        if (call.Args.Count != 2)
        {
            diagnostics.Add(new Diagnostic(
                "E0049", DiagnosticLevel.Error, "Semantic",
                $"batch requires a queue source and Int limit, got {call.Args.Count} arguments",
                call.File, call.Line, call.Column));
            return;
        }

        var source = call.Args[0].Value;
        var sourceType = InferExprType(source, symbols, scope);
        if (sourceType is CommTypeNode { Kind: CommKind.Stream })
        {
            diagnostics.Add(new Diagnostic(
                "E0049", DiagnosticLevel.Error, "Semantic",
                "batch(stream<T>, ...) is not stabilized because stream<T> is latest-wins, not historical",
                source.File, source.Line, source.Column));
        }
        else if (sourceType is not CommTypeNode { Kind: CommKind.Queue })
        {
            var got = sourceType is null ? "unknown" : FormatType(sourceType);
            diagnostics.Add(new Diagnostic(
                "E0049", DiagnosticLevel.Error, "Semantic",
                $"batch source must be queue<T>, got {got}",
                source.File, source.Line, source.Column));
        }

        var limit = call.Args[1].Value;
        var limitType = InferExprType(limit, symbols, scope);
        if (limitType is NamedTypeNode { Name: var name } &&
            name.Equals("Int", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var limitGot = limitType is null ? "unknown" : FormatType(limitType);
        diagnostics.Add(new Diagnostic(
            "E0049", DiagnosticLevel.Error, "Semantic",
            $"batch limit must be Int, got {limitGot}",
            limit.File, limit.Line, limit.Column));
    }

    private static void ValidateRuntimePrimitiveUse(
        string name,
        LocalScope scope,
        List<Diagnostic> diagnostics,
        string file,
        int line,
        int column)
    {
        if (!RuntimePrimitiveRegistry.TryGet(name, out var primitive))
            return;

        if (primitive.Status == RuntimePrimitiveStatus.Proposed)
        {
            diagnostics.Add(new Diagnostic(
                "R0001", DiagnosticLevel.Warning, "Runtime",
                $"runtime primitive '{name}' is proposed and experimental: {primitive.Summary}",
                file, line, column));
        }

        if (scope.RuntimeTarget != RuntimePrimitiveTarget.None &&
            !primitive.SupportsTarget(scope.RuntimeTarget))
        {
            diagnostics.Add(new Diagnostic(
                "R0002", DiagnosticLevel.Error, "Runtime",
                $"runtime primitive '{name}' is not supported on this target",
                file, line, column));
            return;
        }

        var missingImplementations = MissingImplementationsForTarget(primitive, scope.RuntimeTarget);
        if (missingImplementations.Count > 0)
        {
            diagnostics.Add(new Diagnostic(
                "R0003", DiagnosticLevel.Error, "Runtime",
                $"runtime primitive '{name}' has no implementation for this target: missing {string.Join(", ", missingImplementations)}",
                file, line, column));
        }
    }

    private static IReadOnlyList<string> MissingImplementationsForTarget(
        RuntimePrimitive primitive,
        RuntimePrimitiveTarget target)
    {
        if (target == RuntimePrimitiveTarget.None)
            return [];

        var missing = new List<string>();
        AddMissing(RuntimePrimitiveTarget.Server, RuntimePrimitiveImplementation.GoCodegen);
        AddMissing(RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.TypeScriptCodegen);
        AddMissing(RuntimePrimitiveTarget.Embedded, RuntimePrimitiveImplementation.CppCodegen);
        return missing;

        void AddMissing(RuntimePrimitiveTarget targetFlag, RuntimePrimitiveImplementation implementation)
        {
            if ((target & targetFlag) != targetFlag)
                return;

            if (!primitive.HasImplementation(implementation))
                missing.Add(implementation.ToString());
        }
    }

    private static void ValidateFieldAccess(
        FieldAccessNode field,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        // Validate the base expression first
        ValidateExpr(field.Base, symbols, scope, diagnostics);

        // If the base is a call to an undeclared function, the field access
        // will also be broken — but we already reported E0020 on the call.
        // If the base is a variable whose type is unknown (e.g. result of
        // undeclared extern), emit E0022.
        if (field.Base is IdentifierExprNode baseId)
        {
            // The variable is in scope but its type is not statically known
            // (we don't do full type inference yet — that's a future pass).
            // We only flag E0022 when the base comes from a call to an
            // undeclared function, which has already been reported as E0020.
            // So nothing extra here — the E0020 on the call is sufficient.
            _ = baseId; // suppress unused warning
        }

        if (field.Base is CallExprNode baseCall &&
            baseCall.Callee is IdentifierExprNode calleeId)
        {
            // If the callee was undeclared, the field access is opaque
            if (!scope.Contains(calleeId.Name) &&
                !symbols.Callables.ContainsKey(calleeId.Name) &&
                !RuntimePrimitiveRegistry.IsKnown(calleeId.Name))
            {
                diagnostics.Add(new Diagnostic(
                    "W0022", DiagnosticLevel.Warning, "Semantic",
                    $"field access on opaque type: '{calleeId.Name}(...).{field.Field}'",
                    field.File, field.Line, field.Column));
            }
        }
    }

    private static void ValidateStructLiteral(
        StructLiteralNode structLit,
        GlobalSymbolTable symbols,
        LocalScope scope,
        List<Diagnostic> diagnostics)
    {
        // Validate struct name is a known type
        if (!symbols.Types.ContainsKey(structLit.Name) &&
            !s_builtinTypes.Contains(structLit.Name))
        {
            diagnostics.Add(new Diagnostic(
                "E0021", DiagnosticLevel.Warning, "Semantic",
                $"unknown type: '{structLit.Name}'",
                structLit.File, structLit.Line, structLit.Column));
        }

        foreach (var f in structLit.Fields)
            ValidateExpr(f.Value, symbols, scope, diagnostics);
    }

    private static TypeRefNode? InferExprType(
        ExprNode expr,
        GlobalSymbolTable symbols,
        LocalScope scope)
    {
        return expr switch
        {
            LiteralNode literal => InferLiteralType(literal),
            IdentifierExprNode id => scope.TryGetType(id.Name, out var type) ? type : null,
            StructLiteralNode structLiteral => new NamedTypeNode(structLiteral.File, structLiteral.Line, structLiteral.Column, structLiteral.Name),
            RecvExprNode recv => InferRecvType(recv.Source, symbols, scope),
            SendExprNode => new NamedTypeNode(expr.File, expr.Line, expr.Column, "Bool"),
            ParenExprNode paren => InferExprType(paren.Inner, symbols, scope),
            CallExprNode call => InferCallReturnType(call, symbols, scope),
            FieldAccessNode field => InferFieldAccessType(field, symbols, scope),
            UnaryExprNode unary => InferUnaryType(unary, symbols, scope),
            BinaryExprNode binary => InferBinaryType(binary, symbols, scope),
            ArrayLiteralNode array => InferArrayLiteralType(array, symbols, scope),
            _ => null
        };
    }

    private static TypeRefNode? InferLiteralType(LiteralNode literal)
    {
        if (literal.RawText.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ||
            literal.RawText.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            literal.RawText.EndsWith("m", StringComparison.OrdinalIgnoreCase) ||
            literal.RawText.EndsWith("h", StringComparison.OrdinalIgnoreCase))
        {
            return BuiltinType(literal, "Duration");
        }

        return literal.Value switch
        {
            bool => BuiltinType(literal, "Bool"),
            string => BuiltinType(literal, "Text"),
            float or double or decimal => BuiltinType(literal, "Float"),
            int or long or short or byte => BuiltinType(literal, "Int"),
            _ => null
        };
    }

    private static TypeRefNode? InferRecvType(
        ExprNode source,
        GlobalSymbolTable symbols,
        LocalScope scope)
        => InferExprType(source, symbols, scope) is CommTypeNode comm ? comm.Element : null;

    private static TypeRefNode? InferCallReturnType(CallExprNode call, GlobalSymbolTable symbols, LocalScope scope)
    {
        if (call.Callee is not IdentifierExprNode callee)
            return null;

        if (RuntimePrimitiveRegistry.IsKnown(callee.Name))
        {
            if (callee.Name.Equals("recv_any", StringComparison.OrdinalIgnoreCase))
                return InferRecvAnyType(call, symbols, scope);

            if (callee.Name.Equals("latest", StringComparison.OrdinalIgnoreCase))
                return InferLatestType(call, symbols, scope);

            if (callee.Name.Equals("batch", StringComparison.OrdinalIgnoreCase))
                return InferBatchType(call, symbols, scope);

            if (TryGetNumericCastReturnType(callee.Name, out var numericCastType))
                return BuiltinType(callee, numericCastType);

            return callee.Name.Equals("now", StringComparison.OrdinalIgnoreCase) ||
                   callee.Name.Equals("epoch", StringComparison.OrdinalIgnoreCase)
                ? BuiltinType(callee, "Time")
                : null;
        }

        return symbols.Callables.TryGetValue(callee.Name, out var declaration)
            ? declaration switch
            {
                TaskDeclarationNode task => task.OutputType,
                FnDeclarationNode function => function.ReturnType,
                ExternTaskNode external => external.OutputType,
                StorageQueryNode when symbols.StorageQueryRows.TryGetValue(callee.Name, out var row) =>
                    new GenericTypeNode(call.File, call.Line, call.Column, "List",
                        [new NamedTypeNode(call.File, call.Line, call.Column, row.Name)]),
                StorageMutationNode when symbols.StorageMutationResults.TryGetValue(callee.Name, out var result) =>
                    new NamedTypeNode(call.File, call.Line, call.Column, result.Name),
                _ => null
            }
            : null;
    }

    private static TypeRefNode? InferLatestType(
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope)
        => call.Args.Count == 1 &&
           InferExprType(call.Args[0].Value, symbols, scope) is CommTypeNode { Kind: CommKind.Stream } stream
            ? stream.Element
            : null;

    private static TypeRefNode? InferBatchType(
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope)
        => call.Args.Count == 2 &&
           InferExprType(call.Args[0].Value, symbols, scope) is CommTypeNode { Kind: CommKind.Queue } queue
            ? new GenericTypeNode(call.File, call.Line, call.Column, "List", [queue.Element])
            : null;

    private static TypeRefNode? InferRecvAnyType(
        CallExprNode call,
        GlobalSymbolTable symbols,
        LocalScope scope)
    {
        var elementTypes = call.Args
            .Select(arg => InferExprType(arg.Value, symbols, scope))
            .OfType<CommTypeNode>()
            .Select(comm => comm.Element)
            .ToList();

        if (elementTypes.Count == 0)
            return null;

        var distinct = new List<TypeRefNode>();
        foreach (var elementType in elementTypes)
        {
            if (!distinct.Any(existing => TypeEquals(existing, elementType)))
                distinct.Add(elementType);
        }

        return distinct.Count == 1
            ? distinct[0]
            : new UnionTypeNode(call.File, call.Line, call.Column, distinct);
    }

    private static bool TryGetNumericCastReturnType(string name, out string typeName)
    {
        typeName = name.ToLowerInvariant() switch
        {
            "f32" or "float32" => "f32",
            "f64" or "float64" => "f64",
            "i8" or "int8" => "i8",
            "i16" or "int16" => "i16",
            "i32" or "int32" => "i32",
            "i64" or "int64" => "i64",
            "u8" or "uint8" => "u8",
            "u16" or "uint16" => "u16",
            "u32" or "uint32" => "u32",
            "u64" or "uint64" => "u64",
            _ => string.Empty
        };

        return typeName.Length > 0;
    }

    private static TypeRefNode? InferFieldAccessType(
        FieldAccessNode field,
        GlobalSymbolTable symbols,
        LocalScope scope)
    {
        var baseType = InferExprType(field.Base, symbols, scope);
        if (baseType is not NamedTypeNode named ||
            !symbols.Types.TryGetValue(named.Name, out var declaration))
            return null;

        return declaration switch
        {
            StructDeclarationNode structure => structure.Fields
                .FirstOrDefault(f => f.Name.Equals(field.Field, StringComparison.OrdinalIgnoreCase))
                ?.Type,
            _ => null
        };
    }

    private static TypeRefNode? InferUnaryType(
        UnaryExprNode unary,
        GlobalSymbolTable symbols,
        LocalScope scope)
    {
        if (unary.Op is "not" or "!")
            return BuiltinType(unary, "Bool");

        return InferExprType(unary.Operand, symbols, scope);
    }

    private static TypeRefNode? InferBinaryType(
        BinaryExprNode binary,
        GlobalSymbolTable symbols,
        LocalScope scope)
    {
        if (binary.Op == "?:" &&
            binary.Right is BinaryExprNode { Op: ":" } branches)
        {
            var thenType = InferExprType(branches.Left, symbols, scope);
            var elseType = InferExprType(branches.Right, symbols, scope);
            return thenType is not null && elseType is not null && TypeEquals(thenType, elseType)
                ? thenType
                : thenType ?? elseType;
        }

        if (binary.Op is "==" or "!=" or "<" or "<=" or ">" or ">=" or "and" or "&&" or "or" or "||")
            return BuiltinType(binary, "Bool");

        var leftType = InferExprType(binary.Left, symbols, scope);
        var rightType = InferExprType(binary.Right, symbols, scope);
        if (leftType is NamedTypeNode { Name: "Float" } || rightType is NamedTypeNode { Name: "Float" })
            return BuiltinType(binary, "Float");

        return leftType ?? rightType;
    }

    private static TypeRefNode? InferArrayLiteralType(
        ArrayLiteralNode array,
        GlobalSymbolTable symbols,
        LocalScope scope)
    {
        var firstType = array.Elements.Select(element => InferExprType(element, symbols, scope)).FirstOrDefault(type => type is not null);
        return firstType is null
            ? null
            : new GenericTypeNode(array.File, array.Line, array.Column, "List", [firstType]);
    }

    private static TypeRefNode? InferIterationElementType(TypeRefNode? type)
        => type switch
        {
            CommTypeNode comm => comm.Element,
            ArrayTypeNode array => array.Element,
            GenericTypeNode { Name: "List", TypeArgs.Count: 1 } list => list.TypeArgs[0],
            _ => null
        };

    private static NamedTypeNode BuiltinType(AstNode node, string name)
        => new(node.File, node.Line, node.Column, name);

    private static RuntimePrimitiveTarget GetRuntimePrimitiveTarget(IReadOnlyList<PlatformAnnotation> platforms)
    {
        var target = RuntimePrimitiveTarget.None;
        foreach (var platform in platforms)
        {
            target |= platform.Target.ToLowerInvariant() switch
            {
                "server" => RuntimePrimitiveTarget.Server,
                "report" => RuntimePrimitiveTarget.Report,
                "embedded" => RuntimePrimitiveTarget.Embedded,
                _ => RuntimePrimitiveTarget.None
            };
        }

        return target;
    }

    // ── Type reference validation ─────────────────────────────────────────────

    private static void ValidateTypeRef(
        TypeRefNode typeRef,
        GlobalSymbolTable symbols,
        List<Diagnostic> diagnostics)
    {
        switch (typeRef)
        {
            case NamedTypeNode named:
                if (!s_builtinTypes.Contains(named.Name) &&
                    !symbols.Types.ContainsKey(named.Name))
                {
                    diagnostics.Add(new Diagnostic(
                        "E0021", DiagnosticLevel.Warning, "Semantic",
                        $"unknown type: '{named.Name}'",
                        named.File, named.Line, named.Column));
                }
                break;

            case CommTypeNode comm:
                ValidateTypeRef(comm.Element, symbols, diagnostics);
                break;

            case MapTypeNode map:
                ValidateTypeRef(map.Key, symbols, diagnostics);
                ValidateTypeRef(map.Value, symbols, diagnostics);
                break;

            case GenericTypeNode generic:
                // Don't validate the generic name — could be user-defined generic
                foreach (var arg in generic.TypeArgs)
                    ValidateTypeRef(arg, symbols, diagnostics);
                break;

            case ArrayTypeNode arr:
                ValidateTypeRef(arr.Element, symbols, diagnostics);
                break;

            case UnionTypeNode union:
                foreach (var v in union.Variants)
                    ValidateTypeRef(v, symbols, diagnostics);
                break;
        }
    }

    private static bool NullableTypeEquals(TypeRefNode? expected, TypeRefNode? actual) =>
        expected is null
            ? actual is null
            : actual is not null && TypeEquals(expected, actual);

    private static bool TypeEquals(TypeRefNode expected, TypeRefNode actual) =>
        expected switch
        {
            NamedTypeNode expectedNamed when actual is NamedTypeNode actualNamed =>
                expectedNamed.Name.Equals(actualNamed.Name, StringComparison.OrdinalIgnoreCase),

            GenericTypeNode expectedGeneric when actual is GenericTypeNode actualGeneric =>
                expectedGeneric.Name.Equals(actualGeneric.Name, StringComparison.OrdinalIgnoreCase) &&
                expectedGeneric.TypeArgs.Count == actualGeneric.TypeArgs.Count &&
                expectedGeneric.TypeArgs.Zip(actualGeneric.TypeArgs).All(pair => TypeEquals(pair.First, pair.Second)),

            CommTypeNode expectedComm when actual is CommTypeNode actualComm =>
                expectedComm.Kind == actualComm.Kind &&
                TypeEquals(expectedComm.Element, actualComm.Element),

            ArrayTypeNode expectedArray when actual is ArrayTypeNode actualArray =>
                TypeEquals(expectedArray.Element, actualArray.Element),

            MapTypeNode expectedMap when actual is MapTypeNode actualMap =>
                TypeEquals(expectedMap.Key, actualMap.Key) &&
                TypeEquals(expectedMap.Value, actualMap.Value),

            UnionTypeNode expectedUnion when actual is UnionTypeNode actualUnion =>
                expectedUnion.Variants.Count == actualUnion.Variants.Count &&
                expectedUnion.Variants.Zip(actualUnion.Variants).All(pair => TypeEquals(pair.First, pair.Second)),

            _ => false
        };

    private static string FormatNullableType(TypeRefNode? type) =>
        type is null ? "void" : FormatType(type);

    private static string FormatType(TypeRefNode type) => type switch
    {
        NamedTypeNode named => named.Name,
        GenericTypeNode generic => $"{generic.Name}<{string.Join(", ", generic.TypeArgs.Select(FormatType))}>",
        CommTypeNode comm => $"{comm.Kind.ToString().ToLowerInvariant()}<{FormatType(comm.Element)}>",
        ArrayTypeNode array => $"[{FormatType(array.Element)}]",
        MapTypeNode map => $"Map<{FormatType(map.Key)}, {FormatType(map.Value)}>",
        UnionTypeNode union => string.Join(" | ", union.Variants.Select(FormatType)),
        _ => type.ToString() ?? "unknown"
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Supporting types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Global symbol table — populated in Pass 1.</summary>
internal sealed class GlobalSymbolTable
{
    /// <summary>Declared type names: struct, enum, extern type.</summary>
    public Dictionary<string, AstNode> Types { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Declared callable names: task, fn, extern task.</summary>
    public Dictionary<string, AstNode> Callables { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Declared external integration boundary names.</summary>
    public Dictionary<string, AstNode> ExternContracts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Syntetyczny typ wyniku przypisany do językowego wywołania named query.</summary>
    public Dictionary<string, StructDeclarationNode> StorageQueryRows { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Syntetyczny typ wyniku przypisany do językowego wywołania named mutation.</summary>
    public Dictionary<string, StructDeclarationNode> StorageMutationResults { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Lexical scope for local variable names within a task/fn body.</summary>
internal sealed class LocalScope
{
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TypeRefNode?> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ackableNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly LocalScope? _parent;

    internal LocalScope(RuntimePrimitiveTarget runtimeTarget = RuntimePrimitiveTarget.None)
    {
        RuntimeTarget = runtimeTarget;
    }

    private LocalScope(LocalScope parent)
    {
        _parent = parent;
        RuntimeTarget = parent.RuntimeTarget;
    }

    internal RuntimePrimitiveTarget RuntimeTarget { get; }

    /// <summary>Declare a local variable in this scope.</summary>
    internal void Declare(string name, TypeRefNode? type = null, bool ackable = false)
    {
        _names.Add(name);
        _types[name] = type;
        if (ackable)
            _ackableNames.Add(name);
    }

    /// <summary>Check if a name is in scope (current or any ancestor).</summary>
    internal bool Contains(string name)
        => _names.Contains(name) || (_parent?.Contains(name) ?? false);

    /// <summary>Resolve the statically known local variable type, if available.</summary>
    internal bool TryGetType(string name, out TypeRefNode? type)
    {
        if (_types.TryGetValue(name, out type))
            return type is not null;

        if (_parent is not null)
            return _parent.TryGetType(name, out type);

        type = null;
        return false;
    }

    /// <summary>Check whether a local variable is a queue message eligible for ACK/NACK.</summary>
    internal bool IsAckable(string name)
        => _ackableNames.Contains(name) || (_parent?.IsAckable(name) ?? false);

    /// <summary>Create a child scope (for if/for/loop bodies).</summary>
    internal LocalScope Child() => new(this);
}
