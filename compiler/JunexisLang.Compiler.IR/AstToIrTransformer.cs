using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.IR;

/// <summary>
/// Transformuje drzewo AST (<see cref="ProgramNode"/>) w pośrednią reprezentację IR
/// (<see cref="IRDocument"/>).
/// </summary>
public sealed class AstToIrTransformer
{
    // ─── Punkt wejścia ───────────────────────────────────────────────────────

    /// <summary>Transformuje cały program do IR.</summary>
    public IRDocument Transform(ProgramNode program) => TransformCore(program);

    /// <summary>
    /// Transformuje program bez wyrzucania błędów kontraktu na zewnątrz.
    /// Nie zwraca częściowego IR.
    /// </summary>
    public IRTransformResult TransformWithDiagnostics(ProgramNode program)
    {
        try
        {
            return IRTransformResult.Ok(TransformCore(program));
        }
        catch (IRTransformException ex)
        {
            return IRTransformResult.Fail(new Diagnostic(
                "R0001", DiagnosticLevel.Error, "IR", ex.Message,
                ex.Node.File, ex.Node.Line, ex.Node.Column));
        }
        catch (Exception ex)
        {
            return IRTransformResult.Fail(new Diagnostic(
                "R0002", DiagnosticLevel.Error, "IR",
                $"Unexpected Structural IR transformation failure: {ex.Message}",
                program.File, program.Line, program.Column));
        }
    }

    private IRDocument TransformCore(ProgramNode program)
    {
        var imports         = program.Imports.Select(TransformImport).ToList();
        var structs         = new List<IRStruct>();
        var enums           = new List<IREnum>();
        var functions       = new List<IRFn>();
        var tasks           = new List<IRTask>();
        var pipelines       = new List<IRPipeline>();
        var queueBridges    = new List<IRQueueBridgeDescriptor>();
        var streamBridges   = new List<IRStreamBridgeDescriptor>();
        var chanBridges     = new List<IRChanBridgeDescriptor>();
        var devices         = new List<IRDevice>();
        var narratives      = new List<IRNarrative>();
        var deployments     = new List<IRDeployment>();
        var supervisors     = new List<IRSupervisor>();
        var quirks          = new List<IRQuirks>();
        var circuitBreakers = new List<IRCircuitBreaker>();
        var protocols       = new List<IRProtocol>();
        var externTasks     = new List<IRExternTask>();
        var externTypes     = new List<IRExternType>();
        var externContracts = new List<IRExternContract>();
        var storageDeclarations = new List<IRStorageDeclaration>();

        foreach (var decl in program.Declarations)
        {
            switch (decl)
            {
                case StructDeclarationNode n:      structs.Add(TransformStruct(n));             break;
                case EnumDeclarationNode n:        enums.Add(TransformEnum(n));                 break;
                case FnDeclarationNode n:          functions.Add(TransformFn(n));               break;
                case TaskDeclarationNode n:        tasks.Add(TransformTask(n));                 break;
                case PipelineDeclarationNode n:    pipelines.Add(TransformPipeline(n));         break;
                case DeviceDeclarationNode n:      devices.Add(TransformDevice(n));             break;
                case NarrativeDeclarationNode n:   narratives.Add(new(n.Label, n.Body));        break;
                case DeploymentDeclarationNode n:  deployments.Add(new(n.Label, n.Body));       break;
                case SupervisorDeclarationNode n:  supervisors.Add(new(n.Name, n.Body));        break;
                case QuirksDeclarationNode n:      quirks.Add(new(n.Label, n.Body));            break;
                case CircuitBreakerDeclarationNode n: circuitBreakers.Add(new(n.Name, n.Body)); break;
                case ProtocolDeclarationNode n:    protocols.Add(new(n.Name, n.Body));          break;
                case ExternTaskNode n:             externTasks.Add(TransformExternTask(n));     break;
                case ExternTypeNode n:             externTypes.Add(TransformExternType(n));     break;
                case ExternContractNode n:         externContracts.Add(TransformExternContract(n)); break;
                case StorageDeclarationNode n:     storageDeclarations.Add(TransformStorage(n)); break;
                default: throw Unsupported("top-level declaration", decl);
            }
        }

        queueBridges.AddRange(BuildQueueBridgeDescriptors(tasks, pipelines));
        streamBridges.AddRange(BuildStreamBridgeDescriptors(tasks, pipelines));
        chanBridges.AddRange(BuildChanBridgeDescriptors(tasks, pipelines));

        return new IRDocument(
            program.File,
            imports,
            structs, enums, functions, tasks, pipelines, queueBridges, streamBridges, chanBridges, devices,
            narratives, deployments, supervisors, quirks, circuitBreakers, protocols,
            externTasks, externTypes, externContracts)
        {
            SourceMap = IRSourceMapBuilder.Build(program),
            StorageDeclarations = storageDeclarations
        };
    }

    // ─── Import ──────────────────────────────────────────────────────────────

    private static IRImport TransformImport(ImportNode n) =>
        new(n.ModulePath, n.Alias,
            n.Symbols.Select(s => (s.Name, s.Alias)).ToList());

    // ─── Typy ────────────────────────────────────────────────────────────────

    private static IRType TransformType(TypeRefNode t) => t switch
    {
        NamedTypeNode n    => new IRNamedType(n.Name),
        CommTypeNode n     => new IRCommType(TransformCommKind(n.Kind), TransformType(n.Element)),
        MapTypeNode n      => new IRMapType(TransformType(n.Key), TransformType(n.Value)),
        GenericTypeNode n  => new IRGenericType(n.Name, n.TypeArgs.Select(TransformType).ToList()),
        ArrayTypeNode n    => new IRArrayType(TransformType(n.Element)),
        UnionTypeNode n    => new IRUnionType(n.Variants.Select(TransformType).ToList()),
        _                  => throw Unsupported("type", t)
    };

    private static IRCommKind TransformCommKind(CommKind k) => k switch
    {
        CommKind.Chan   => IRCommKind.Chan,
        CommKind.Queue  => IRCommKind.Queue,
        CommKind.Stream => IRCommKind.Stream,
        _               => throw new ArgumentOutOfRangeException(nameof(k))
    };

    // ─── Struct / Enum ───────────────────────────────────────────────────────

    private static IRStruct TransformStruct(StructDeclarationNode n) =>
        new(n.Name, n.Fields.Select(f =>
            new IRField(f.Name, TransformType(f.Type),
                        f.Default is not null ? TransformExpr(f.Default) : null)).ToList());

    private static IREnum TransformEnum(EnumDeclarationNode n) =>
        new(n.Name, n.TypeParams,
            n.Variants.Select(v =>
                new IREnumVariant(v.Name, v.Payload.Select(TransformType).ToList())).ToList());

    // ─── storage (D22/D35, M13/M14) ──────────────────────────────────────────

    private static IRStorageDeclaration TransformStorage(StorageDeclarationNode node) => new(
        node.Name,
        node.Entities.Select(entity => new IRStorageEntity(
            entity.Name,
            entity.Fields.Select(field => new IRStorageField(field.Name, TransformType(field.Type))).ToList(),
            entity.PrimaryKeys.Select(key => new IRStoragePrimaryKey(key.Fields)).ToList())).ToList(),
        node.Queries.Select(query => new IRStorageQuery(
            query.Name,
            query.Params.Select(TransformParam).ToList(),
            query.SourceEntity,
            query.Steps.Select(TransformStorageStep).ToList())).ToList(),
        node.Mutations.Select(mutation => new IRStorageMutation(
            mutation.Name,
            mutation.Params.Select(TransformParam).ToList(),
            mutation.Entity,
            mutation.Kind switch
            {
                StorageMutationKindNode.Insert => IRStorageMutationKind.Insert,
                StorageMutationKindNode.Update => IRStorageMutationKind.Update,
                StorageMutationKindNode.Delete => IRStorageMutationKind.Delete,
                _ => throw Unsupported("storage mutation kind", mutation)
            },
            mutation.Cardinality == StorageMutationCardinalityNode.Single
                ? IRStorageMutationCardinality.Single
                : IRStorageMutationCardinality.Many,
            mutation.Assignments.Select(assignment => new IRStorageMutationAssignment(
                assignment.Field,
                TransformExpr(assignment.Value))).ToList(),
            mutation.Predicate is null ? null : TransformExpr(mutation.Predicate))).ToList());

    private static IRStorageQueryStep TransformStorageStep(StorageQueryStepNode step) => step switch
    {
        StorageFilterStepNode filter => new IRStorageFilterStep(TransformExpr(filter.Predicate)),
        StorageProjectStepNode project => new IRStorageProjectStep(
            project.Projections.Select(item => new IRStorageProjection(item.Field, item.Alias)).ToList()),
        StorageSortStepNode sort => new IRStorageSortStep(
            sort.Keys.Select(key => new IRStorageSortKey(key.Field, key.Descending)).ToList()),
        StorageLimitStepNode limit => new IRStorageLimitStep(limit.Count, limit.Offset),
        _ => throw Unsupported("storage query step", step)
    };

    // ─── fn ──────────────────────────────────────────────────────────────────

    private static IRFn TransformFn(FnDeclarationNode n) =>
        new(n.Name,
            n.Params.Select(TransformParam).ToList(),
            n.ReturnType is not null ? TransformType(n.ReturnType) : null,
            n.Body.Select(TransformStmt).ToList());

    // ─── task ────────────────────────────────────────────────────────────────

    private IRTask TransformTask(TaskDeclarationNode n)
    {
        var states  = new List<IRStateDecl>();
        var configs = new List<IRConfigBlock>();
        var hooks   = new List<IRLifecycleHook>();
        var stmts   = new List<IRStmt>();
        IRNativeSection? native = null;

        foreach (var member in n.Body)
        {
            switch (member)
            {
                case StateDeclarationNode s:  states.Add(TransformState(s)); break;
                case ConfigBlockNode c:       configs.Add(TransformConfigBlock(c)); break;
                case LifecycleHookNode h:     hooks.Add(TransformHook(h)); break;
                case NativeSectionNode ns:    native = TransformNative(ns); break;
                case StmtNode s:              stmts.Add(TransformStmt(s)); break;
                default: throw Unsupported("task member", member);
            }
        }

        if (configs.Count > 1)
            throw new IRTransformException($"Task '{n.Name}' contains more than one config block.",
                n.Body.OfType<ConfigBlockNode>().Skip(1).First());

        if (n.Body.OfType<NativeSectionNode>().Skip(1).Any())
            throw new IRTransformException($"Task '{n.Name}' contains more than one native block.",
                n.Body.OfType<NativeSectionNode>().Skip(1).First());

        return new IRTask(
            n.Name,
            n.Params.Select(TransformParam).ToList(),
            n.Platforms.Select(TransformPlatform).ToList(),
            n.OutputType is not null ? TransformType(n.OutputType) : null,
            states,
            configs.FirstOrDefault(),
            hooks,
            stmts,
            native);
    }

    private static IRParam TransformParam(ParamNode p) =>
        new(p.Name, TransformType(p.Type),
            p.Default is not null ? TransformExpr(p.Default) : null);

    private static IRPlatform TransformPlatform(PlatformAnnotation pa) =>
        new(pa.Target, pa.Args.Select(a =>
            new IRPlatformArg(a.Key, TransformExpr(a.Value))).ToList());

    private static IRStateDecl TransformState(StateDeclarationNode s) =>
        new(s.Name, TransformType(s.Type),
            s.Init is not null ? TransformExpr(s.Init) : null);

    private IRConfigBlock TransformConfigBlock(ConfigBlockNode cb) =>
        new(cb.Fields.Select(f =>
            new IRConfigField(f.Name, TransformType(f.Type),
                              f.Default is not null ? TransformExpr(f.Default) : null)).ToList());

    private IRLifecycleHook TransformHook(LifecycleHookNode h) =>
        new(h.Event, h.Body.Select(TransformStmt).ToList());

    // ─── pipeline ────────────────────────────────────────────────────────────

    private static IRPipeline TransformPipeline(PipelineDeclarationNode n) =>
        new(n.Name, n.Connections.Select(TransformPipelineConn).ToList());

    private static IRPipelineConn TransformPipelineConn(PipelineConnectionNode c) => c switch
    {
        UseConnectionNode u    => new IRUseConn(u.SourceTask, u.SourcePort, u.TargetTask, u.TargetPort),
        ParallelBlockNode pb   => new IRParallelBlock(pb.Connections.Select(TransformPipelineConn).ToList()),
        _                      => throw Unsupported("pipeline connection", c)
    };

    private static IReadOnlyList<IRQueueBridgeDescriptor> BuildQueueBridgeDescriptors(
        IReadOnlyList<IRTask> tasks,
        IReadOnlyList<IRPipeline> pipelines)
    {
        var descriptors = new List<IRQueueBridgeDescriptor>();

        foreach (var pipeline in pipelines)
        {
            foreach (var connection in FlattenPipelineConnections(pipeline.Connections))
            {
                if (connection is not IRUseConn
                    {
                        SourceTask: { } sourceTask,
                        SourcePort: var sourcePort,
                        TargetTask: { } targetTask,
                        TargetPort: var targetPort
                    })
                    continue;

                var source = tasks.FirstOrDefault(task => task.Name == sourceTask);
                var target = tasks.FirstOrDefault(task => task.Name == targetTask);

                if (source is null || target is null)
                    continue;

                if (source.OutputType is not IRCommType { Kind: IRCommKind.Queue })
                    continue;

                if (!source.Platforms.Any(platform => platform.Target == "server"))
                    continue;

                if (!target.Platforms.Any(platform => platform.Target == "report"))
                    continue;

                if (!target.Params.Any(param => param.Type is IRCommType { Kind: IRCommKind.Queue }))
                    continue;

                descriptors.Add(new IRQueueBridgeDescriptor(
                    pipeline.Name,
                    sourceTask,
                    sourcePort,
                    targetTask,
                    targetPort,
                    BuildQueuePollRoute(sourceTask, sourcePort),
                    BuildQueueAckRoute(sourceTask, sourcePort)));
            }
        }

        return descriptors;
    }

    private static IReadOnlyList<IRStreamBridgeDescriptor> BuildStreamBridgeDescriptors(
        IReadOnlyList<IRTask> tasks,
        IReadOnlyList<IRPipeline> pipelines)
    {
        var descriptors = new List<IRStreamBridgeDescriptor>();

        foreach (var pipeline in pipelines)
        {
            foreach (var connection in FlattenPipelineConnections(pipeline.Connections))
            {
                if (connection is not IRUseConn
                    {
                        SourceTask: { } sourceTask,
                        SourcePort: var sourcePort,
                        TargetTask: { } targetTask,
                        TargetPort: var targetPort
                    })
                    continue;

                var source = tasks.FirstOrDefault(task => task.Name == sourceTask);
                var target = tasks.FirstOrDefault(task => task.Name == targetTask);

                if (source is null || target is null)
                    continue;

                if (source.OutputType is not IRCommType { Kind: IRCommKind.Stream })
                    continue;

                if (!source.Platforms.Any(platform => platform.Target == "server"))
                    continue;

                if (!target.Platforms.Any(platform => platform.Target == "report"))
                    continue;

                if (!target.Params.Any(param => param.Type is IRCommType { Kind: IRCommKind.Stream }))
                    continue;

                descriptors.Add(new IRStreamBridgeDescriptor(
                    pipeline.Name,
                    sourceTask,
                    sourcePort,
                    targetTask,
                    targetPort,
                    BuildStreamRoute(sourceTask, sourcePort)));
            }
        }

        return descriptors;
    }

    private static string BuildQueuePollRoute(string sourceTask, string? sourcePort)
        => sourcePort is not null ? $"/queue/{sourceTask}/{sourcePort}" : $"/queue/{sourceTask}";

    private static string BuildQueueAckRoute(string sourceTask, string? sourcePort)
        => sourcePort is not null ? $"/queue/{sourceTask}/{sourcePort}/ack" : $"/queue/{sourceTask}/ack";

    private static string BuildStreamRoute(string sourceTask, string? sourcePort)
        => sourcePort is not null ? $"/stream/{sourceTask}/{sourcePort}" : $"/stream/{sourceTask}";

    private static IReadOnlyList<IRChanBridgeDescriptor> BuildChanBridgeDescriptors(
        IReadOnlyList<IRTask> tasks,
        IReadOnlyList<IRPipeline> pipelines)
    {
        var descriptors = new List<IRChanBridgeDescriptor>();

        foreach (var pipeline in pipelines)
        {
            foreach (var connection in FlattenPipelineConnections(pipeline.Connections))
            {
                if (connection is not IRUseConn
                    {
                        SourceTask: { } sourceTask,
                        SourcePort: var sourcePort,
                        TargetTask: { } targetTask,
                        TargetPort: var targetPort
                    })
                    continue;

                var source = tasks.FirstOrDefault(task => task.Name == sourceTask);
                var target = tasks.FirstOrDefault(task => task.Name == targetTask);

                if (source is null || target is null)
                    continue;

                if (source.OutputType is not IRCommType { Kind: IRCommKind.Chan })
                    continue;

                if (!source.Platforms.Any(platform => platform.Target == "server"))
                    continue;

                if (!target.Platforms.Any(platform => platform.Target == "report"))
                    continue;

                if (!target.Params.Any(param => param.Type is IRCommType { Kind: IRCommKind.Chan }))
                    continue;

                descriptors.Add(new IRChanBridgeDescriptor(
                    pipeline.Name,
                    sourceTask,
                    sourcePort,
                    targetTask,
                    targetPort,
                    BuildChanRoute(sourceTask, sourcePort)));
            }
        }

        return descriptors;
    }

    private static string BuildChanRoute(string sourceTask, string? sourcePort)
        => sourcePort is not null ? $"/chan/{sourceTask}/{sourcePort}" : $"/chan/{sourceTask}";

    private static IEnumerable<IRPipelineConn> FlattenPipelineConnections(IReadOnlyList<IRPipelineConn> connections)
    {
        foreach (var connection in connections)
        {
            yield return connection;

            if (connection is IRParallelBlock parallelBlock)
            {
                foreach (var nested in FlattenPipelineConnections(parallelBlock.Connections))
                    yield return nested;
            }
        }
    }

    // ─── device ──────────────────────────────────────────────────────────────

    private IRDevice TransformDevice(DeviceDeclarationNode n)
    {
        IRConfigBlock? config = null;
        var blocks  = new List<IRDeviceBlock>();
        IRNativeSection? native = null;

        foreach (var m in n.Members)
        {
            switch (m)
            {
                case ConfigBlockNode cb:       config = TransformConfigBlock(cb); break;
                case DeviceBlockNode db:       blocks.Add(TransformDeviceBlock(db)); break;
                case NativeSectionNode ns:     native = TransformNative(ns); break;
                default: throw Unsupported("device member", m);
            }
        }

        if (n.Members.OfType<ConfigBlockNode>().Skip(1).Any())
            throw new IRTransformException($"Device '{n.Name}' contains more than one config block.",
                n.Members.OfType<ConfigBlockNode>().Skip(1).First());

        if (n.Members.OfType<NativeSectionNode>().Skip(1).Any())
            throw new IRTransformException($"Device '{n.Name}' contains more than one native block.",
                n.Members.OfType<NativeSectionNode>().Skip(1).First());

        return new IRDevice(n.Name, TransformPlatform(n.Platform), config, blocks, native);
    }

    private static IRDeviceBlock TransformDeviceBlock(DeviceBlockNode db) =>
        new(db.Name, db.Fields.Select(f =>
            new IRConfigField(f.Name, TransformType(f.Type),
                              f.Default is not null ? TransformExpr(f.Default) : null)).ToList());

    private static IRNativeSection TransformNative(NativeSectionNode ns) =>
        new(ns.Languages.Select(l =>
            new IRNativeLang(l.Name,
                l.Fields.Select(f => (f.Name, f.RawValue)).ToList())).ToList());

    // ─── extern ──────────────────────────────────────────────────────────────

    private static IRExternTask TransformExternTask(ExternTaskNode n) =>
        new(n.Name,
            n.Params.Select(TransformParam).ToList(),
            n.Platforms.Select(TransformPlatform).ToList(),
            n.OutputType is not null ? TransformType(n.OutputType) : null);

    private static IRExternType TransformExternType(ExternTypeNode n) =>
        new(n.Name, n.Platforms.Select(TransformPlatform).ToList());

    private static IRExternContract TransformExternContract(ExternContractNode n) =>
        new(n.Role,
            n.Name,
            n.Target,
            n.Language,
            n.UserFile,
            n.Functions.Select(TransformExternContractFunction).ToList());

    private static IRExternContractFunction TransformExternContractFunction(ExternContractFunctionNode n) =>
        new(n.Name,
            n.Params.Select(TransformParam).ToList(),
            n.ReturnType is not null ? TransformType(n.ReturnType) : null);

    // ─── Statements ──────────────────────────────────────────────────────────

    private static IRStmt TransformStmt(StmtNode s) => s switch
    {
        LetStmtNode n     => new IRLetStmt(n.BindingKind, n.Name,
                                n.Type is not null ? TransformType(n.Type) : null,
                                TransformExpr(n.Init)),
        ReturnStmtNode n  => new IRReturnStmt(n.Value is not null ? TransformExpr(n.Value) : null),
        DelayStmtNode n   => new IRDelayStmt(TransformExpr(n.Duration)),
        LogStmtNode n     => new IRLogStmt(n.Args.Select(TransformExpr).ToList()),
        SendStmtNode n    => new IRSendStmt(TransformExpr(n.Value),
                                n.Target is not null ? TransformExpr(n.Target) : null),
        RecvStmtNode n    => new IRRecvStmt(TransformExpr(n.Source)),
        AckStmtNode n     => new IRAckStmt(TransformExpr(n.Message)),
        NackStmtNode n    => new IRNackStmt(TransformExpr(n.Message),
                                n.Reason is not null ? TransformExpr(n.Reason) : null),
        RespondStmtNode n => new IRRespondStmt(TransformExpr(n.Value)),
        ExprStmtNode n    => new IRExprStmt(TransformExpr(n.Expr)),
        IfStmtNode n      => TransformIf(n),
        ForStmtNode n     => new IRForStmt(n.Var, TransformExpr(n.Iter),
                                n.Body.Select(TransformStmt).ToList()),
        EveryStmtNode n   => new IREveryStmt(TransformExpr(n.Interval),
                                n.Body.Select(TransformStmt).ToList()),
        LoopStmtNode n    => new IRLoopStmt(n.Body.Select(TransformStmt).ToList()),
        MatchStmtNode n   => new IRMatchStmt(TransformExpr(n.Subject),
                                n.Arms.Select(TransformArm).ToList()),
        _                 => throw Unsupported("statement", s)
    };

    private static IRIfStmt TransformIf(IfStmtNode n) =>
        new(TransformExpr(n.Condition),
            n.Then.Select(TransformStmt).ToList(),
            n.Else switch
            {
                IfStmtNode ei    => new IRElseIf(TransformIf(ei)),
                ElseBlockNode eb => new IRElseBlock(eb.Body.Select(TransformStmt).ToList()),
                null             => null,
                _                => throw Unsupported("else branch", n.Else)
            });

    private static IRMatchArm TransformArm(MatchArmNode arm) =>
        new(TransformPattern(arm.Pattern),
            arm.Body.Select(TransformStmt).ToList(),
            arm.BodyExpr is not null ? TransformExpr(arm.BodyExpr) : null);

    // ─── Patterns ────────────────────────────────────────────────────────────

    private static IRPattern TransformPattern(MatchPatternNode p) => p switch
    {
        WildcardPatternNode    => new IRWildcardPattern(),
        LiteralPatternNode n   => new IRLiteralPattern(TransformLiteral(n.Literal)),
        BindingPatternNode n   => new IRBindingPattern(n.Name),
        EnumVariantPatternNode n => new IREnumVariantPattern(
                                        n.TypeName, n.Variant,
                                        n.Fields.Select(TransformPattern).ToList()),
        _                      => throw Unsupported("match pattern", p)
    };

    // ─── Expressions ─────────────────────────────────────────────────────────

    private static IRExpr TransformExpr(ExprNode e) => e switch
    {
        LiteralNode n           => new IRLiteralExpr(TransformLiteral(n)),
        IdentifierExprNode n    => new IRIdentExpr(n.Name),
        ConfigAccessNode n      => new IRConfigAccessExpr(n.Field),
        // Trójnik: cond ? then : els — modelowany jako BinaryExprNode("?:", cond, BinaryExprNode(":", then, els))
        BinaryExprNode { Op: "?:" } n when n.Right is BinaryExprNode { Op: ":" } branches
                                => new IRTernaryExpr(TransformExpr(n.Left),
                                                     TransformExpr(branches.Left),
                                                     TransformExpr(branches.Right)),
        BinaryExprNode n        => new IRBinaryExpr(n.Op, TransformExpr(n.Left), TransformExpr(n.Right)),
        UnaryExprNode n         => new IRUnaryExpr(n.Op, TransformExpr(n.Operand)),
        CallExprNode n          => new IRCallExpr(TransformExpr(n.Callee),
                                       n.Args.Select(a => new IRCallArg(a.Name, TransformExpr(a.Value))).ToList()),
        FieldAccessNode n        => new IRFieldAccessExpr(TransformExpr(n.Base), n.Field),
        IndexExprNode n         => new IRIndexExpr(TransformExpr(n.Base), TransformExpr(n.Index)),
        EnumAccessNode n        => new IREnumAccessExpr(n.Type, n.Variant),
        StructLiteralNode n     => new IRCallExpr(new IRIdentExpr(n.Name),
                                       n.Fields.Select(f => new IRCallArg(f.Name, TransformExpr(f.Value))).ToList()),
        ArrayLiteralNode n      => new IRCallExpr(new IRIdentExpr("__array__"),
                                       n.Elements.Select(el => new IRCallArg(null, TransformExpr(el))).ToList()),
        JsonLiteralNode n       => new IRCallExpr(new IRIdentExpr("__json__"),
                                       n.Fields.Select(f => new IRCallArg(f.Key, TransformExpr(f.Value))).ToList()),
        ParenExprNode n         => TransformExpr(n.Inner),
        SendExprNode n          => new IRSendExpr(TransformExpr(n.Value),
                                       n.Target is not null ? TransformExpr(n.Target) : null),
        RecvExprNode n          => new IRRecvExpr(TransformExpr(n.Source)),
        _                       => throw Unsupported("expression", e)
    };

    // ─── Literals ────────────────────────────────────────────────────────────

    private static IRValue TransformLiteral(LiteralNode n)
    {
        var unit = TryParseUnit(n.RawText);
        if (unit is not null)
            return unit;

        return n.Value switch
        {
            bool b    => new IRBoolValue(b),
            int i     => new IRIntValue(i),
            long l    => new IRIntValue(l),
            double d  => new IRFloatValue(d),
            string s  => new IRTextValue(s),
            null      => new IRNullValue(),
            _         => new IRTextValue(n.RawText)
        };
    }

    private static IRValue? TryParseUnit(string raw)
    {
        // np. "5s", "100ms", "32KB", "4MB"
        var unitIdx = 0;
        while (unitIdx < raw.Length && (char.IsDigit(raw[unitIdx]) || raw[unitIdx] == '.'))
            unitIdx++;
        if (unitIdx == 0 || unitIdx == raw.Length) return null;
        if (!double.TryParse(raw[..unitIdx], System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out var mag))
            return null;
        return new IRUnitValue(mag, raw[unitIdx..]);
    }

    private static IRTransformException Unsupported(string role, AstNode node) =>
        new($"Unsupported {role} '{node.GetType().Name}' in Structural IR.", node);
}
