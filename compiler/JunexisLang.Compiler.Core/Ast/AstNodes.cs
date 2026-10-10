namespace JunexisLang.Compiler.Core.Ast;

// ═══════════════════════════════════════════════════════════════════════════
// Węzły AST — JunexisLang
//
// Zasady:
//  - Każdy węzeł dziedziczy po AstNode (lokalizacja w źródle).
//  - Czyste rekordy — brak logiki, tylko dane.
//  - Opaque bloki (narrative, deployment, supervisor, quirks, circuit_breaker,
//    protocol) przechowują surowy tekst ciała — semantyka w IR stage.
// ═══════════════════════════════════════════════════════════════════════════

// ─────────────────── Baza ───────────────────────────────────────────────────

/// <summary>Wspólna baza dla wszystkich węzłów AST — pozycja w źródle.</summary>
public abstract record AstNode(string File, int Line, int Column);

// ─────────────────── Program ────────────────────────────────────────────────

/// <summary>Korzeń drzewa AST — cały plik .jnx.</summary>
public sealed record ProgramNode(
    string File,
    IReadOnlyList<ImportNode> Imports,
    IReadOnlyList<AstNode>    Declarations)
    : AstNode(File, 1, 1);

// ─────────────────── Import ─────────────────────────────────────────────────

/// <summary>
/// import stdlib as std
/// import stdlib { X, Y as Z }
/// import junexis::runtime { Channel }
/// </summary>
public sealed record ImportNode(
    string File, int Line, int Column,
    string       ModulePath,   // "stdlib" lub "junexis::runtime"
    string?      Alias,        // alias za AS (bez symboli)
    IReadOnlyList<ImportSymbolNode> Symbols)  // pusta gdy import bez nawiasów
    : AstNode(File, Line, Column);

/// <summary>Jeden symbol w import … { X as Y }.</summary>
public sealed record ImportSymbolNode(
    string File, int Line, int Column,
    string Name,
    string? Alias)
    : AstNode(File, Line, Column);

// ─────────────────── Typy ────────────────────────────────────────────────────

/// <summary>Odwołanie do typu — np. Int, chan&lt;Float&gt;, Map&lt;Text,Int&gt;, MyType | Null.</summary>
public abstract record TypeRefNode(string File, int Line, int Column)
    : AstNode(File, Line, Column);

/// <summary>Typ wbudowany lub user-defined: Int, Float, Text, MyStruct, u8 …</summary>
public sealed record NamedTypeNode(
    string File, int Line, int Column,
    string Name)
    : TypeRefNode(File, Line, Column);

/// <summary>chan&lt;T&gt; | queue&lt;T&gt; | stream&lt;T&gt;</summary>
public sealed record CommTypeNode(
    string File, int Line, int Column,
    CommKind Kind,
    TypeRefNode Element)
    : TypeRefNode(File, Line, Column);

public enum CommKind { Chan, Queue, Stream }

/// <summary>Map&lt;K,V&gt;</summary>
public sealed record MapTypeNode(
    string File, int Line, int Column,
    TypeRefNode Key,
    TypeRefNode Value)
    : TypeRefNode(File, Line, Column);

/// <summary>Foo&lt;T,U&gt; — generyczny typ user-defined.</summary>
public sealed record GenericTypeNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<TypeRefNode> TypeArgs)
    : TypeRefNode(File, Line, Column);

/// <summary>[T] — tablica.</summary>
public sealed record ArrayTypeNode(
    string File, int Line, int Column,
    TypeRefNode Element)
    : TypeRefNode(File, Line, Column);

/// <summary>A | B | C — unia.</summary>
public sealed record UnionTypeNode(
    string File, int Line, int Column,
    IReadOnlyList<TypeRefNode> Variants)
    : TypeRefNode(File, Line, Column);

// ─────────────────── Struct / Enum ───────────────────────────────────────────

/// <summary>struct Point { x: Float; y: Float }</summary>
public sealed record StructDeclarationNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<StructFieldNode> Fields)
    : AstNode(File, Line, Column);

/// <summary>Pole struktury: x: Float = 0.0</summary>
public sealed record StructFieldNode(
    string File, int Line, int Column,
    string     Name,
    TypeRefNode Type,
    ExprNode?  Default)
    : AstNode(File, Line, Column);

/// <summary>enum Color { Red, Green, Blue } lub enum Option&lt;T&gt; { Some(T), None }</summary>
public sealed record EnumDeclarationNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<string>          TypeParams,   // pusta gdy brak typeparams
    IReadOnlyList<EnumVariantNode> Variants)
    : AstNode(File, Line, Column);

/// <summary>Wariant enuma: Some(T) lub Empty</summary>
public sealed record EnumVariantNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<TypeRefNode> Payload)   // pusta gdy brak danych
    : AstNode(File, Line, Column);

// ───────────── Storage model, query i mutation intent (D22/D35) ─────────────

/// <summary>Jawna deklaracja logicznego modelu i operacji storage.</summary>
public sealed record StorageDeclarationNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<StorageEntityNode> Entities,
    IReadOnlyList<StorageQueryNode> Queries,
    IReadOnlyList<StorageMutationNode> Mutations)
    : AstNode(File, Line, Column);

public sealed record StorageEntityNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<StorageFieldNode> Fields,
    IReadOnlyList<StoragePrimaryKeyNode> PrimaryKeys)
    : AstNode(File, Line, Column);

public sealed record StorageFieldNode(
    string File, int Line, int Column,
    string Name,
    TypeRefNode Type)
    : AstNode(File, Line, Column);

public sealed record StoragePrimaryKeyNode(
    string File, int Line, int Column,
    IReadOnlyList<string> Fields)
    : AstNode(File, Line, Column);

public sealed record StorageQueryNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<ParamNode> Params,
    string SourceEntity,
    IReadOnlyList<StorageQueryStepNode> Steps)
    : AstNode(File, Line, Column);

public abstract record StorageQueryStepNode(string File, int Line, int Column)
    : AstNode(File, Line, Column);

public sealed record StorageFilterStepNode(
    string File, int Line, int Column,
    ExprNode Predicate)
    : StorageQueryStepNode(File, Line, Column);

public sealed record StorageProjectStepNode(
    string File, int Line, int Column,
    IReadOnlyList<StorageProjectionNode> Projections)
    : StorageQueryStepNode(File, Line, Column);

public sealed record StorageProjectionNode(
    string File, int Line, int Column,
    string Field,
    string? Alias)
    : AstNode(File, Line, Column);

public sealed record StorageSortStepNode(
    string File, int Line, int Column,
    IReadOnlyList<StorageSortKeyNode> Keys)
    : StorageQueryStepNode(File, Line, Column);

public sealed record StorageSortKeyNode(
    string File, int Line, int Column,
    string Field,
    bool Descending)
    : AstNode(File, Line, Column);

public sealed record StorageLimitStepNode(
    string File, int Line, int Column,
    long Count,
    long Offset)
    : StorageQueryStepNode(File, Line, Column);

/// <summary>Rodzaj mutacji zapisany w źródle Junexis.</summary>
public enum StorageMutationKindNode
{
    /// <summary>Utworzenie rekordu.</summary>
    Insert,
    /// <summary>Zmiana istniejących pól.</summary>
    Update,
    /// <summary>Usunięcie rekordu lub rekordów.</summary>
    Delete
}

/// <summary>Jawny zasięg mutacji zapisany przez autora.</summary>
public enum StorageMutationCardinalityNode
{
    /// <summary>Operacja pojedynczego rekordu.</summary>
    Single,
    /// <summary>Jawna operacja wielu rekordów.</summary>
    Many
}

/// <summary>Nazwana mutacja wewnątrz logicznego modelu storage.</summary>
public sealed record StorageMutationNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<ParamNode> Params,
    string Entity,
    StorageMutationKindNode Kind,
    StorageMutationCardinalityNode Cardinality,
    IReadOnlyList<StorageMutationAssignmentNode> Assignments,
    ExprNode? Predicate)
    : AstNode(File, Line, Column);

/// <summary>Źródłowe przypisanie pola w mutacji storage.</summary>
public sealed record StorageMutationAssignmentNode(
    string File, int Line, int Column,
    string Field,
    ExprNode Value)
    : AstNode(File, Line, Column);

// ─────────────────── fn ──────────────────────────────────────────────────────

/// <summary>fn add(a: Int, b: Int) -> Int { return a + b }</summary>
public sealed record FnDeclarationNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<ParamNode> Params,
    TypeRefNode?             ReturnType,
    IReadOnlyList<StmtNode>  Body)
    : AstNode(File, Line, Column);

// ─────────────────── task ────────────────────────────────────────────────────

/// <summary>task SensorRead() @embedded -> chan&lt;SensorData&gt; { … }</summary>
public sealed record TaskDeclarationNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<ParamNode>          Params,
    IReadOnlyList<PlatformAnnotation> Platforms,
    TypeRefNode?                      OutputType,  // z communicationSignature
    IReadOnlyList<AstNode>            Body)        // stateDecl | configBlock | lifecycleHook | stmt
    : AstNode(File, Line, Column);

/// <summary>Parametr: name: type = default</summary>
public sealed record ParamNode(
    string File, int Line, int Column,
    string     Name,
    TypeRefNode Type,
    ExprNode?  Default)
    : AstNode(File, Line, Column);

/// <summary>@embedded(esp32c3, memory=4MB)</summary>
public sealed record PlatformAnnotation(
    string File, int Line, int Column,
    string Target,          // "embedded", "server", …
    IReadOnlyList<PlatformArgNode> Args)
    : AstNode(File, Line, Column);

/// <summary>Argument adnotacji platformy: pozycyjny lub nazwany.</summary>
public sealed record PlatformArgNode(
    string File, int Line, int Column,
    string?  Key,     // null → pozycyjny
    ExprNode Value)
    : AstNode(File, Line, Column);

/// <summary>state counter: u32 = 0</summary>
public sealed record StateDeclarationNode(
    string File, int Line, int Column,
    string     Name,
    TypeRefNode Type,
    ExprNode?  Init)
    : AstNode(File, Line, Column);

/// <summary>config { port: u16 : 8080 }</summary>
public sealed record ConfigBlockNode(
    string File, int Line, int Column,
    IReadOnlyList<ConfigFieldNode> Fields)
    : AstNode(File, Line, Column);

/// <summary>Pole bloku config: port: u16 : 8080</summary>
public sealed record ConfigFieldNode(
    string File, int Line, int Column,
    string     Name,
    TypeRefNode Type,
    LiteralNode? Default)
    : AstNode(File, Line, Column);

/// <summary>on connect { … }</summary>
public sealed record LifecycleHookNode(
    string File, int Line, int Column,
    string                  Event,
    IReadOnlyList<StmtNode> Body)
    : AstNode(File, Line, Column);

// ─────────────────── pipeline ────────────────────────────────────────────────

/// <summary>pipeline ProcessOrder { use A -> B; parallel { … } }</summary>
public sealed record PipelineDeclarationNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<PipelineConnectionNode> Connections)
    : AstNode(File, Line, Column);

/// <summary>use Source.output -> Sink.input  lub  parallel { … }</summary>
public abstract record PipelineConnectionNode(string File, int Line, int Column)
    : AstNode(File, Line, Column);

/// <summary>use A.port -> B.port (lub bez portu)</summary>
public sealed record UseConnectionNode(
    string File, int Line, int Column,
    string  SourceTask, string? SourcePort,
    string? TargetTask, string? TargetPort)
    : PipelineConnectionNode(File, Line, Column);

/// <summary>parallel { … }</summary>
public sealed record ParallelBlockNode(
    string File, int Line, int Column,
    IReadOnlyList<PipelineConnectionNode> Connections)
    : PipelineConnectionNode(File, Line, Column);

// ─────────────────── device ──────────────────────────────────────────────────

/// <summary>device sensor_kitchen @embedded(esp32c3) { hardware { … } }</summary>
public sealed record DeviceDeclarationNode(
    string File, int Line, int Column,
    string             Name,
    PlatformAnnotation Platform,
    IReadOnlyList<AstNode> Members)   // ConfigBlockNode | DeviceBlockNode | NativeSectionNode
    : AstNode(File, Line, Column);

/// <summary>hardware { pin: u8 : 4 } — nazwany blok urządzenia</summary>
public sealed record DeviceBlockNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<ConfigFieldNode> Fields)
    : AstNode(File, Line, Column);

// ─────────────────── native ──────────────────────────────────────────────────

/// <summary>native { @cpp { include: "DHT.h" } }</summary>
public sealed record NativeSectionNode(
    string File, int Line, int Column,
    IReadOnlyList<NativeLanguageNode> Languages)
    : AstNode(File, Line, Column);

/// <summary>@cpp { include: "…" use: "…" }</summary>
public sealed record NativeLanguageNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<NativeFieldNode> Fields)
    : AstNode(File, Line, Column);

/// <summary>include: "DHT.h"</summary>
public sealed record NativeFieldNode(
    string File, int Line, int Column,
    string Name,
    string RawValue)   // surowa wartość jako tekst
    : AstNode(File, Line, Column);

// ─────────────────── Opaque declarations ─────────────────────────────────────
// Parser zbiera balanced-braces. Semantyka w IR stage.

/// <summary>narrative "…" { … }</summary>
public sealed record NarrativeDeclarationNode(
    string File, int Line, int Column,
    string  Label,
    string  Body)
    : AstNode(File, Line, Column);

/// <summary>deployment "…" { … }</summary>
public sealed record DeploymentDeclarationNode(
    string File, int Line, int Column,
    string? Label,
    string  Body)
    : AstNode(File, Line, Column);

/// <summary>supervisor Name { … }</summary>
public sealed record SupervisorDeclarationNode(
    string File, int Line, int Column,
    string Name,
    string Body)
    : AstNode(File, Line, Column);

/// <summary>quirks "esp32c3" { … }</summary>
public sealed record QuirksDeclarationNode(
    string File, int Line, int Column,
    string Label,
    string Body)
    : AstNode(File, Line, Column);

/// <summary>circuit_breaker Name { … }</summary>
public sealed record CircuitBreakerDeclarationNode(
    string File, int Line, int Column,
    string Name,
    string Body)
    : AstNode(File, Line, Column);

/// <summary>protocol rest { … }</summary>
public sealed record ProtocolDeclarationNode(
    string File, int Line, int Column,
    string Name,
    string Body)
    : AstNode(File, Line, Column);

// ─────────────────── extern (D13) ────────────────────────────────────────────

/// <summary>extern task Foo(…) @embedded</summary>
public sealed record ExternTaskNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<ParamNode>          Params,
    IReadOnlyList<PlatformAnnotation> Platforms,
    TypeRefNode?                      OutputType)
    : AstNode(File, Line, Column);

/// <summary>extern type Handle @embedded(esp32)</summary>
public sealed record ExternTypeNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<PlatformAnnotation> Platforms)
    : AstNode(File, Line, Column);

/// <summary>extern &lt;role&gt; BoundaryName { target server; language go; file "user/server/storage.go"; fn save(...) }</summary>
public sealed record ExternContractNode(
    string File, int Line, int Column,
    string Role,
    string Name,
    string? Target,
    string? Language,
    string? UserFile,
    IReadOnlyList<ExternContractFunctionNode> Functions)
    : AstNode(File, Line, Column);

/// <summary>Function required by an extern contract.</summary>
public sealed record ExternContractFunctionNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<ParamNode> Params,
    TypeRefNode? ReturnType)
    : AstNode(File, Line, Column);

// ─────────────────── Statements ──────────────────────────────────────────────

public abstract record StmtNode(string File, int Line, int Column)
    : AstNode(File, Line, Column);

/// <summary>let x: Int = 42 | const | var</summary>
public sealed record LetStmtNode(
    string File, int Line, int Column,
    string BindingKind,  // "let" | "const" | "var"
    string     Name,
    TypeRefNode? Type,
    ExprNode   Init)
    : StmtNode(File, Line, Column);

/// <summary>return expr</summary>
public sealed record ReturnStmtNode(
    string File, int Line, int Column,
    ExprNode? Value)
    : StmtNode(File, Line, Column);

/// <summary>delay 100ms</summary>
public sealed record DelayStmtNode(
    string File, int Line, int Column,
    ExprNode Duration)
    : StmtNode(File, Line, Column);

/// <summary>log("msg", value)</summary>
public sealed record LogStmtNode(
    string File, int Line, int Column,
    IReadOnlyList<ExprNode> Args)
    : StmtNode(File, Line, Column);

/// <summary>send expr [to chan]</summary>
public sealed record SendStmtNode(
    string File, int Line, int Column,
    ExprNode  Value,
    ExprNode? Target)
    : StmtNode(File, Line, Column);

/// <summary>recv [from] expr — jako statement</summary>
public sealed record RecvStmtNode(
    string File, int Line, int Column,
    ExprNode Source)
    : StmtNode(File, Line, Column);

/// <summary>ack expr — acknowledgement of a value received from queue&lt;T&gt;</summary>
public sealed record AckStmtNode(
    string File, int Line, int Column,
    ExprNode Message)
    : StmtNode(File, Line, Column);

/// <summary>nack expr [reason expr] — negative acknowledgement of a value received from queue&lt;T&gt;</summary>
public sealed record NackStmtNode(
    string File, int Line, int Column,
    ExprNode Message,
    ExprNode? Reason)
    : StmtNode(File, Line, Column);

/// <summary>respond expr</summary>
public sealed record RespondStmtNode(
    string File, int Line, int Column,
    ExprNode Value)
    : StmtNode(File, Line, Column);

/// <summary>Dowolne wyrażenie jako statement (wywołanie funkcji itd.)</summary>
public sealed record ExprStmtNode(
    string File, int Line, int Column,
    ExprNode Expr)
    : StmtNode(File, Line, Column);

/// <summary>if cond { … } else { … }</summary>
public sealed record IfStmtNode(
    string File, int Line, int Column,
    ExprNode               Condition,
    IReadOnlyList<StmtNode> Then,
    AstNode?               Else)  // IfStmtNode (else if) lub ElseBlockNode
    : StmtNode(File, Line, Column);

/// <summary>Blok else { … } — odróżnienie od else-if.</summary>
public sealed record ElseBlockNode(
    string File, int Line, int Column,
    IReadOnlyList<StmtNode> Body)
    : AstNode(File, Line, Column);

/// <summary>for x in expr { … }</summary>
public sealed record ForStmtNode(
    string File, int Line, int Column,
    string                 Var,
    ExprNode               Iter,
    IReadOnlyList<StmtNode> Body)
    : StmtNode(File, Line, Column);

/// <summary>every 1s { … }</summary>
public sealed record EveryStmtNode(
    string File, int Line, int Column,
    ExprNode               Interval,
    IReadOnlyList<StmtNode> Body)
    : StmtNode(File, Line, Column);

/// <summary>loop { … }</summary>
public sealed record LoopStmtNode(
    string File, int Line, int Column,
    IReadOnlyList<StmtNode> Body)
    : StmtNode(File, Line, Column);

/// <summary>match expr { pattern -> expr, … }</summary>
public sealed record MatchStmtNode(
    string File, int Line, int Column,
    ExprNode               Subject,
    IReadOnlyList<MatchArmNode> Arms)
    : StmtNode(File, Line, Column);

/// <summary>pattern -> body</summary>
public sealed record MatchArmNode(
    string File, int Line, int Column,
    MatchPatternNode       Pattern,
    IReadOnlyList<StmtNode> Body,   // pusta gdy body = wyrażenie
    ExprNode?              BodyExpr)
    : AstNode(File, Line, Column);

// ─────────────────── Match patterns ──────────────────────────────────────────

public abstract record MatchPatternNode(string File, int Line, int Column)
    : AstNode(File, Line, Column);

public sealed record WildcardPatternNode(string File, int Line, int Column)
    : MatchPatternNode(File, Line, Column);

public sealed record LiteralPatternNode(
    string File, int Line, int Column,
    LiteralNode Literal)
    : MatchPatternNode(File, Line, Column);

public sealed record BindingPatternNode(
    string File, int Line, int Column,
    string Name)
    : MatchPatternNode(File, Line, Column);

public sealed record EnumVariantPatternNode(
    string File, int Line, int Column,
    string TypeName, string Variant,
    IReadOnlyList<MatchPatternNode> Fields)
    : MatchPatternNode(File, Line, Column);

// ─────────────────── Expressions ─────────────────────────────────────────────

public abstract record ExprNode(string File, int Line, int Column)
    : AstNode(File, Line, Column);

/// <summary>Literal: 42, 3.14, "hello", true, 5s, 4MB, 0xFF</summary>
public sealed record LiteralNode(
    string File, int Line, int Column,
    object Value,           // int / long / double / bool / string
    string RawText)         // oryginalna reprezentacja z kodu źródłowego
    : ExprNode(File, Line, Column);

/// <summary>Identyfikator: x, myVar</summary>
public sealed record IdentifierExprNode(
    string File, int Line, int Column,
    string Name)
    : ExprNode(File, Line, Column);

/// <summary>Dostęp do config: config.port</summary>
public sealed record ConfigAccessNode(
    string File, int Line, int Column,
    string? Field)  // null = samo "config"
    : ExprNode(File, Line, Column);

/// <summary>a + b, x == y, a and b …</summary>
public sealed record BinaryExprNode(
    string File, int Line, int Column,
    string   Op,
    ExprNode Left,
    ExprNode Right)
    : ExprNode(File, Line, Column);

/// <summary>not x, -y</summary>
public sealed record UnaryExprNode(
    string File, int Line, int Column,
    string   Op,
    ExprNode Operand)
    : ExprNode(File, Line, Column);

/// <summary>foo(a, b) lub foo(x: 1, y: 2)</summary>
public sealed record CallExprNode(
    string File, int Line, int Column,
    ExprNode                  Callee,
    IReadOnlyList<CallArgNode> Args)
    : ExprNode(File, Line, Column);

/// <summary>Argument wywołania: pozycyjny lub nazwany.</summary>
public sealed record CallArgNode(
    string File, int Line, int Column,
    string?  Name,   // null → pozycyjny
    ExprNode Value)
    : AstNode(File, Line, Column);

/// <summary>expr.field</summary>
public sealed record FieldAccessNode(
    string File, int Line, int Column,
    ExprNode Base,
    string   Field)
    : ExprNode(File, Line, Column);

/// <summary>Enum::Variant</summary>
public sealed record EnumAccessNode(
    string File, int Line, int Column,
    string Type,
    string Variant)
    : ExprNode(File, Line, Column);

/// <summary>arr[i]</summary>
public sealed record IndexExprNode(
    string File, int Line, int Column,
    ExprNode Base,
    ExprNode Index)
    : ExprNode(File, Line, Column);

/// <summary>Point { x: 1.0, y: 2.0 }</summary>
public sealed record StructLiteralNode(
    string File, int Line, int Column,
    string Name,
    IReadOnlyList<StructFieldInitNode> Fields)
    : ExprNode(File, Line, Column);

/// <summary>x: expr w struct literal</summary>
public sealed record StructFieldInitNode(
    string File, int Line, int Column,
    string   Name,
    ExprNode Value)
    : AstNode(File, Line, Column);

/// <summary>[1, 2, 3]</summary>
public sealed record ArrayLiteralNode(
    string File, int Line, int Column,
    IReadOnlyList<ExprNode> Elements)
    : ExprNode(File, Line, Column);

/// <summary>{ "key": expr } — JSON literal</summary>
public sealed record JsonLiteralNode(
    string File, int Line, int Column,
    IReadOnlyList<JsonFieldNode> Fields)
    : ExprNode(File, Line, Column);

/// <summary>"key": expr</summary>
public sealed record JsonFieldNode(
    string File, int Line, int Column,
    string   Key,
    ExprNode Value)
    : AstNode(File, Line, Column);

/// <summary>recv [from] source — jako wyrażenie</summary>
public sealed record RecvExprNode(
    string File, int Line, int Column,
    ExprNode Source)
    : ExprNode(File, Line, Column);

/// <summary>send value [to] target — jako wyrażenie</summary>
public sealed record SendExprNode(
    string File, int Line, int Column,
    ExprNode  Value,
    ExprNode? Target)
    : ExprNode(File, Line, Column);

/// <summary>(expr)</summary>
public sealed record ParenExprNode(
    string File, int Line, int Column,
    ExprNode Inner)
    : ExprNode(File, Line, Column);
