using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.Core.Runtime;
using JunexisLang.Compiler.LspHost.Analysis;
using System.Text.RegularExpressions;

namespace JunexisLang.Compiler.LspHost.Handlers;

/// <summary>Wyświetla sygnaturę symbolu pod kursorem jako Markdown.</summary>
internal sealed class HoverHandler(DocumentStore store) : HoverHandlerBase
{
    private static readonly IReadOnlyDictionary<string, string> KeywordDocs =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["task"] = "Defines what the system does: an executable unit that can be assigned to a platform such as `@embedded`, `@server`, or `@report`.",
            ["struct"] = "Declares a structural data type shared by tasks, protocols, generated artifacts, and extern contracts.",
            ["enum"] = "Declares a closed set of named variants, optionally carrying payload values.",
            ["fn"] = "Declares deterministic transformation logic callable from Junexis code.",
            ["pipeline"] = "Describes the direction of data flow between tasks.",
            ["device"] = "Describes the character and capabilities of hardware that an embedded task can run on.",
            ["narrative"] = "Captures intent, observability, and explanation around the system flow.",
            ["deployment"] = "Describes where concrete task instances run.",
            ["supervisor"] = "Declares supervision policy for runtime resilience.",
            ["circuit_breaker"] = "Declares a failure boundary that can stop or degrade unsafe communication.",
            ["quirks"] = "Describes known platform or device-specific behavior that the compiler and developer should keep visible.",
            ["extern"] = "Declares a developer-owned boundary implemented outside generated Junexis code.",
            ["protocol"] = "Describes how data crosses platform or transport boundaries.",
            ["import"] = "Imports declarations from another Junexis module.",
            ["let"] = "Declares a local immutable binding.",
            ["const"] = "Declares a constant binding.",
            ["var"] = "Declares a mutable local binding.",
            ["state"] = "Declares persistent task-local state.",
            ["config"] = "Declares configuration values for a task or device.",
            ["native"] = "Marks target-language-specific implementation details.",
            ["use"] = "References a task or component inside a pipeline or composition block.",
            ["return"] = "Returns a value from a function or task.",
            ["if"] = "Starts a conditional branch.",
            ["else"] = "Starts the alternative branch of a condition.",
            ["for"] = "Starts iteration over a value or collection.",
            ["in"] = "Connects an iteration variable with the iterated value.",
            ["match"] = "Starts pattern matching over a value.",
            ["every"] = "Declares periodic execution.",
            ["loop"] = "Declares repeated or long-running execution.",
            ["delay"] = "Suspends execution for a duration.",
            ["log"] = "Emits an observable log/narrative event.",
            ["as"] = "Introduces an alias or named binding in supported contexts.",
            ["on"] = "Introduces lifecycle or event handling logic.",
            ["send"] = "Sends a value to a channel, queue, stream, or downstream task.",
            ["recv"] = "Receives a value from a communication primitive.",
            ["respond"] = "Sends a response from request/response-style logic.",
            ["to"] = "Marks a destination in flow or communication syntax.",
            ["from"] = "Marks a source in flow or communication syntax.",
            ["broadcast"] = "Sends a value to multiple observers or consumers.",
            ["parallel"] = "Marks a branch that may execute concurrently.",
            ["and"] = "Boolean conjunction. Equivalent to `&&`.",
            ["or"] = "Boolean disjunction. Equivalent to `||`.",
            ["not"] = "Boolean negation. Equivalent to `!`.",
            ["true"] = "Boolean literal value.",
            ["false"] = "Boolean literal value.",
            ["embedded"] = "Platform target for native/AOT or device-oriented code. Usually written as `@embedded` on tasks.",
            ["server"] = "Platform target for server-side services and orchestration. Usually written as `@server` on tasks.",
            ["report"] = "Platform target for reporting/UI artifacts. Usually written as `@report` on tasks.",
            ["storage"] = "Declares a logical, provider-neutral data model containing entities and typed named queries.",
            ["entity"] = "Declares a logical record shape inside a storage model.",
            ["query"] = "Declares a typed, provider-neutral read operation inside a storage model.",
            ["filter"] = "Restricts storage query rows with a typed predicate.",
            ["project"] = "Selects and optionally renames the result fields of a storage query.",
            ["sort"] = "Defines deterministic ordering in a storage query.",
            ["limit"] = "Bounds the number of rows returned by a storage query.",
            ["offset"] = "Skips a bounded number of rows before returning a storage query result.",
            ["key"] = "Declares the logical primary key fields of a storage entity.",
            ["mutation"] = "Declares a typed, provider-neutral storage write operation.",
            ["insert"] = "Creates one logical entity record; requires the explicit `single` scope.",
            ["update"] = "Changes declared fields of records selected by a required `where` predicate.",
            ["delete"] = "Removes records selected by a required `where` predicate.",
            ["single"] = "Requires the mutation predicate to bind every primary-key field.",
            ["many"] = "Explicitly permits a mutation to affect multiple records.",
            ["set"] = "Assigns a mutation parameter or literal to a logical entity field.",
            ["where"] = "Defines the typed record-selection predicate for update or delete.",
            ["motion"] = "Extern role for motion-control boundaries, such as CNC or robotics drivers.",
        };

    private static readonly IReadOnlyDictionary<string, string> BuiltInTypeDocs =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Int"] = "Built-in integer numeric type.",
            ["Float"] = "Built-in floating-point numeric type.",
            ["Bool"] = "Built-in boolean type.",
            ["Text"] = "Built-in text/string type.",
            ["Time"] = "Built-in temporal value type.",
            ["Symbol"] = "Built-in symbolic identifier type.",
            ["Duration"] = "Built-in duration type for intervals such as `5s` or `5m`.",
            ["Null"] = "Built-in absence/null value type.",
            ["Map"] = "Built-in associative collection type: `Map<Key, Value>`.",
            ["List"] = "Built-in ordered collection type: `List<T>`.",
            ["Option"] = "Built-in optional value type: `Option<T>`.",
            ["Result"] = "Built-in success-or-error value type: `Result<T, E>`.",
            ["Bytes"] = "Portable storage byte-sequence type.",
            ["Decimal"] = "Portable exact decimal storage type.",
            ["Uuid"] = "Portable UUID storage identifier type.",
            ["Json"] = "Portable structured JSON storage value type.",
            ["chan"] = CommunicationTypeDoc(
                "Point-to-point communication channel type: `chan<T>`.",
                delivery: "Backpressured",
                backpressure: "ProducerWaits",
                retention: "UntilConsumed",
                ordering: "Fifo"),
            ["queue"] = CommunicationTypeDoc(
                "Durable or buffered communication queue type: `queue<T>`.",
                delivery: "AtLeastOnce",
                backpressure: "ProducerContinues",
                retention: "UntilAck",
                ordering: "Fifo"),
            ["stream"] = CommunicationTypeDoc(
                "Continuous or observable data stream type: `stream<T>`.",
                delivery: "LatestWins",
                backpressure: "ProducerContinues",
                retention: "LatestOnly",
                ordering: "LatestOnly"),
            ["u8"] = "Unsigned 8-bit integer type.",
            ["u16"] = "Unsigned 16-bit integer type.",
            ["u32"] = "Unsigned 32-bit integer type.",
            ["u64"] = "Unsigned 64-bit integer type.",
            ["i8"] = "Signed 8-bit integer type.",
            ["i16"] = "Signed 16-bit integer type.",
            ["i32"] = "Signed 32-bit integer type.",
            ["i64"] = "Signed 64-bit integer type.",
            ["f32"] = "32-bit floating-point numeric type.",
            ["f64"] = "64-bit floating-point numeric type.",
        };

    private static readonly IReadOnlyDictionary<string, string> RuntimePrimitiveDocs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ack"] = """
                      Queue acknowledgement primitive.

                      Signature:

                      ```junexis
                      ack message
                      ```

                      Status: `proposed`.

                      `message` must be a value received from `queue<T>`. ACK confirms processing and lets the queue remove the message. Go/TypeScript lowering exists; embedded is not supported in 0.2.
                      """,
            ["nack"] = """
                       Queue negative acknowledgement primitive.

                       Signature:

                       ```junexis
                       nack message reason text
                       ```

                       Status: `proposed`.

                       `message` must be a value received from `queue<T>`. NACK keeps runtime delivery context for retry/DLQ policy. Go/TypeScript lowering exists; embedded is not supported in 0.2.
                       """,
            ["latest"] = """
                         Stream latest-value primitive.

                         Signature:

                         ```junexis
                         latest(stream<T>) -> T
                         ```

                         Status: `proposed`.

                         Reads the most recent value from `stream<T>` without ACK and without consuming history. Go/TypeScript lowering exists; empty-stream semantics are still experimental.
                         """,
            ["batch"] = """
                        Queue batch snapshot primitive.

                        Signature:

                        ```junexis
                        batch(queue<T>, Int) -> List<T>
                        ```

                        Status: `proposed`.

                        Returns a snapshot of queued payloads without ACK/NACK. `batch(stream<T>, ...)` is intentionally not supported because stream is latest-wins, not historical.
                        """
        };

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability _c, ClientCapabilities _caps) =>
        new() { DocumentSelector = TextDocumentSelector.ForLanguage("junexis") };

    public override Task<Hover?> Handle(HoverParams request, CancellationToken _)
    {
        var (source, ast, _) = store.Get(request.TextDocument.Uri.ToString());
        if (source is null) return Task.FromResult<Hover?>(null);

        var word = WordAtPosition.Get(source, request.Position.Line, request.Position.Character);
        if (word is null) return Task.FromResult<Hover?>(null);

        var storageQuery = store.FindStorageQuerySymbol(
            request.TextDocument.Uri.ToString(), word.Value.Word);
        var markdown = storageQuery?.Markdown ?? (ast is not null
            ? SymbolIndex.Build(ast).Find(word.Value.Word) is { } node
                ? Format(node)
                : FormatKnownWord(word.Value.Word, DisplayWord(source, request.Position.Line, word.Value))
                    ?? FormatLocalWord(source, ast, request.Position.Line, request.Position.Character, word.Value)
            : FormatKnownWord(word.Value.Word, DisplayWord(source, request.Position.Line, word.Value)));

        if (markdown is null) return Task.FromResult<Hover?>(null);

        var hoverRange = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            new Position(request.Position.Line, word.Value.StartChar),
            new Position(request.Position.Line, word.Value.EndChar));

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind  = MarkupKind.Markdown,
                Value = markdown
            }),
            Range = hoverRange
        });
    }

    // ── Formatowanie ──────────────────────────────────────────────────────

    private static string Format(AstNode n) => n switch
    {
        TaskDeclarationNode t           => FormatTask(t),
        StructDeclarationNode s         => FormatStruct(s),
        EnumDeclarationNode e           => FormatEnum(e),
        FnDeclarationNode f             => FormatFn(f),
        PipelineDeclarationNode p       => $"```junexis\npipeline {p.Name}\n```",
        DeviceDeclarationNode d         => $"```junexis\ndevice {d.Name}\n```",
        ExternTaskNode et               => FormatExternTask(et),
        ExternContractNode ec           => FormatExternContract(ec),
        SupervisorDeclarationNode sv    => $"```junexis\nsupervisor {sv.Name}\n```",
        CircuitBreakerDeclarationNode cb => $"```junexis\ncircuit_breaker {cb.Name}\n```",
        StructFieldNode sf              => $"```junexis\n{sf.Name}: {FT(sf.Type)}\n```",
        EnumVariantNode ev              => FormatEnumVariant(ev),
        ParamNode pm                    => $"```junexis\n{pm.Name}: {FT(pm.Type)}\n```",
        _                               => $"*symbol*"
    };

    private static string? FormatKnownWord(string word, string displayWord)
    {
        if (RuntimePrimitiveDocs.TryGetValue(word, out var primitiveDoc) &&
            RuntimePrimitiveRegistry.TryGet(word, out var primitive))
        {
            return $"""
                   ```junexis
                   {displayWord}
                   ```

                   {primitiveDoc}

                   Runtime registry:

                   - status: `{primitive.Status.ToString().ToLowerInvariant()}`
                   - targets: `{primitive.Targets}`
                   - implementations: `{primitive.Implementations}`
                   """;
        }

        if (BuiltInTypeDocs.TryGetValue(word, out var typeDoc))
        {
            return $"```junexis\n{displayWord}\n```\n\n{typeDoc}";
        }

        if (KeywordDocs.TryGetValue(word, out var keywordDoc))
        {
            return $"```junexis\n{displayWord}\n```\n\n{keywordDoc}";
        }

        return null;
    }

    private static string CommunicationTypeDoc(
        string summary,
        string delivery,
        string backpressure,
        string retention,
        string ordering)
        => $"""
           {summary}

           Runtime contract:

           - delivery: `{delivery}`
           - backpressure: `{backpressure}`
           - retention: `{retention}`
           - ordering: `{ordering}`
           """;

    private static string? FormatLocalWord(
        string source,
        ProgramNode ast,
        long line,
        long character,
        WordAtPosition.WordInfo word)
    {
        var oneBasedLine = (int)line + 1;
        if (FindVisibleLocalBinding(ast, word.Word, oneBasedLine) is { } localBinding)
        {
            return localBinding.Type is null
                ? $"```junexis\n{word.Word}\n```\n\nLocal `{localBinding.Kind}` binding."
                : $"```junexis\n{word.Word}: {localBinding.Type}\n```\n\nLocal `{localBinding.Kind}` binding.";
        }

        var binding = FindEnclosingForBinding(source, line, character);
        if (binding is null || binding.Value.Var != word.Word)
        {
            return null;
        }

        var inferredType = InferForElementType(ast, binding.Value.Iter, oneBasedLine);
        if (inferredType is null)
        {
            return $"```junexis\n{word.Word}\n```\n\nLoop variable declared by `for {word.Word} in {binding.Value.Iter}`.";
        }

        return $"```junexis\n{word.Word}: {inferredType}\n```\n\nLoop variable inferred from `{binding.Value.Iter}`.";
    }

    private readonly record struct LocalBinding(string Kind, string? Type);

    private static LocalBinding? FindVisibleLocalBinding(ProgramNode ast, string name, int oneBasedLine)
    {
        var task = ast.Declarations
            .OfType<TaskDeclarationNode>()
            .Where(t => t.Line <= oneBasedLine)
            .OrderByDescending(t => t.Line)
            .FirstOrDefault();

        if (task is null) return null;

        var locals = new List<LocalBindingCandidate>();
        CollectLocalBindings(ast, task.Body, oneBasedLine, locals);

        var candidate = locals
            .Where(local => local.Name == name)
            .OrderByDescending(local => local.Line)
            .FirstOrDefault();

        return candidate is null ? null : new LocalBinding(candidate.Kind, candidate.Type);
    }

    private sealed record LocalBindingCandidate(string Name, string Kind, string? Type, int Line);

    private static void CollectLocalBindings(
        ProgramNode ast,
        IEnumerable<AstNode> nodes,
        int oneBasedLine,
        List<LocalBindingCandidate> locals)
    {
        foreach (var node in nodes)
        {
            if (node.Line > oneBasedLine) continue;

            switch (node)
            {
                case LetStmtNode let:
                    locals.Add(new LocalBindingCandidate(
                        let.Name,
                        let.BindingKind,
                        let.Type is not null ? FT(let.Type) : InferExpressionType(ast, let.Init),
                        let.Line));
                    break;

                case ForStmtNode forStmt:
                    if (InferForElementType(ast, ExprName(forStmt.Iter), forStmt.Line) is { } loopType)
                    {
                        locals.Add(new LocalBindingCandidate(forStmt.Var, "for", loopType, forStmt.Line));
                    }
                    CollectLocalBindings(ast, forStmt.Body, oneBasedLine, locals);
                    break;

                case EveryStmtNode every:
                    CollectLocalBindings(ast, every.Body, oneBasedLine, locals);
                    break;

                case LoopStmtNode loop:
                    CollectLocalBindings(ast, loop.Body, oneBasedLine, locals);
                    break;

                case IfStmtNode ifStmt:
                    CollectLocalBindings(ast, ifStmt.Then, oneBasedLine, locals);
                    if (ifStmt.Else is ElseBlockNode elseBlock)
                    {
                        CollectLocalBindings(ast, elseBlock.Body, oneBasedLine, locals);
                    }
                    else if (ifStmt.Else is IfStmtNode elseIf)
                    {
                        CollectLocalBindings(ast, [elseIf], oneBasedLine, locals);
                    }
                    break;

                case MatchStmtNode match:
                    foreach (var arm in match.Arms)
                    {
                        CollectLocalBindings(ast, arm.Body, oneBasedLine, locals);
                    }
                    break;
            }
        }
    }

    private static string? InferExpressionType(ProgramNode ast, ExprNode expr) => expr switch
    {
        CallExprNode call => InferCallType(ast, call),
        StructLiteralNode literal => literal.Name,
        LiteralNode literal => LiteralType(literal),
        BinaryExprNode binary when IsBooleanBinaryOperator(binary.Op) => "Bool",
        UnaryExprNode { Op: "not" or "!" } => "Bool",
        IdentifierExprNode id => FindTaskParamType(ast, id.Name, expr.Line),
        _ => null
    };

    private static string? InferCallType(ProgramNode ast, CallExprNode call)
    {
        if (call.Callee is not IdentifierExprNode callee)
        {
            return null;
        }

        if (callee.Name.Equals("latest", StringComparison.OrdinalIgnoreCase) &&
            call.Args.Count == 1 &&
            InferExpressionType(ast, call.Args[0].Value) is { } latestSourceType)
        {
            return CommElementType(latestSourceType, "stream");
        }

        if (callee.Name.Equals("batch", StringComparison.OrdinalIgnoreCase) &&
            call.Args.Count == 2 &&
            InferExpressionType(ast, call.Args[0].Value) is { } batchSourceType &&
            CommElementType(batchSourceType, "queue") is { } batchElementType)
        {
            return $"List<{batchElementType}>";
        }

        return ReturnTypeOf(ast, callee.Name);
    }

    private static bool IsBooleanBinaryOperator(string op) => op is
        "==" or "!=" or "<" or "<=" or ">" or ">=" or
        "and" or "&&" or "or" or "||";

    private static string? ReturnTypeOf(ProgramNode ast, string callableName)
    {
        foreach (var decl in ast.Declarations)
        {
            switch (decl)
            {
                case FnDeclarationNode fn when fn.Name == callableName:
                    return fn.ReturnType is null ? null : FT(fn.ReturnType);
                case TaskDeclarationNode task when task.Name == callableName:
                    return task.OutputType is null ? null : FT(task.OutputType);
                case ExternTaskNode externTask when externTask.Name == callableName:
                    return externTask.OutputType is null ? null : FT(externTask.OutputType);
                case ExternContractNode contract:
                    var function = contract.Functions.FirstOrDefault(fn => fn.Name == callableName);
                    if (function?.ReturnType is not null) return FT(function.ReturnType);
                    break;
            }
        }

        return null;
    }

    private static string? FindTaskParamType(ProgramNode ast, string name, int oneBasedLine)
    {
        var task = ast.Declarations
            .OfType<TaskDeclarationNode>()
            .Where(t => t.Line <= oneBasedLine)
            .OrderByDescending(t => t.Line)
            .FirstOrDefault();

        return task?.Params.FirstOrDefault(param => param.Name == name)?.Type is { } type
            ? FT(type)
            : null;
    }

    private static string? LiteralType(LiteralNode literal) => literal.Value switch
    {
        bool => "Bool",
        string => "Text",
        double or float => "Float",
        int or long => "Int",
        _ => null
    };

    private static string? ExprName(ExprNode expr) => expr is IdentifierExprNode id ? id.Name : null;

    private static (string Var, string Iter)? FindEnclosingForBinding(string source, long line, long character)
    {
        var normalized = source.ReplaceLineEndings("\n");
        var offset = OffsetAt(normalized, line, character);
        if (offset is null) return null;

        (string Var, string Iter)? result = null;
        foreach (Match match in Regex.Matches(
            normalized,
            @"\bfor\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{"))
        {
            if (match.Index > offset.Value) break;

            if (offset.Value >= match.Index && offset.Value <= match.Index + match.Length)
            {
                result = (match.Groups[1].Value, match.Groups[2].Value);
                continue;
            }

            var openBrace = normalized.IndexOf('{', match.Index);
            var closeBrace = FindMatchingBrace(normalized, openBrace);
            if (openBrace >= 0 && closeBrace >= offset.Value && offset.Value > openBrace)
            {
                result = (match.Groups[1].Value, match.Groups[2].Value);
            }
        }

        return result;
    }

    private static string? InferForElementType(ProgramNode ast, string? iteratorName, int oneBasedLine)
    {
        if (string.IsNullOrWhiteSpace(iteratorName)) return null;

        var task = ast.Declarations
            .OfType<TaskDeclarationNode>()
            .Where(t => t.Line <= oneBasedLine)
            .OrderByDescending(t => t.Line)
            .FirstOrDefault();

        var paramType = task?.Params.FirstOrDefault(p => p.Name == iteratorName)?.Type;
        if (paramType is not null)
        {
            return ElementType(paramType);
        }

        return FindVisibleLetBindingType(ast, iteratorName, oneBasedLine) is { } localType
            ? ElementType(localType)
            : null;
    }

    private static string? FindVisibleLetBindingType(ProgramNode ast, string name, int oneBasedLine)
    {
        var task = ast.Declarations
            .OfType<TaskDeclarationNode>()
            .Where(t => t.Line <= oneBasedLine)
            .OrderByDescending(t => t.Line)
            .FirstOrDefault();

        if (task is null) return null;

        var locals = new List<LocalBindingCandidate>();
        CollectLetBindings(ast, task.Body, oneBasedLine, locals);

        return locals
            .Where(local => local.Name == name)
            .OrderByDescending(local => local.Line)
            .Select(local => local.Type)
            .FirstOrDefault(type => type is not null);
    }

    private static void CollectLetBindings(
        ProgramNode ast,
        IEnumerable<AstNode> nodes,
        int oneBasedLine,
        List<LocalBindingCandidate> locals)
    {
        foreach (var node in nodes)
        {
            if (node.Line > oneBasedLine) continue;

            switch (node)
            {
                case LetStmtNode let:
                    locals.Add(new LocalBindingCandidate(
                        let.Name,
                        let.BindingKind,
                        let.Type is not null ? FT(let.Type) : InferExpressionType(ast, let.Init),
                        let.Line));
                    break;

                case ForStmtNode forStmt:
                    CollectLetBindings(ast, forStmt.Body, oneBasedLine, locals);
                    break;

                case EveryStmtNode every:
                    CollectLetBindings(ast, every.Body, oneBasedLine, locals);
                    break;

                case LoopStmtNode loop:
                    CollectLetBindings(ast, loop.Body, oneBasedLine, locals);
                    break;

                case IfStmtNode ifStmt:
                    CollectLetBindings(ast, ifStmt.Then, oneBasedLine, locals);
                    if (ifStmt.Else is ElseBlockNode elseBlock)
                    {
                        CollectLetBindings(ast, elseBlock.Body, oneBasedLine, locals);
                    }
                    else if (ifStmt.Else is IfStmtNode elseIf)
                    {
                        CollectLetBindings(ast, [elseIf], oneBasedLine, locals);
                    }
                    break;

                case MatchStmtNode match:
                    foreach (var arm in match.Arms)
                    {
                        CollectLetBindings(ast, arm.Body, oneBasedLine, locals);
                    }
                    break;
            }
        }
    }

    private static string? ElementType(TypeRefNode type) => type switch
    {
        CommTypeNode c => FT(c.Element),
        GenericTypeNode { Name: "List", TypeArgs.Count: > 0 } g => FT(g.TypeArgs[0]),
        ArrayTypeNode a => FT(a.Element),
        _ => null
    };

    private static string? ElementType(string type) =>
        GenericElementType(type, "List") ??
        CommElementType(type, "chan") ??
        CommElementType(type, "queue") ??
        CommElementType(type, "stream");

    private static string? CommElementType(string type, string kind) =>
        GenericElementType(type, kind);

    private static string? GenericElementType(string type, string name)
    {
        var prefix = name + "<";
        return type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               type.EndsWith(">", StringComparison.Ordinal)
            ? type[prefix.Length..^1].Trim()
            : null;
    }

    private static int? OffsetAt(string source, long line, long character)
    {
        if (line < 0 || character < 0) return null;

        var currentLine = 0L;
        var offset = 0;
        while (currentLine < line)
        {
            var next = source.IndexOf('\n', offset);
            if (next < 0) return null;
            offset = next + 1;
            currentLine++;
        }

        return offset + (int)Math.Min(character, Math.Max(0, source.Length - offset));
    }

    private static int FindMatchingBrace(string source, int openBrace)
    {
        if (openBrace < 0) return -1;

        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : 0;
            depth -= source[i] == '}' ? 1 : 0;
            if (depth == 0) return i;
        }

        return source.Length - 1;
    }

    private static string DisplayWord(string source, long line, WordAtPosition.WordInfo word)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        if (line < 0 || line >= lines.Length) return word.Word;

        var currentLine = lines[line];
        return word.StartChar > 0 && currentLine[word.StartChar - 1] == '@'
            ? "@" + word.Word
            : word.Word;
    }

    private static string FormatTask(TaskDeclarationNode t)
    {
        var ps   = string.Join(", ", t.Params.Select(p => $"{p.Name}: {FT(p.Type)}"));
        var plat = t.Platforms.Count > 0
            ? " " + string.Join(" ", t.Platforms.Select(p => $"@{p.Target}"))
            : "";
        var ret  = t.OutputType is not null ? $" -> {FT(t.OutputType)}" : "";
        return $"```junexis\ntask {t.Name}({ps}){plat}{ret}\n```";
    }

    private static string FormatStruct(StructDeclarationNode s)
    {
        var fields = string.Join("\n  ", s.Fields.Select(f => $"{f.Name}: {FT(f.Type)}"));
        return $"```junexis\nstruct {s.Name} {{\n  {fields}\n}}\n```";
    }

    private static string FormatEnum(EnumDeclarationNode e)
    {
        var variants = string.Join("\n  ", e.Variants.Select(v =>
            v.Payload.Count > 0
                ? $"{v.Name}({string.Join(", ", v.Payload.Select(FT))})"
                : v.Name));
        return $"```junexis\nenum {e.Name} {{\n  {variants}\n}}\n```";
    }

    private static string FormatFn(FnDeclarationNode f)
    {
        var ps  = string.Join(", ", f.Params.Select(p => $"{p.Name}: {FT(p.Type)}"));
        var ret = f.ReturnType is not null ? $" -> {FT(f.ReturnType)}" : "";
        return $"```junexis\nfn {f.Name}({ps}){ret}\n```";
    }

    private static string FormatExternTask(ExternTaskNode et)
    {
        var ps = string.Join(", ", et.Params.Select(p => $"{p.Name}: {FT(p.Type)}"));
        return $"```junexis\nextern task {et.Name}({ps})\n```";
    }

    private static string FormatExternContract(ExternContractNode ec)
    {
        var target = ec.Target is not null ? $"\n  target {ec.Target}" : "";
        var language = ec.Language is not null ? $"\n  language {ec.Language}" : "";
        var file = ec.UserFile is not null ? $"\n  file \"{ec.UserFile}\"" : "";
        return $"```junexis\nextern {ec.Role} {ec.Name} {{{target}{language}{file}\n}}\n```";
    }

    private static string FormatEnumVariant(EnumVariantNode ev) =>
        ev.Payload.Count > 0
            ? $"```junexis\n{ev.Name}({string.Join(", ", ev.Payload.Select(FT))})\n```"
            : $"```junexis\n{ev.Name}\n```";

    // ── Formatowanie typu ─────────────────────────────────────────────────

    private static string FT(TypeRefNode t) => t switch
    {
        NamedTypeNode n    => n.Name,
        CommTypeNode c     => $"{c.Kind.ToString().ToLowerInvariant()}<{FT(c.Element)}>",
        MapTypeNode m      => $"Map<{FT(m.Key)}, {FT(m.Value)}>",
        ArrayTypeNode a    => $"[{FT(a.Element)}]",
        GenericTypeNode g  => $"{g.Name}<{string.Join(", ", g.TypeArgs.Select(FT))}>",
        UnionTypeNode u    => string.Join(" | ", u.Variants.Select(FT)),
        _                  => "?"
    };
}
