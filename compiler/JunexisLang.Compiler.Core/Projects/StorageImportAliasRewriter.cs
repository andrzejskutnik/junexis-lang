using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Core.Projects;

/// <summary>Obniża lokalne aliasy API storage do kanonicznych nazw przed utworzeniem wspólnego IR.</summary>
internal static class StorageImportAliasRewriter
{
    internal static AstNode Rewrite(
        AstNode declaration,
        IEnumerable<ProjectVisibleSymbol> visibleSymbols)
    {
        var names = BuildNameMap(visibleSymbols);
        if (names.Count == 0)
            return declaration;

        return declaration switch
        {
            StructDeclarationNode structure => structure with
            {
                Fields = structure.Fields.Select(field => field with
                {
                    Type = Rewrite(field.Type, names),
                    Default = field.Default is null ? null : Rewrite(field.Default, names)
                }).ToList()
            },
            EnumDeclarationNode enumeration => enumeration with
            {
                Variants = enumeration.Variants.Select(variant => variant with
                {
                    Payload = variant.Payload.Select(type => Rewrite(type, names)).ToList()
                }).ToList()
            },
            FnDeclarationNode function => function with
            {
                Params = function.Params.Select(parameter => Rewrite(parameter, names)).ToList(),
                ReturnType = function.ReturnType is null ? null : Rewrite(function.ReturnType, names),
                Body = function.Body.Select(statement => Rewrite(statement, names)).ToList()
            },
            TaskDeclarationNode task => task with
            {
                Params = task.Params.Select(parameter => Rewrite(parameter, names)).ToList(),
                OutputType = task.OutputType is null ? null : Rewrite(task.OutputType, names),
                Body = task.Body.Select(item => RewriteBodyNode(item, names)).ToList()
            },
            ExternTaskNode external => external with
            {
                Params = external.Params.Select(parameter => Rewrite(parameter, names)).ToList(),
                OutputType = external.OutputType is null ? null : Rewrite(external.OutputType, names)
            },
            ExternContractNode contract => contract with
            {
                Functions = contract.Functions.Select(function => function with
                {
                    Params = function.Params.Select(parameter => Rewrite(parameter, names)).ToList(),
                    ReturnType = function.ReturnType is null ? null : Rewrite(function.ReturnType, names)
                }).ToList()
            },
            _ => declaration
        };
    }

    private static IReadOnlyDictionary<string, string> BuildNameMap(
        IEnumerable<ProjectVisibleSymbol> visibleSymbols)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in visibleSymbols)
        {
            if (symbol.IsLocal ||
                symbol.Declaration is not StorageDeclarationNode storage ||
                symbol.VisibleName == storage.Name ||
                symbol.VisibleName.Contains("::", StringComparison.Ordinal))
                continue;

            foreach (var query in storage.Queries)
            {
                names.TryAdd(
                    StorageQueryNames.Callable(symbol.VisibleName, query.Name),
                    StorageQueryNames.Callable(storage.Name, query.Name));
                names.TryAdd(
                    StorageQueryNames.Row(symbol.VisibleName, query.Name),
                    StorageQueryNames.Row(storage.Name, query.Name));
            }
        }
        return names;
    }

    private static AstNode RewriteBodyNode(AstNode node, IReadOnlyDictionary<string, string> names) => node switch
    {
        StmtNode statement => Rewrite(statement, names),
        StateDeclarationNode state => state with
        {
            Type = Rewrite(state.Type, names),
            Init = state.Init is null ? null : Rewrite(state.Init, names)
        },
        ConfigBlockNode config => config with
        {
            Fields = config.Fields.Select(field => field with
            {
                Type = Rewrite(field.Type, names)
            }).ToList()
        },
        LifecycleHookNode hook => hook with
        {
            Body = hook.Body.Select(statement => Rewrite(statement, names)).ToList()
        },
        _ => node
    };

    private static ParamNode Rewrite(ParamNode parameter, IReadOnlyDictionary<string, string> names) => parameter with
    {
        Type = Rewrite(parameter.Type, names),
        Default = parameter.Default is null ? null : Rewrite(parameter.Default, names)
    };

    private static TypeRefNode Rewrite(TypeRefNode type, IReadOnlyDictionary<string, string> names) => type switch
    {
        NamedTypeNode named when names.TryGetValue(named.Name, out var canonical) => named with { Name = canonical },
        CommTypeNode communication => communication with { Element = Rewrite(communication.Element, names) },
        MapTypeNode map => map with { Key = Rewrite(map.Key, names), Value = Rewrite(map.Value, names) },
        GenericTypeNode generic => generic with
        {
            TypeArgs = generic.TypeArgs.Select(argument => Rewrite(argument, names)).ToList()
        },
        ArrayTypeNode array => array with { Element = Rewrite(array.Element, names) },
        UnionTypeNode union => union with
        {
            Variants = union.Variants.Select(variant => Rewrite(variant, names)).ToList()
        },
        _ => type
    };

    private static StmtNode Rewrite(StmtNode statement, IReadOnlyDictionary<string, string> names) => statement switch
    {
        LetStmtNode let => let with
        {
            Type = let.Type is null ? null : Rewrite(let.Type, names),
            Init = Rewrite(let.Init, names)
        },
        ReturnStmtNode value => value with
        {
            Value = value.Value is null ? null : Rewrite(value.Value, names)
        },
        DelayStmtNode delay => delay with { Duration = Rewrite(delay.Duration, names) },
        LogStmtNode log => log with { Args = log.Args.Select(value => Rewrite(value, names)).ToList() },
        SendStmtNode send => send with
        {
            Value = Rewrite(send.Value, names),
            Target = send.Target is null ? null : Rewrite(send.Target, names)
        },
        RecvStmtNode receive => receive with { Source = Rewrite(receive.Source, names) },
        AckStmtNode ack => ack with { Message = Rewrite(ack.Message, names) },
        NackStmtNode nack => nack with
        {
            Message = Rewrite(nack.Message, names),
            Reason = nack.Reason is null ? null : Rewrite(nack.Reason, names)
        },
        RespondStmtNode respond => respond with { Value = Rewrite(respond.Value, names) },
        ExprStmtNode expression => expression with { Expr = Rewrite(expression.Expr, names) },
        IfStmtNode conditional => conditional with
        {
            Condition = Rewrite(conditional.Condition, names),
            Then = conditional.Then.Select(item => Rewrite(item, names)).ToList(),
            Else = conditional.Else switch
            {
                IfStmtNode nested => Rewrite(nested, names),
                ElseBlockNode block => block with
                {
                    Body = block.Body.Select(item => Rewrite(item, names)).ToList()
                },
                _ => conditional.Else
            }
        },
        ForStmtNode loop => loop with
        {
            Iter = Rewrite(loop.Iter, names),
            Body = loop.Body.Select(item => Rewrite(item, names)).ToList()
        },
        EveryStmtNode every => every with
        {
            Interval = Rewrite(every.Interval, names),
            Body = every.Body.Select(item => Rewrite(item, names)).ToList()
        },
        LoopStmtNode loop => loop with { Body = loop.Body.Select(item => Rewrite(item, names)).ToList() },
        MatchStmtNode match => match with
        {
            Subject = Rewrite(match.Subject, names),
            Arms = match.Arms.Select(arm => arm with
            {
                Body = arm.Body.Select(item => Rewrite(item, names)).ToList(),
                BodyExpr = arm.BodyExpr is null ? null : Rewrite(arm.BodyExpr, names)
            }).ToList()
        },
        _ => statement
    };

    private static ExprNode Rewrite(ExprNode expression, IReadOnlyDictionary<string, string> names) => expression switch
    {
        IdentifierExprNode identifier when names.TryGetValue(identifier.Name, out var canonical) =>
            identifier with { Name = canonical },
        BinaryExprNode binary => binary with
        {
            Left = Rewrite(binary.Left, names),
            Right = Rewrite(binary.Right, names)
        },
        UnaryExprNode unary => unary with { Operand = Rewrite(unary.Operand, names) },
        CallExprNode call => call with
        {
            Callee = Rewrite(call.Callee, names),
            Args = call.Args.Select(argument => argument with
            {
                Value = Rewrite(argument.Value, names)
            }).ToList()
        },
        FieldAccessNode field => field with { Base = Rewrite(field.Base, names) },
        IndexExprNode index => index with
        {
            Base = Rewrite(index.Base, names),
            Index = Rewrite(index.Index, names)
        },
        StructLiteralNode value => value with
        {
            Name = names.GetValueOrDefault(value.Name, value.Name),
            Fields = value.Fields.Select(field => field with
            {
                Value = Rewrite(field.Value, names)
            }).ToList()
        },
        ArrayLiteralNode array => array with
        {
            Elements = array.Elements.Select(item => Rewrite(item, names)).ToList()
        },
        JsonLiteralNode json => json with
        {
            Fields = json.Fields.Select(field => field with
            {
                Value = Rewrite(field.Value, names)
            }).ToList()
        },
        RecvExprNode receive => receive with { Source = Rewrite(receive.Source, names) },
        SendExprNode send => send with
        {
            Value = Rewrite(send.Value, names),
            Target = send.Target is null ? null : Rewrite(send.Target, names)
        },
        ParenExprNode parenthesized => parenthesized with { Inner = Rewrite(parenthesized.Inner, names) },
        _ => expression
    };
}
