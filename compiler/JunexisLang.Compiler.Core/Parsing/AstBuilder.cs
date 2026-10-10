using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Core.Parsing;

/// <summary>
/// Buduje typowane drzewo AST (<see cref="ProgramNode"/>) z drzewa parsowania ANTLR4.
/// Używaj <see cref="Build"/> jako punktu wejścia.
/// </summary>
internal sealed class AstBuilder : JunexisLangBaseVisitor<AstNode?>
{
    private readonly string           _file;
    private readonly CommonTokenStream _tokens;

    private AstBuilder(string file, CommonTokenStream tokens)
    {
        _file   = file;
        _tokens = tokens;
    }

    /// <summary>
    /// Builduje <see cref="ProgramNode"/> z korzenia drzewa ANTLR4.
    /// </summary>
    public static ProgramNode Build(
        JunexisLangParser.ProgramContext ctx,
        CommonTokenStream                tokens,
        string                           file)
        => new AstBuilder(file, tokens).BuildProgram(ctx);

    // ────────────────────────────────────────────────────────────────────
    // Program
    // ────────────────────────────────────────────────────────────────────

    private ProgramNode BuildProgram(JunexisLangParser.ProgramContext ctx)
    {
        var imports = ctx.importDeclaration()
                         .Select(i => (ImportNode)Visit(i)!)
                         .ToList();
        var decls = ctx.topLevelDeclaration()
                       .Select(d => Visit(d) ?? throw new InvalidOperationException(
                           $"No AST node was produced for top-level declaration at {d.Start.Line}:{d.Start.Column + 1}."))
                       .ToList();
        return new ProgramNode(_file, imports, decls);
    }

    // ────────────────────────────────────────────────────────────────────
    // Import
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitImportDeclaration(
        [NotNull] JunexisLangParser.ImportDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var path    = ctx.modulePath().GetText().Replace("::", "::");  // zachowaj :: w ścieżce
        var alias   = ctx.AS() != null ? ctx.IDENTIFIER()?.GetText() : null;
        var symbols = ctx.importSymbolList() != null
            ? ctx.importSymbolList().importSymbol()
                 .Select(s => (ImportSymbolNode)VisitImportSymbol(s)!)
                 .ToList()
            : (IReadOnlyList<ImportSymbolNode>)[];
        return new ImportNode(_file, l, c, path, alias, symbols);
    }

    public override AstNode? VisitImportSymbol(
        [NotNull] JunexisLangParser.ImportSymbolContext ctx)
    {
        var (l, c) = Loc(ctx);
        var names = ctx.anyIdentifier();
        var name  = names[0].GetText();
        var alias = names.Length > 1 ? names[1].GetText() : null;
        return new ImportSymbolNode(_file, l, c, name, alias);
    }

    // ────────────────────────────────────────────────────────────────────
    // Top-level dispatch
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitTopLevelDeclaration(
        [NotNull] JunexisLangParser.TopLevelDeclarationContext ctx)
        => Visit(ctx.GetChild(0));

    // ────────────────────────────────────────────────────────────────────
    // struct
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitStructDeclaration(
        [NotNull] JunexisLangParser.StructDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var fields = ctx.structField()
                        .Select(f => BuildStructField(f))
                        .ToList();
        return new StructDeclarationNode(_file, l, c, ctx.IDENTIFIER().GetText(), fields);
    }

    private StructFieldNode BuildStructField(JunexisLangParser.StructFieldContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new StructFieldNode(
            _file, l, c,
            ctx.IDENTIFIER().GetText(),
            BuildTypeRef(ctx.typeReference()),
            ctx.expression() != null ? (ExprNode)Visit(ctx.expression())! : null);
    }

    // ────────────────────────────────────────────────────────────────────
    // enum
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitEnumDeclaration(
        [NotNull] JunexisLangParser.EnumDeclarationContext ctx)
    {
        var (l, c)   = Loc(ctx);
        var typeIds  = ctx.IDENTIFIER();
        var name     = typeIds[0].GetText();
        // Parametry typów jeśli < i > są w gramatyce
        var typeParams = new List<string>();
        if (ctx.LT() != null)
            for (int i = 1; i < typeIds.Length; i++)
                typeParams.Add(typeIds[i].GetText());

        var variants = ctx.enumVariant()
                          .Select(v => BuildEnumVariant(v))
                          .ToList();
        return new EnumDeclarationNode(_file, l, c, name, typeParams, variants);
    }

    private EnumVariantNode BuildEnumVariant(JunexisLangParser.EnumVariantContext ctx)
    {
        var (l, c) = Loc(ctx);
        var payload = ctx.typeReference()
                        .Select(t => BuildTypeRef(t))
                        .ToList();
        return new EnumVariantNode(_file, l, c, ctx.IDENTIFIER().GetText(), payload);
    }

    // ────────────────────────────────────────────────────────────────────
    // storage (D22/D35, M13/M14)
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitStorageDeclaration(
        [NotNull] JunexisLangParser.StorageDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var entities = ctx.storageMember()
            .Where(member => member.storageEntityDeclaration() is not null)
            .Select(member => BuildStorageEntity(member.storageEntityDeclaration()!))
            .ToList();
        var queries = ctx.storageMember()
            .Where(member => member.storageQueryDeclaration() is not null)
            .Select(member => BuildStorageQuery(member.storageQueryDeclaration()!))
            .ToList();
        var mutations = ctx.storageMember()
            .Where(member => member.storageMutationDeclaration() is not null)
            .Select(member => BuildStorageMutation(member.storageMutationDeclaration()!))
            .ToList();
        return new StorageDeclarationNode(_file, l, c, ctx.anyIdentifier().GetText(), entities, queries, mutations);
    }

    private StorageEntityNode BuildStorageEntity(JunexisLangParser.StorageEntityDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var fields = ctx.storageEntityMember()
            .Where(member => member.storageModelField() is not null)
            .Select(member => BuildStorageField(member.storageModelField()!))
            .ToList();
        var keys = ctx.storageEntityMember()
            .Where(member => member.storagePrimaryKey() is not null)
            .Select(member => BuildStoragePrimaryKey(member.storagePrimaryKey()!))
            .ToList();
        return new StorageEntityNode(_file, l, c, ctx.anyIdentifier().GetText(), fields, keys);
    }

    private StorageFieldNode BuildStorageField(JunexisLangParser.StorageModelFieldContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new StorageFieldNode(_file, l, c, ctx.anyIdentifier().GetText(), BuildTypeRef(ctx.typeReference()));
    }

    private StoragePrimaryKeyNode BuildStoragePrimaryKey(JunexisLangParser.StoragePrimaryKeyContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new StoragePrimaryKeyNode(_file, l, c, ctx.anyIdentifier().Select(identifier => identifier.GetText()).ToList());
    }

    private StorageQueryNode BuildStorageQuery(JunexisLangParser.StorageQueryDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var identifiers = ctx.anyIdentifier();
        var parameters = ctx.storageQueryParameterList() is { } list
            ? list.storageQueryParameter().Select(BuildStorageQueryParameter).ToList()
            : [];
        var steps = ctx.storageQueryStep().Select(BuildStorageQueryStep).ToList();
        return new StorageQueryNode(
            _file, l, c,
            identifiers[0].GetText(),
            parameters,
            identifiers[1].GetText(),
            steps);
    }

    private ParamNode BuildStorageQueryParameter(JunexisLangParser.StorageQueryParameterContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new ParamNode(_file, l, c, ctx.anyIdentifier().GetText(), BuildTypeRef(ctx.typeReference()), null);
    }

    private StorageQueryStepNode BuildStorageQueryStep(JunexisLangParser.StorageQueryStepContext ctx)
    {
        if (ctx.storageFilterStep() is { } filter)
        {
            var (l, c) = Loc(filter);
            return new StorageFilterStepNode(_file, l, c, (ExprNode)Visit(filter.expression())!);
        }
        if (ctx.storageProjectStep() is { } project)
        {
            var (l, c) = Loc(project);
            return new StorageProjectStepNode(
                _file, l, c,
                project.storageProjection().Select(BuildStorageProjection).ToList());
        }
        if (ctx.storageSortStep() is { } sort)
        {
            var (l, c) = Loc(sort);
            return new StorageSortStepNode(
                _file, l, c,
                sort.storageSortKey().Select(BuildStorageSortKey).ToList());
        }
        if (ctx.storageLimitStep() is { } limit)
        {
            var (l, c) = Loc(limit);
            var values = limit.INTEGER_LITERAL()
                .Select(token => long.Parse(token.GetText().Replace("_", string.Empty, StringComparison.Ordinal)))
                .ToArray();
            return new StorageLimitStepNode(_file, l, c, values[0], values.Length > 1 ? values[1] : 0);
        }
        throw new InvalidOperationException("Nieznany storageQueryStep");
    }

    private StorageProjectionNode BuildStorageProjection(JunexisLangParser.StorageProjectionContext ctx)
    {
        var (l, c) = Loc(ctx);
        var identifiers = ctx.anyIdentifier();
        return new StorageProjectionNode(
            _file, l, c,
            identifiers[0].GetText(),
            identifiers.Length > 1 ? identifiers[1].GetText() : null);
    }

    private StorageSortKeyNode BuildStorageSortKey(JunexisLangParser.StorageSortKeyContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new StorageSortKeyNode(_file, l, c, ctx.anyIdentifier().GetText(), ctx.DESC() is not null);
    }

    private StorageMutationNode BuildStorageMutation(JunexisLangParser.StorageMutationDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var identifiers = ctx.anyIdentifier();
        var parameters = ctx.storageQueryParameterList() is { } list
            ? list.storageQueryParameter().Select(BuildStorageQueryParameter).ToList()
            : [];
        var assignments = ctx.storageMutationAssignment()
            .Select(assignment =>
            {
                var (assignmentLine, assignmentColumn) = Loc(assignment);
                return new StorageMutationAssignmentNode(
                    _file,
                    assignmentLine,
                    assignmentColumn,
                    assignment.anyIdentifier().GetText(),
                    (ExprNode)Visit(assignment.expression())!);
            })
            .ToList();
        var predicate = ctx.storageMutationWhere() is { } where
            ? (ExprNode)Visit(where.expression())!
            : null;
        var kind = ctx.storageMutationKind().Start.Type switch
        {
            JunexisLangParser.INSERT => StorageMutationKindNode.Insert,
            JunexisLangParser.UPDATE => StorageMutationKindNode.Update,
            JunexisLangParser.DELETE => StorageMutationKindNode.Delete,
            _ => throw new InvalidOperationException("Nieznany storageMutationKind")
        };
        var cardinality = ctx.storageMutationCardinality().SINGLE() is not null
            ? StorageMutationCardinalityNode.Single
            : StorageMutationCardinalityNode.Many;
        return new StorageMutationNode(
            _file,
            l,
            c,
            identifiers[0].GetText(),
            parameters,
            identifiers[1].GetText(),
            kind,
            cardinality,
            assignments,
            predicate);
    }

    // ────────────────────────────────────────────────────────────────────
    // fn
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitFnDeclaration(
        [NotNull] JunexisLangParser.FnDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var prms   = BuildParamList(ctx.parameterList());
        var retType = ctx.typeReference() != null ? BuildTypeRef(ctx.typeReference()) : null;
        var body    = ctx.fnBody().statement().Select(s => BuildStmt(s)).ToList();
        return new FnDeclarationNode(_file, l, c, ctx.IDENTIFIER().GetText(), prms, retType, body);
    }

    // ────────────────────────────────────────────────────────────────────
    // task
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitTaskDeclaration(
        [NotNull] JunexisLangParser.TaskDeclarationContext ctx)
    {
        var (l, c)   = Loc(ctx);
        var prms     = BuildParamList(ctx.parameterList());
        var platforms = ctx.platformAnnotation().Select(p => BuildPlatformAnnotation(p)).ToList();
        var outputType = ctx.communicationSignature() != null
            ? BuildTypeRef(ctx.communicationSignature().typeReference())
            : null;
        var body = ctx.taskBody().taskMember().Select(m => BuildTaskMember(m)).ToList();
        return new TaskDeclarationNode(_file, l, c, ctx.IDENTIFIER().GetText(), prms, platforms, outputType, body);
    }

    private AstNode BuildTaskMember(JunexisLangParser.TaskMemberContext ctx)
    {
        if (ctx.stateDeclaration()   != null) return BuildStateDecl(ctx.stateDeclaration());
        if (ctx.configBlock()        != null) return BuildConfigBlock(ctx.configBlock());
        if (ctx.nativeSection()      != null) return BuildNativeSection(ctx.nativeSection());
        if (ctx.lifecycleHook()      != null) return BuildLifecycleHook(ctx.lifecycleHook());
        if (ctx.statement()          != null) return BuildStmt(ctx.statement());
        throw new InvalidOperationException("Nieznany taskMember");
    }

    // ────────────────────────────────────────────────────────────────────
    // state / config / lifecycle / native
    // ────────────────────────────────────────────────────────────────────

    private StateDeclarationNode BuildStateDecl(JunexisLangParser.StateDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new StateDeclarationNode(
            _file, l, c,
            ctx.IDENTIFIER().GetText(),
            BuildTypeRef(ctx.typeReference()),
            ctx.expression() != null ? (ExprNode)Visit(ctx.expression())! : null);
    }

    private ConfigBlockNode BuildConfigBlock(JunexisLangParser.ConfigBlockContext ctx)
    {
        var (l, c) = Loc(ctx);
        var fields = ctx.configField().Select(f => BuildConfigField(f)).ToList();
        return new ConfigBlockNode(_file, l, c, fields);
    }

    private ConfigFieldNode BuildConfigField(JunexisLangParser.ConfigFieldContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new ConfigFieldNode(
            _file, l, c,
            ctx.IDENTIFIER().GetText(),
            BuildTypeRef(ctx.typeReference()),
            ctx.literal() != null ? BuildLiteral(ctx.literal()) : null);
    }

    private LifecycleHookNode BuildLifecycleHook(JunexisLangParser.LifecycleHookContext ctx)
    {
        var (l, c) = Loc(ctx);
        var body = ctx.statement().Select(s => BuildStmt(s)).ToList();
        return new LifecycleHookNode(_file, l, c, ctx.IDENTIFIER().GetText(), body);
    }

    private NativeSectionNode BuildNativeSection(JunexisLangParser.NativeSectionContext ctx)
    {
        var (l, c)  = Loc(ctx);
        var langs = ctx.nativeLanguage().Select(nl => BuildNativeLang(nl)).ToList();
        return new NativeSectionNode(_file, l, c, langs);
    }

    private NativeLanguageNode BuildNativeLang(JunexisLangParser.NativeLanguageContext ctx)
    {
        var (l, c) = Loc(ctx);
        var fields = ctx.nativeField().Select(nf => BuildNativeField(nf)).ToList();
        return new NativeLanguageNode(_file, l, c, ctx.IDENTIFIER().GetText(), fields);
    }

    private NativeFieldNode BuildNativeField(JunexisLangParser.NativeFieldContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new NativeFieldNode(_file, l, c,
            ctx.nativeFieldName().GetText(),
            ctx.nativeValue().GetText());
    }

    // ────────────────────────────────────────────────────────────────────
    // pipeline
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitPipelineDeclaration(
        [NotNull] JunexisLangParser.PipelineDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var conns  = ctx.pipelineConnection().Select(pc => BuildPipelineConn(pc)).ToList();
        return new PipelineDeclarationNode(_file, l, c, ctx.IDENTIFIER().GetText(), conns);
    }

    private PipelineConnectionNode BuildPipelineConn(JunexisLangParser.PipelineConnectionContext ctx)
    {
        var (l, c) = Loc(ctx);

        if (ctx.PARALLEL() != null)
        {
            var inner = ctx.pipelineConnection().Select(pc => BuildPipelineConn(pc)).ToList();
            return new ParallelBlockNode(_file, l, c, inner);
        }

        // use src -> target
        var src   = ctx.pipelineSource();
        var tgt   = ctx.pipelineTarget();
        var ids   = src.IDENTIFIER();
        var srcTask = ids[0].GetText();
        var srcPort = ids.Length > 1 ? ids[1].GetText() : null;
        string? tgtTask = null, tgtPort = null;
        if (tgt != null)
        {
            var tids  = tgt.IDENTIFIER();
            tgtTask = tids[0].GetText();
            tgtPort = tids.Length > 1 ? tids[1].GetText() : null;
        }
        return new UseConnectionNode(_file, l, c, srcTask, srcPort, tgtTask, tgtPort);
    }

    // ────────────────────────────────────────────────────────────────────
    // device
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitDeviceDeclaration(
        [NotNull] JunexisLangParser.DeviceDeclarationContext ctx)
    {
        var (l, c)   = Loc(ctx);
        var platform = BuildPlatformAnnotation(ctx.platformAnnotation());
        var members  = ctx.deviceMember().Select(m => BuildDeviceMember(m)).ToList();
        return new DeviceDeclarationNode(_file, l, c, ctx.IDENTIFIER().GetText(), platform, members);
    }

    private AstNode BuildDeviceMember(JunexisLangParser.DeviceMemberContext ctx)
    {
        if (ctx.nativeSection() != null) return BuildNativeSection(ctx.nativeSection());
        if (ctx.configBlock()   != null) return BuildConfigBlock(ctx.configBlock());

        // hardware / network / timing — wszystkie jako DeviceBlockNode
        var block = (ctx.hardwareBlock() as ParserRuleContext)
                 ?? (ctx.networkBlock() as ParserRuleContext)
                 ?? (ctx.timingBlock() as ParserRuleContext);
        if (block != null)
        {
            var (l, c) = Loc(block);
            var name   = block.GetChild(0)!.GetText();   // IDENTIFIER
            var fields = ((dynamic)block).deviceField() is IEnumerable<JunexisLangParser.DeviceFieldContext> df
                ? df.Select(f => BuildDeviceField(f)).ToList()
                : (IReadOnlyList<ConfigFieldNode>)[];
            return new DeviceBlockNode(_file, l, c, name, fields);
        }

        throw new InvalidOperationException("Nieznany deviceMember");
    }

    private ConfigFieldNode BuildDeviceField(JunexisLangParser.DeviceFieldContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new ConfigFieldNode(
            _file, l, c,
            ctx.IDENTIFIER().GetText(),
            BuildTypeRef(ctx.typeReference()),
            ctx.literal() != null ? BuildLiteral(ctx.literal()) : null);
    }

    // ────────────────────────────────────────────────────────────────────
    // Opaque declarations
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitNarrativeDeclaration(
        [NotNull] JunexisLangParser.NarrativeDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var label  = ctx.STRING_LITERAL()?.GetText()?.Trim('"') ?? ctx.IDENTIFIER()?.GetText() ?? "";
        return new NarrativeDeclarationNode(_file, l, c, label, OpaqueBody(ctx.narrativeBody()));
    }

    public override AstNode? VisitDeploymentDeclaration(
        [NotNull] JunexisLangParser.DeploymentDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var label  = ctx.STRING_LITERAL()?.GetText()?.Trim('"') ?? ctx.IDENTIFIER()?.GetText();
        return new DeploymentDeclarationNode(_file, l, c, label, OpaqueBody(ctx.deploymentBody()));
    }

    public override AstNode? VisitSupervisorDeclaration(
        [NotNull] JunexisLangParser.SupervisorDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new SupervisorDeclarationNode(_file, l, c, ctx.IDENTIFIER().GetText(), OpaqueBody(ctx.supervisorBody()));
    }

    public override AstNode? VisitQuirksDeclaration(
        [NotNull] JunexisLangParser.QuirksDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var label  = ctx.STRING_LITERAL()?.GetText()?.Trim('"') ?? ctx.IDENTIFIER()?.GetText() ?? "";
        return new QuirksDeclarationNode(_file, l, c, label, OpaqueBody(ctx.quirksBody()));
    }

    public override AstNode? VisitCircuitBreakerDeclaration(
        [NotNull] JunexisLangParser.CircuitBreakerDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new CircuitBreakerDeclarationNode(_file, l, c, ctx.IDENTIFIER().GetText(), OpaqueBody(ctx.circuitBreakerBody()));
    }

    public override AstNode? VisitProtocolDeclaration(
        [NotNull] JunexisLangParser.ProtocolDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new ProtocolDeclarationNode(_file, l, c, ctx.IDENTIFIER().GetText(), OpaqueBody(ctx.protocolBody()));
    }

    // ────────────────────────────────────────────────────────────────────
    // extern (D13)
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitExternDeclaration(
        [NotNull] JunexisLangParser.ExternDeclarationContext ctx)
    {
        var (l, c)    = Loc(ctx);
        var prms      = BuildParamList(ctx.parameterList());
        var platforms = ctx.platformAnnotation().Select(p => BuildPlatformAnnotation(p)).ToList();
        var outType   = ctx.communicationSignature() != null
            ? BuildTypeRef(ctx.communicationSignature().typeReference())
            : null;
        return new ExternTaskNode(_file, l, c, ctx.IDENTIFIER().GetText(), prms, platforms, outType);
    }

    public override AstNode? VisitExternTypeDeclaration(
        [NotNull] JunexisLangParser.ExternTypeDeclarationContext ctx)
    {
        var (l, c)    = Loc(ctx);
        var platforms = ctx.platformAnnotation().Select(p => BuildPlatformAnnotation(p)).ToList();
        return new ExternTypeNode(_file, l, c, ctx.IDENTIFIER().GetText(), platforms);
    }

    public override AstNode? VisitExternContractDeclaration(
        [NotNull] JunexisLangParser.ExternContractDeclarationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var identifiers = ctx.anyIdentifier();
        var role = identifiers[0].GetText();
        var name = identifiers[1].GetText();

        string? target = null;
        string? language = null;
        string? userFile = null;
        var functions = new List<ExternContractFunctionNode>();

        foreach (var member in ctx.externContractBody().externContractMember())
        {
            if (member.externTargetMember() is { } targetMember)
            {
                target = targetMember.platformTarget().GetText();
                continue;
            }

            if (member.externLanguageMember() is { } languageMember)
            {
                language = languageMember.IDENTIFIER(1).GetText();
                continue;
            }

            if (member.externFileMember() is { } fileMember)
            {
                userFile = Unquote(fileMember.STRING_LITERAL().GetText());
                continue;
            }

            if (member.externFunctionMember() is { } fnMember)
            {
                var (fl, fc) = Loc(fnMember);
                functions.Add(new ExternContractFunctionNode(
                    _file,
                    fl,
                    fc,
                    fnMember.IDENTIFIER().GetText(),
                    BuildParamList(fnMember.parameterList()),
                    fnMember.typeReference() is not null ? BuildTypeRef(fnMember.typeReference()) : null));
            }
        }

        return new ExternContractNode(_file, l, c, role, name, target, language, userFile, functions);
    }

    // ────────────────────────────────────────────────────────────────────
    // Statements
    // ────────────────────────────────────────────────────────────────────

    private StmtNode BuildStmt(JunexisLangParser.StatementContext ctx)
    {
        if (ctx.declarationStatement()   != null) return BuildDeclStmt(ctx.declarationStatement());
        if (ctx.returnStatement()        != null) return BuildReturnStmt(ctx.returnStatement());
        if (ctx.delayStatement()         != null) return BuildDelayStmt(ctx.delayStatement());
        if (ctx.logStatement()           != null) return BuildLogStmt(ctx.logStatement());
        if (ctx.communicationStatement() != null) return BuildCommStmt(ctx.communicationStatement());
        if (ctx.controlStatement()       != null) return BuildControlStmt(ctx.controlStatement());
        if (ctx.matchStatement()         != null) return BuildMatchStmt(ctx.matchStatement());
        if (ctx.stateDeclaration()       != null) return WrapInExprStmt(ctx, BuildStateDecl(ctx.stateDeclaration()));
        if (ctx.expressionStatement()    != null)
        {
            var expr = (ExprNode)Visit(ctx.expressionStatement().expression())!;
            var (l, c) = Loc(ctx);
            return new ExprStmtNode(_file, l, c, expr);
        }
        // empty statement (SEMICOLON)
        var (el, ec) = Loc(ctx);
        return new ExprStmtNode(_file, el, ec, new LiteralNode(_file, el, ec, null!, ";"));
    }

    private StmtNode WrapInExprStmt(JunexisLangParser.StatementContext ctx, AstNode node)
    {
        // state declaration jako stmt — zawijamy, ale zwracamy ExprStmt z LiteralNode
        // (state jest obsługiwany jako taskMember, tutaj tylko dla kompletności)
        var (l, c) = Loc(ctx);
        return new ExprStmtNode(_file, l, c, new LiteralNode(_file, l, c, node, node.GetType().Name));
    }

    private LetStmtNode BuildDeclStmt(JunexisLangParser.DeclarationStatementContext ctx)
    {
        var (l, c) = Loc(ctx);
        var kind  = ctx.GetChild(0)!.GetText();   // "let" | "const" | "var"
        var type  = ctx.typeReference() != null ? BuildTypeRef(ctx.typeReference()) : null;
        var init  = (ExprNode)Visit(ctx.expression())!;
        return new LetStmtNode(_file, l, c, kind, ctx.IDENTIFIER().GetText(), type, init);
    }

    private ReturnStmtNode BuildReturnStmt(JunexisLangParser.ReturnStatementContext ctx)
    {
        var (l, c) = Loc(ctx);
        var val  = ctx.expression() != null ? (ExprNode)Visit(ctx.expression())! : null;
        return new ReturnStmtNode(_file, l, c, val);
    }

    private DelayStmtNode BuildDelayStmt(JunexisLangParser.DelayStatementContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new DelayStmtNode(_file, l, c, (ExprNode)Visit(ctx.expression())!);
    }

    private LogStmtNode BuildLogStmt(JunexisLangParser.LogStatementContext ctx)
    {
        var (l, c) = Loc(ctx);
        var args  = ctx.expression().Select(e => (ExprNode)Visit(e)!).ToList();
        return new LogStmtNode(_file, l, c, args);
    }

    private StmtNode BuildCommStmt(JunexisLangParser.CommunicationStatementContext ctx)
    {
        if (ctx.sendStatement() != null)
        {
            var s     = ctx.sendStatement();
            var (l, c) = Loc(s);
            var val   = (ExprNode)Visit(s.expression(0))!;
            var hasExplicitTarget = s.TO() is { Symbol.TokenIndex: >= 0 };
            var tgt   = hasExplicitTarget && s.expression().Length > 1
                ? (ExprNode)Visit(s.expression(1))!
                : null;
            return new SendStmtNode(_file, l, c, val, tgt);
        }
        if (ctx.receiveStatement() != null)
        {
            var r     = ctx.receiveStatement();
            var (l, c) = Loc(r);
            var kind  = r.LET() != null ? "let" : "var";
            var name  = r.IDENTIFIER().GetText();
            var src   = (ExprNode)Visit(r.expression())!;
            // receiveStatement: (LET | VAR) IDENTIFIER ASSIGN RECV FROM? expression
            // Modelujemy jako LetStmt z RecvExpr jako inicjalizatorem.
            return new LetStmtNode(_file, l, c, kind, name, null,
                                   new RecvExprNode(_file, l, c, src));
        }
        if (ctx.ackStatement() != null)
        {
            var ack = ctx.ackStatement();
            var (l, c) = Loc(ack);
            return new AckStmtNode(_file, l, c, (ExprNode)Visit(ack.expression())!);
        }
        if (ctx.nackStatement() != null)
        {
            var nack = ctx.nackStatement();
            var (l, c) = Loc(nack);
            var expressions = nack.expression();
            var message = (ExprNode)Visit(expressions[0])!;
            var reason = expressions.Length > 1 ? (ExprNode)Visit(expressions[1])! : null;
            return new NackStmtNode(_file, l, c, message, reason);
        }
        if (ctx.respondStatement() != null)
        {
            var rs    = ctx.respondStatement();
            var (l, c) = Loc(rs);
            return new RespondStmtNode(_file, l, c, (ExprNode)Visit(rs.expression())!);
        }
        throw new InvalidOperationException("Nieznany communicationStatement");
    }

    private StmtNode BuildControlStmt(JunexisLangParser.ControlStatementContext ctx)
    {
        if (ctx.ifStatement()   != null) return BuildIfStmt(ctx.ifStatement());
        if (ctx.forStatement()  != null) return BuildForStmt(ctx.forStatement());
        if (ctx.everyStatement() != null) return BuildEveryStmt(ctx.everyStatement());
        if (ctx.loopStatement() != null) return BuildLoopStmt(ctx.loopStatement());
        throw new InvalidOperationException("Nieznany controlStatement");
    }

    private IfStmtNode BuildIfStmt(JunexisLangParser.IfStatementContext ctx)
    {
        var (l, c)      = Loc(ctx);
        var cond        = (ExprNode)Visit(ctx.expression())!;
        var elseToken   = ctx.ELSE();
        var allStmts    = ctx.statement();

        if (elseToken == null)
        {
            // Brak else — wszystkie stmts należą do then
            return new IfStmtNode(_file, l, c, cond,
                allStmts.Select(s => BuildStmt(s)).ToList(), null);
        }

        // Dzielimy stmts na then/else po pozycji tokenu ELSE w strumieniu tokenów
        var elsePos  = elseToken.Symbol.TokenIndex;
        var thenList = allStmts
            .Where(s => s.Start.TokenIndex < elsePos)
            .Select(s => BuildStmt(s))
            .ToList();

        AstNode els;
        if (ctx.ifStatement() != null)
        {
            els = BuildIfStmt(ctx.ifStatement());
        }
        else
        {
            var (el, ec) = Loc(elseToken);
            var elseList = allStmts
                .Where(s => s.Start.TokenIndex > elsePos)
                .Select(s => BuildStmt(s))
                .ToList();
            els = new ElseBlockNode(_file, el, ec, elseList);
        }

        return new IfStmtNode(_file, l, c, cond, thenList, els);
    }

    private ForStmtNode BuildForStmt(JunexisLangParser.ForStatementContext ctx)
    {
        var (l, c) = Loc(ctx);
        var body  = ctx.statement().Select(s => BuildStmt(s)).ToList();
        return new ForStmtNode(_file, l, c, ctx.anyIdentifier().GetText(), (ExprNode)Visit(ctx.expression())!, body);
    }

    private EveryStmtNode BuildEveryStmt(JunexisLangParser.EveryStatementContext ctx)
    {
        var (l, c) = Loc(ctx);
        var body  = ctx.statement().Select(s => BuildStmt(s)).ToList();
        return new EveryStmtNode(_file, l, c, (ExprNode)Visit(ctx.expression())!, body);
    }

    private LoopStmtNode BuildLoopStmt(JunexisLangParser.LoopStatementContext ctx)
    {
        var (l, c) = Loc(ctx);
        var body  = ctx.statement().Select(s => BuildStmt(s)).ToList();
        return new LoopStmtNode(_file, l, c, body);
    }

    private MatchStmtNode BuildMatchStmt(JunexisLangParser.MatchStatementContext ctx)
    {
        var (l, c) = Loc(ctx);
        var subj  = (ExprNode)Visit(ctx.expression())!;
        var arms  = ctx.matchArm().Select(a => BuildMatchArm(a)).ToList();
        return new MatchStmtNode(_file, l, c, subj, arms);
    }

    private MatchArmNode BuildMatchArm(JunexisLangParser.MatchArmContext ctx)
    {
        var (l, c)   = Loc(ctx);
        var pattern  = BuildMatchPattern(ctx.matchPattern());
        List<StmtNode> bodyStmts = [];
        ExprNode?      bodyExpr  = null;

        if (ctx.expression() != null)
            bodyExpr = (ExprNode)Visit(ctx.expression())!;
        else
            bodyStmts = ctx.statement().Select(s => BuildStmt(s)).ToList();

        return new MatchArmNode(_file, l, c, pattern, bodyStmts, bodyExpr);
    }

    private MatchPatternNode BuildMatchPattern(JunexisLangParser.MatchPatternContext ctx)
    {
        var (l, c) = Loc(ctx);

        if (ctx.UNDERSCORE() != null)
            return new WildcardPatternNode(_file, l, c);

        if (ctx.literal() != null)
            return new LiteralPatternNode(_file, l, c, BuildLiteral(ctx.literal()));

        var id = ctx.IDENTIFIER();
        if (id == null)
        {
            // anyIdentifier (binding) — brak IDENTIFIER, ale jest anyIdentifier
            return new BindingPatternNode(_file, l, c, ctx.GetText());
        }

        if (ctx.DOUBLE_COLON() != null)
        {
            var typeName = id.GetText();
            var variant  = ctx.anyIdentifier()!.GetText();
            var nested   = ctx.matchPattern().Select(mp => BuildMatchPattern(mp)).ToList();
            return new EnumVariantPatternNode(_file, l, c, typeName, variant, nested);
        }

        // prosty binding: IDENTIFIER
        return new BindingPatternNode(_file, l, c, id.GetText());
    }

    // ────────────────────────────────────────────────────────────────────
    // Expressions
    // ────────────────────────────────────────────────────────────────────

    public override AstNode? VisitAssignmentExpression(
        [NotNull] JunexisLangParser.AssignmentExpressionContext ctx)
    {
        if (ctx.compoundOperator() == null)
            return Visit(ctx.conditionalExpression(0));

        var (l, c) = Loc(ctx);
        var left  = (ExprNode)Visit(ctx.conditionalExpression(0))!;
        var right = (ExprNode)Visit(ctx.conditionalExpression(1))!;
        return new BinaryExprNode(_file, l, c, ctx.compoundOperator().GetText(), left, right);
    }

    public override AstNode? VisitConditionalExpression(
        [NotNull] JunexisLangParser.ConditionalExpressionContext ctx)
    {
        if (ctx.QUESTION() == null)
            return Visit(ctx.logicalOrExpression());

        var (l, c)   = Loc(ctx);
        var cond     = (ExprNode)Visit(ctx.logicalOrExpression())!;
        var then     = (ExprNode)Visit(ctx.expression())!;
        var els      = (ExprNode)Visit(ctx.conditionalExpression())!;
        // trójnik: cond ? then : els — modelujemy jako BinaryExpr z op "?:"
        return new BinaryExprNode(_file, l, c, "?:",
            cond,
            new BinaryExprNode(_file, l, c, ":", then, els));
    }

    public override AstNode? VisitLogicalOrExpression(
        [NotNull] JunexisLangParser.LogicalOrExpressionContext ctx)
        => BuildLeftAssoc(ctx.children!, ctx.logicalAndExpression);

    public override AstNode? VisitLogicalAndExpression(
        [NotNull] JunexisLangParser.LogicalAndExpressionContext ctx)
        => BuildLeftAssoc(ctx.children!, ctx.equalityExpression);

    public override AstNode? VisitEqualityExpression(
        [NotNull] JunexisLangParser.EqualityExpressionContext ctx)
        => BuildLeftAssoc(ctx.children!, ctx.relationalExpression);

    public override AstNode? VisitRelationalExpression(
        [NotNull] JunexisLangParser.RelationalExpressionContext ctx)
        => BuildLeftAssoc(ctx.children!, ctx.additiveExpression);

    public override AstNode? VisitAdditiveExpression(
        [NotNull] JunexisLangParser.AdditiveExpressionContext ctx)
        => BuildLeftAssoc(ctx.children!, ctx.multiplicativeExpression);

    public override AstNode? VisitMultiplicativeExpression(
        [NotNull] JunexisLangParser.MultiplicativeExpressionContext ctx)
        => BuildLeftAssoc(ctx.children!, ctx.castExpression);

    public override AstNode? VisitCastExpression(
        [NotNull] JunexisLangParser.CastExpressionContext ctx)
    {
        var inner = Visit(ctx.unaryExpression())!;
        if (ctx.AS() == null) return inner;
        var (l, c) = Loc(ctx);
        var targetType = BuildTypeRef(ctx.typeReference());
        return new BinaryExprNode(_file, l, c, "as", (ExprNode)inner, new IdentifierExprNode(_file, l, c, FormatTypeRef(targetType)));
    }

    public override AstNode? VisitUnaryExpression(
        [NotNull] JunexisLangParser.UnaryExpressionContext ctx)
    {
        if (ctx.postfixExpression() != null)
            return Visit(ctx.postfixExpression());

        var (l, c) = Loc(ctx);
        var op     = NormalizeOperator(ctx.GetChild(0)!.GetText());
        return new UnaryExprNode(_file, l, c, op, (ExprNode)Visit(ctx.unaryExpression())!);
    }

    public override AstNode? VisitPostfixExpression(
        [NotNull] JunexisLangParser.PostfixExpressionContext ctx)
    {
        var expr = (ExprNode)Visit(ctx.primaryExpression())!;
        var (l, c) = Loc(ctx);

        // iterujemy po postfix-suffix'ach: [i], .field, ::Variant, (args)
        int i = 0;
        int argListUsed = 0;
        i = 1;
        while (i < ctx.ChildCount)
        {
            var child = ctx.GetChild(i);
            var tok   = child.GetText();
            if (tok == "[")
            {
                var idx = (ExprNode)Visit(ctx.expression(GetExprIndex(ctx, i)))!;
                expr = new IndexExprNode(_file, l, c, expr, idx);
                i += 3; // [ expr ]
            }
            else if (tok == "." && i + 1 < ctx.ChildCount)
            {
                var field = ctx.GetChild(i + 1)!.GetText();
                if (field == "::")
                {
                    // ::Variant
                    var variant = ctx.GetChild(i + 2)!.GetText();
                    expr = new EnumAccessNode(_file, l, c, ((IdentifierExprNode)expr).Name, variant);
                    i += 3;
                }
                else
                {
                    expr = new FieldAccessNode(_file, l, c, expr, field);
                    i += 2;
                }
            }
            else if (tok == "::")
            {
                var variant = ctx.GetChild(i + 1)!.GetText();
                expr = new EnumAccessNode(_file, l, c, ((IdentifierExprNode)expr).Name, variant);
                i += 2;
            }
            else if (tok == "(")
            {
                var allArgLists = ctx.argumentList();
                var argList     = argListUsed < allArgLists.Length ? allArgLists[argListUsed] : null;
                argListUsed++;
                var args = BuildArgList(argList);
                expr = new CallExprNode(_file, l, c, expr, args);
                i += argList != null ? 3 : 2; // ( [argList] )
            }
            else
                i++;
        }
        return expr;
    }

    public override AstNode? VisitPrimaryExpression(
        [NotNull] JunexisLangParser.PrimaryExpressionContext ctx)
    {
        var (l, c) = Loc(ctx);

        if (ctx.literal()                != null) return BuildLiteral(ctx.literal());
        if (ctx.anyIdentifier()           != null) return new IdentifierExprNode(_file, l, c, ctx.anyIdentifier().GetText());
        if (ctx.CONFIG()                 != null) return new ConfigAccessNode(_file, l, c, null);
        if (ctx.LPAREN()                 != null) return new ParenExprNode(_file, l, c, (ExprNode)Visit(ctx.expression())!);
        if (ctx.structLiteral()          != null) return VisitStructLiteral(ctx.structLiteral());
        if (ctx.arrayLiteral()           != null) return VisitArrayLiteral(ctx.arrayLiteral());
        if (ctx.jsonLiteral()            != null) return VisitJsonLiteral(ctx.jsonLiteral());
        if (ctx.communicationExpression() != null) return VisitCommunicationExpression(ctx.communicationExpression());

        return new IdentifierExprNode(_file, l, c, ctx.GetText());
    }

    public override AstNode? VisitCommunicationExpression(
        [NotNull] JunexisLangParser.CommunicationExpressionContext ctx)
    {
        var (l, c) = Loc(ctx);
        if (ctx.RECV() != null)
        {
            var src = (ExprNode)Visit(ctx.expression(0))!;
            return new RecvExprNode(_file, l, c, src);
        }
        // SEND expr TO? expr
        var val = (ExprNode)Visit(ctx.expression(0))!;
        var tgt = ctx.expression().Length > 1 ? (ExprNode)Visit(ctx.expression(1))! : null;
        return new SendExprNode(_file, l, c, val, tgt);
    }

    public override AstNode? VisitStructLiteral(
        [NotNull] JunexisLangParser.StructLiteralContext ctx)
    {
        var (l, c) = Loc(ctx);
        var fields = ctx.structFieldInit().Select(f => BuildStructFieldInit(f)).ToList();
        return new StructLiteralNode(_file, l, c, ctx.IDENTIFIER().GetText(), fields);
    }

    private StructFieldInitNode BuildStructFieldInit(JunexisLangParser.StructFieldInitContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new StructFieldInitNode(_file, l, c, ctx.IDENTIFIER().GetText(), (ExprNode)Visit(ctx.expression())!);
    }

    public override AstNode? VisitArrayLiteral(
        [NotNull] JunexisLangParser.ArrayLiteralContext ctx)
    {
        var (l, c)  = Loc(ctx);
        var elems = ctx.expression().Select(e => (ExprNode)Visit(e)!).ToList();
        return new ArrayLiteralNode(_file, l, c, elems);
    }

    public override AstNode? VisitJsonLiteral(
        [NotNull] JunexisLangParser.JsonLiteralContext ctx)
    {
        var (l, c) = Loc(ctx);
        var fields = ctx.jsonField().Select(f => BuildJsonField(f)).ToList();
        return new JsonLiteralNode(_file, l, c, fields);
    }

    private JsonFieldNode BuildJsonField(JunexisLangParser.JsonFieldContext ctx)
    {
        var (l, c) = Loc(ctx);
        var key   = ctx.STRING_LITERAL().GetText().Trim('"');
        return new JsonFieldNode(_file, l, c, key, (ExprNode)Visit(ctx.expression())!);
    }

    // ────────────────────────────────────────────────────────────────────
    // Types
    // ────────────────────────────────────────────────────────────────────

    private TypeRefNode BuildTypeRef(JunexisLangParser.TypeReferenceContext ctx)
    {
        var atoms = ctx.atomicType();
        if (atoms.Length == 1)
            return BuildAtomicType(atoms[0]);

        var (l, c) = Loc(ctx);
        var parts = atoms.Select(a => BuildAtomicType(a)).ToList();
        return new UnionTypeNode(_file, l, c, parts);
    }

    private TypeRefNode BuildAtomicType(JunexisLangParser.AtomicTypeContext ctx)
    {
        if (ctx.communicationType() != null) return BuildCommType(ctx.communicationType());
        if (ctx.mapType()           != null) return BuildMapType(ctx.mapType());
        if (ctx.genericType()       != null) return BuildGenericType(ctx.genericType());
        if (ctx.arrayType()         != null) return BuildArrayType(ctx.arrayType());
        return BuildBaseType(ctx.baseType()!);
    }

    private CommTypeNode BuildCommType(JunexisLangParser.CommunicationTypeContext ctx)
    {
        var (l, c) = Loc(ctx);
        var kind   = ctx.CHAN() != null ? CommKind.Chan
                   : ctx.QUEUE() != null ? CommKind.Queue
                   : CommKind.Stream;
        return new CommTypeNode(_file, l, c, kind, BuildTypeRef(ctx.typeReference()));
    }

    private MapTypeNode BuildMapType(JunexisLangParser.MapTypeContext ctx)
    {
        var (l, c) = Loc(ctx);
        var refs   = ctx.typeReference();
        return new MapTypeNode(_file, l, c, BuildTypeRef(refs[0]), BuildTypeRef(refs[1]));
    }

    private GenericTypeNode BuildGenericType(JunexisLangParser.GenericTypeContext ctx)
    {
        var (l, c)   = Loc(ctx);
        var typeArgs = ctx.typeReference().Select(t => BuildTypeRef(t)).ToList();
        return new GenericTypeNode(_file, l, c, ctx.IDENTIFIER().GetText(), typeArgs);
    }

    private ArrayTypeNode BuildArrayType(JunexisLangParser.ArrayTypeContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new ArrayTypeNode(_file, l, c, BuildTypeRef(ctx.typeReference()));
    }

    private static string FormatTypeRef(TypeRefNode typeRef) => typeRef switch
    {
        NamedTypeNode namedType => namedType.Name,
        CommTypeNode commType => $"{commType.Kind.ToString().ToLowerInvariant()}<{FormatTypeRef(commType.Element)}>",
        MapTypeNode mapType => $"Map<{FormatTypeRef(mapType.Key)},{FormatTypeRef(mapType.Value)}>",
        GenericTypeNode genericType => $"{genericType.Name}<{string.Join(",", genericType.TypeArgs.Select(FormatTypeRef))}>",
        ArrayTypeNode arrayType => $"[{FormatTypeRef(arrayType.Element)}]",
        UnionTypeNode unionType => string.Join("|", unionType.Variants.Select(FormatTypeRef)),
        _ => typeRef.ToString() ?? string.Empty,
    };

    private NamedTypeNode BuildBaseType(JunexisLangParser.BaseTypeContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new NamedTypeNode(_file, l, c, ctx.GetText());
    }

    // ────────────────────────────────────────────────────────────────────
    // Platform annotation
    // ────────────────────────────────────────────────────────────────────

    private PlatformAnnotation BuildPlatformAnnotation(JunexisLangParser.PlatformAnnotationContext ctx)
    {
        var (l, c) = Loc(ctx);
        var target = ctx.platformTarget().GetText();
        var args   = ctx.platformArgument().Select(a => BuildPlatformArg(a)).ToList();
        return new PlatformAnnotation(_file, l, c, target, args);
    }

    private PlatformArgNode BuildPlatformArg(JunexisLangParser.PlatformArgumentContext ctx)
    {
        var (l, c) = Loc(ctx);
        string? key = null;
        ExprNode val;

        if (ctx.ASSIGN() != null)
        {
            key = ctx.anyIdentifier()!.GetText();
            val = BuildPlatformValue(ctx.platformValue());
        }
        else
        {
            val = BuildPlatformValue(ctx.platformValue());
        }
        return new PlatformArgNode(_file, l, c, key, val);
    }

    private ExprNode BuildPlatformValue(JunexisLangParser.PlatformValueContext ctx)
    {
        var (l, c) = Loc(ctx);
        if (ctx.literal()    != null) return BuildLiteral(ctx.literal());
        return new IdentifierExprNode(_file, l, c, ctx.anyIdentifier().GetText());
    }

    // ────────────────────────────────────────────────────────────────────
    // Parameters
    // ────────────────────────────────────────────────────────────────────

    private IReadOnlyList<ParamNode> BuildParamList(JunexisLangParser.ParameterListContext? ctx)
    {
        if (ctx == null) return [];
        return ctx.parameter().Select(p => BuildParam(p)).ToList();
    }

    private ParamNode BuildParam(JunexisLangParser.ParameterContext ctx)
    {
        var (l, c) = Loc(ctx);
        return new ParamNode(
            _file, l, c,
            ctx.anyIdentifier().GetText(),
            BuildTypeRef(ctx.typeReference()),
            ctx.expression() != null ? (ExprNode)Visit(ctx.expression())! : null);
    }

    // ────────────────────────────────────────────────────────────────────
    // Argument list (call)
    // ────────────────────────────────────────────────────────────────────

    private IReadOnlyList<CallArgNode> BuildArgList(JunexisLangParser.ArgumentListContext? ctx)
    {
        if (ctx == null) return [];
        var (l, c) = Loc(ctx);

        // named arguments: IDENTIFIER COLON expression
        if (ctx.namedArgument() is { Length: > 0 } named)
            return named.Select(na =>
            {
                var (nl, nc) = Loc(na);
                return new CallArgNode(_file, nl, nc, na.IDENTIFIER().GetText(), (ExprNode)Visit(na.expression())!);
            }).ToList();

        // positional
        return ctx.expression().Select(e =>
            new CallArgNode(_file, l, c, null, (ExprNode)Visit(e)!)).ToList();
    }

    // ────────────────────────────────────────────────────────────────────
    // Literal
    // ────────────────────────────────────────────────────────────────────

    private LiteralNode BuildLiteral(JunexisLangParser.LiteralContext ctx)
    {
        var (l, c) = Loc(ctx);
        var raw = ctx.GetText();

        object value = raw switch
        {
            _  when ctx.TRUE()             != null => true,
            _  when ctx.FALSE()            != null => false,
            _  when ctx.HEX_LITERAL()      != null => Convert.ToInt64(raw, 16),
            _  when ctx.INTEGER_LITERAL()  != null => long.TryParse(raw, out var iv) ? iv : (object)raw,
            _  when ctx.FLOAT_LITERAL()    != null => double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                                         System.Globalization.CultureInfo.InvariantCulture, out var dv) ? dv : (object)raw,
            _  when ctx.STRING_LITERAL()   != null => raw.Trim('"'),
            _  when ctx.RAW_STRING()       != null => raw.TrimStart('@').Trim('"'),
            _  when ctx.TRIPLE_STRING()    != null => raw.Trim('"'),
            _                                      => raw
        };

        return new LiteralNode(_file, l, c, value, raw);
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private static (int line, int col) Loc(ParserRuleContext ctx)
        => (ctx.Start.Line, ctx.Start.Column + 1);

    private static (int line, int col) Loc(IParseTree ctx)
    {
        if (ctx is ParserRuleContext prc) return Loc(prc);
        if (ctx is ITerminalNode tn)      return (tn.Symbol.Line, tn.Symbol.Column + 1);
        return (0, 0);
    }

    /// <summary>Zbiera surowy tekst balanced-braces (bez { i }).</summary>
    private string OpaqueBody(ParserRuleContext ctx)
        => _tokens.GetText(
               new Interval(ctx.Start.TokenIndex + 1, ctx.Stop.TokenIndex - 1))
               .Trim();

    private static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;

    /// <summary>Buduje lewostronnie asocjatywne wyrażenia binarne z listy dzieci.</summary>
    private ExprNode? BuildLeftAssoc<TChild>(
        IList<IParseTree>         children,
        Func<int, TChild>         sub)
        where TChild : ParserRuleContext
    {
        // Zbieramy tylko reguły sub (nie tokeny operatorów)
        var ops    = new List<string>();
        var subCtxList = new List<TChild>();

        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is TChild sc)
                subCtxList.Add(sc);
            else if (children[i] is ITerminalNode tn)
                ops.Add(tn.GetText());
        }

        if (subCtxList.Count == 0) return null;
        var result = (ExprNode?)Visit(subCtxList[0]);

        for (int i = 0; i < ops.Count; i++)
        {
            var (l, c) = Loc(subCtxList[i]);
            result = new BinaryExprNode(_file, l, c, NormalizeOperator(ops[i]), result!, (ExprNode)Visit(subCtxList[i + 1])!);
        }
        return result;
    }

    private static string NormalizeOperator(string op) => op switch
    {
        "||" => "or",
        "&&" => "and",
        "!"  => "not",
        _    => op
    };

    // Wyznacza który expression() w postfix odpowiada danemu tokenowi [
    // (ANTLR4 numeruje expression() od 0 w kolejności pojawiania się)
    private static int GetExprIndex(JunexisLangParser.PostfixExpressionContext ctx, int tokenPos)
    {
        // Niestandardowe — używamy liczby expression-tokenów do określenia indeksu
        int count = 0;
        for (int i = 1; i < tokenPos; i++)
            if (ctx.GetChild(i) is JunexisLangParser.ExpressionContext)
                count++;
        return count;
    }
}
