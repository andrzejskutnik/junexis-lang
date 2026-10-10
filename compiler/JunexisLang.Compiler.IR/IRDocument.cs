namespace JunexisLang.Compiler.IR;

// ═══════════════════════════════════════════════════════════════════════════
// Model IR — Intermediate Representation JunexisLang
//
// Zasady:
//  - Czyste rekordy / sealed classes — bez logiki domenowej.
//  - Wszystkie typy komunikacyjne reprezentowane przez IRCommType (D1).
//  - Platformy jako IRPlatform (D2) — embedded / server / report.
//  - Opaque bloki (narrative, deployment, supervisor, quirks, circuit_breaker,
//    protocol) zamknięte w dedykowanych węzłach z surowym tekstem (D3).
// ═══════════════════════════════════════════════════════════════════════════

// ─────────────────── Dokument (korzeń IR) ───────────────────────────────────

/// <summary>
/// Cały dokument IR — wynik transformacji jednego pliku .jnx.
/// </summary>
public sealed record IRDocument(
    string                       SourceFile,
    IReadOnlyList<IRImport>      Imports,
    IReadOnlyList<IRStruct>      Structs,
    IReadOnlyList<IREnum>        Enums,
    IReadOnlyList<IRFn>          Functions,
    IReadOnlyList<IRTask>        Tasks,
    IReadOnlyList<IRPipeline>    Pipelines,
    IReadOnlyList<IRQueueBridgeDescriptor> QueueBridges,
    IReadOnlyList<IRStreamBridgeDescriptor> StreamBridges,
    IReadOnlyList<IRChanBridgeDescriptor> ChanBridges,
    IReadOnlyList<IRDevice>      Devices,
    IReadOnlyList<IRNarrative>   Narratives,
    IReadOnlyList<IRDeployment>  Deployments,
    IReadOnlyList<IRSupervisor>  Supervisors,
    IReadOnlyList<IRQuirks>      Quirks,
    IReadOnlyList<IRCircuitBreaker> CircuitBreakers,
    IReadOnlyList<IRProtocol>    Protocols,
    IReadOnlyList<IRExternTask>  ExternTasks,
    IReadOnlyList<IRExternType>  ExternTypes,
    IReadOnlyList<IRExternContract> ExternContracts)
{
    /// <summary>Mapowanie stabilnych ścieżek pochodzenia Structural IR na pozycje w źródle.</summary>
    public IReadOnlyList<IRSourceMapEntry> SourceMap { get; init; } = [];

    /// <summary>Jawne modele oraz operacje storage zachowane dla projekcji M13/M14.</summary>
    public IReadOnlyList<IRStorageDeclaration> StorageDeclarations { get; init; } = [];
}

/// <summary>Pozycja 1-based elementu Structural IR w pliku źródłowym.</summary>
public sealed record IRSourceSpan(string File, int Line, int Column);

/// <summary>Jedno powiązanie ścieżki pochodzenia IR z pozycją źródłową.</summary>
public sealed record IRSourceMapEntry(string Path, IRSourceSpan Source);

// ─────────────────── Import ──────────────────────────────────────────────────

/// <summary>Reprezentacja import po rozwinięciu ścieżki modułu.</summary>
public sealed record IRImport(
    string  ModulePath,
    string? Alias,
    IReadOnlyList<(string Name, string? Alias)> Symbols);

// ─────────────────── Typy ────────────────────────────────────────────────────

/// <summary>Wewnętrzna reprezentacja odwołania do typu.</summary>
public abstract record IRType;

/// <summary>Typ prymitywny lub user-defined: Int, Float, Text, MyStruct …</summary>
public sealed record IRNamedType(string Name) : IRType;

/// <summary>chan&lt;T&gt; | queue&lt;T&gt; | stream&lt;T&gt; — typy komunikacyjne (D1).</summary>
public sealed record IRCommType(IRCommKind Kind, IRType Element) : IRType;

public enum IRCommKind { Chan, Queue, Stream }

/// <summary>Map&lt;K,V&gt;</summary>
public sealed record IRMapType(IRType Key, IRType Value) : IRType;

/// <summary>Foo&lt;T,U&gt; — generyczny typ user-defined.</summary>
public sealed record IRGenericType(string Name, IReadOnlyList<IRType> TypeArgs) : IRType;

/// <summary>[T] — tablica/lista.</summary>
public sealed record IRArrayType(IRType Element) : IRType;

/// <summary>A | B — typ sumy.</summary>
public sealed record IRUnionType(IReadOnlyList<IRType> Variants) : IRType;

// ─────────────────── Struct / Enum ───────────────────────────────────────────

/// <summary>struct Point { x: Float; y: Float }</summary>
public sealed record IRStruct(
    string Name,
    IReadOnlyList<IRField> Fields);

/// <summary>Pole struktury.</summary>
public sealed record IRField(string Name, IRType Type, IRExpr? Default);

/// <summary>enum Color { Red, Green, Blue }</summary>
public sealed record IREnum(
    string Name,
    IReadOnlyList<string>          TypeParams,
    IReadOnlyList<IREnumVariant>   Variants);

/// <summary>Wariant enuma: Some(T) lub Empty.</summary>
public sealed record IREnumVariant(string Name, IReadOnlyList<IRType> Payload);

// ─────────────────── fn ──────────────────────────────────────────────────────

/// <summary>Czysta funkcja fn add(…) -> Int { … } (D13 fn).</summary>
public sealed record IRFn(
    string Name,
    IReadOnlyList<IRParam> Params,
    IRType?                ReturnType,
    IReadOnlyList<IRStmt>  Body);

// ─────────────────── task ────────────────────────────────────────────────────

/// <summary>Deklaracja task z pełnym modelem IR.</summary>
public sealed record IRTask(
    string                         Name,
    IReadOnlyList<IRParam>         Params,
    IReadOnlyList<IRPlatform>      Platforms,
    IRType?                        OutputType,
    IReadOnlyList<IRStateDecl>     States,
    IRConfigBlock?                 Config,
    IReadOnlyList<IRLifecycleHook> Hooks,
    IReadOnlyList<IRStmt>          Body,
    IRNativeSection?               Native = null);

/// <summary>Parametr: name: type = default.</summary>
public sealed record IRParam(string Name, IRType Type, IRExpr? Default);

/// <summary>Adnotacja platformy (D2): @embedded(esp32c3) / @server / @report.</summary>
public sealed record IRPlatform(
    string Target,   // "embedded" / "server" / "report"
    IReadOnlyList<IRPlatformArg> Args)
{
    /// <summary>Returns the Junexis platform annotation string, e.g. "@server".</summary>
    public override string ToString() => $"@{Target}";
}

/// <summary>Argument adnotacji platformy.</summary>
public sealed record IRPlatformArg(string? Key, IRExpr Value);

/// <summary>state counter: u32 = 0</summary>
public sealed record IRStateDecl(string Name, IRType Type, IRExpr? Init);

/// <summary>config { port: u16 : 8080 }</summary>
public sealed record IRConfigBlock(IReadOnlyList<IRConfigField> Fields);

/// <summary>Pole bloku config.</summary>
public sealed record IRConfigField(string Name, IRType Type, IRExpr? Default);

/// <summary>on connect { … }</summary>
public sealed record IRLifecycleHook(string Event, IReadOnlyList<IRStmt> Body);

// ─────────────────── pipeline ────────────────────────────────────────────────

/// <summary>Deklaracja pipeline z listą połączeń.</summary>
public sealed record IRPipeline(
    string Name,
    IReadOnlyList<IRPipelineConn> Connections);

/// <summary>Jedno połączenie w pipeline.</summary>
public abstract record IRPipelineConn;

/// <summary>use A.port -> B.port</summary>
public sealed record IRUseConn(
    string  SourceTask, string? SourcePort,
    string? TargetTask, string? TargetPort)
    : IRPipelineConn;

/// <summary>parallel { … }</summary>
public sealed record IRParallelBlock(IReadOnlyList<IRPipelineConn> Connections)
    : IRPipelineConn;

/// <summary>Jawny descriptor bridge route dla queue połączenia server → report.</summary>
public sealed record IRQueueBridgeDescriptor(
    string PipelineName,
    string SourceTask,
    string? SourcePort,
    string TargetTask,
    string? TargetPort,
    string PollRoute,
    string AckRoute);

/// <summary>Jawny descriptor bridge route dla stream połączenia server → report.</summary>
public sealed record IRStreamBridgeDescriptor(
    string PipelineName,
    string SourceTask,
    string? SourcePort,
    string TargetTask,
    string? TargetPort,
    string StreamRoute);

/// <summary>Jawny descriptor bridge route dla chan połączenia server → report.</summary>
public sealed record IRChanBridgeDescriptor(
    string PipelineName,
    string SourceTask,
    string? SourcePort,
    string TargetTask,
    string? TargetPort,
    string ChannelRoute);

// ─────────────────── device ──────────────────────────────────────────────────

/// <summary>device sensor_kitchen @embedded(esp32c3) { … }</summary>
public sealed record IRDevice(
    string Name,
    IRPlatform Platform,
    IRConfigBlock? Config,
    IReadOnlyList<IRDeviceBlock> Blocks,
    IRNativeSection? Native);

/// <summary>hardware { … } / network { … } / timing { … }</summary>
public sealed record IRDeviceBlock(string Name, IReadOnlyList<IRConfigField> Fields);

/// <summary>native { @cpp { include: "…" } }</summary>
public sealed record IRNativeSection(IReadOnlyList<IRNativeLang> Languages);

/// <summary>Jeden język w sekcji native.</summary>
public sealed record IRNativeLang(string Name, IReadOnlyList<(string Key, string Value)> Fields);

// ─────────────────── Opaque declarations (D3) ────────────────────────────────

/// <summary>narrative "label" { … }</summary>
public sealed record IRNarrative(string Label, string RawBody);

/// <summary>deployment "label" { … }</summary>
public sealed record IRDeployment(string? Label, string RawBody);

/// <summary>supervisor Name { … }</summary>
public sealed record IRSupervisor(string Name, string RawBody);

/// <summary>quirks "label" { … }</summary>
public sealed record IRQuirks(string Label, string RawBody);

/// <summary>circuit_breaker Name { … }</summary>
public sealed record IRCircuitBreaker(string Name, string RawBody);

/// <summary>protocol rest { … }</summary>
public sealed record IRProtocol(string Name, string RawBody);

// ─────────────────── extern (D13) ────────────────────────────────────────────

/// <summary>extern task Foo(…) @embedded</summary>
public sealed record IRExternTask(
    string Name,
    IReadOnlyList<IRParam>    Params,
    IReadOnlyList<IRPlatform> Platforms,
    IRType?                   OutputType);

/// <summary>extern type Handle @embedded(esp32)</summary>
public sealed record IRExternType(string Name, IReadOnlyList<IRPlatform> Platforms);

/// <summary>extern &lt;role&gt; BoundaryName { target server; language go; file "user/server/storage.go"; fn save(...) }</summary>
public sealed record IRExternContract(
    string Role,
    string Name,
    string? Target,
    string? Language,
    string? UserFile,
    IReadOnlyList<IRExternContractFunction> Functions);

/// <summary>Function required by an extern contract boundary.</summary>
public sealed record IRExternContractFunction(
    string Name,
    IReadOnlyList<IRParam> Params,
    IRType? ReturnType);

// ─────────────────── Statements ──────────────────────────────────────────────

/// <summary>Bazowa klasa statementu IR.</summary>
public abstract record IRStmt;

public sealed record IRLetStmt(string Kind, string Name, IRType? Type, IRExpr Init) : IRStmt;
public sealed record IRReturnStmt(IRExpr? Value) : IRStmt;
public sealed record IRDelayStmt(IRExpr Duration) : IRStmt;
public sealed record IRLogStmt(IReadOnlyList<IRExpr> Args) : IRStmt;
public sealed record IRSendStmt(IRExpr Value, IRExpr? Target) : IRStmt;
public sealed record IRRecvStmt(IRExpr Source) : IRStmt;
public sealed record IRAckStmt(IRExpr Message) : IRStmt;
public sealed record IRNackStmt(IRExpr Message, IRExpr? Reason) : IRStmt;
public sealed record IRRespondStmt(IRExpr Value) : IRStmt;
public sealed record IRExprStmt(IRExpr Expr) : IRStmt;

public sealed record IRIfStmt(
    IRExpr               Condition,
    IReadOnlyList<IRStmt> Then,
    IRElse?              Else)
    : IRStmt;

public abstract record IRElse;
public sealed record IRElseIf(IRIfStmt IfStmt)           : IRElse;
public sealed record IRElseBlock(IReadOnlyList<IRStmt> Body) : IRElse;

public sealed record IRForStmt(string Var, IRExpr Iter, IReadOnlyList<IRStmt> Body) : IRStmt;
public sealed record IREveryStmt(IRExpr Interval, IReadOnlyList<IRStmt> Body) : IRStmt;
public sealed record IRLoopStmt(IReadOnlyList<IRStmt> Body) : IRStmt;

public sealed record IRMatchStmt(IRExpr Subject, IReadOnlyList<IRMatchArm> Arms) : IRStmt;

public sealed record IRMatchArm(
    IRPattern             Pattern,
    IReadOnlyList<IRStmt> Body,
    IRExpr?               BodyExpr);

// ─────────────────── Patterns ────────────────────────────────────────────────

public abstract record IRPattern;
public sealed record IRWildcardPattern                                         : IRPattern;
public sealed record IRLiteralPattern(IRValue Literal)                        : IRPattern;
public sealed record IRBindingPattern(string Name)                             : IRPattern;
public sealed record IREnumVariantPattern(
    string TypeName, string Variant,
    IReadOnlyList<IRPattern> Fields)
    : IRPattern;

// ─────────────────── Expressions ─────────────────────────────────────────────

public abstract record IRExpr;

public sealed record IRLiteralExpr(IRValue Value) : IRExpr;
public sealed record IRIdentExpr(string Name)     : IRExpr;
public sealed record IRConfigAccessExpr(string? Field) : IRExpr;
public sealed record IRBinaryExpr(string Op, IRExpr Left, IRExpr Right) : IRExpr;
public sealed record IRUnaryExpr(string Op, IRExpr Operand) : IRExpr;
public sealed record IRCallExpr(IRExpr Callee, IReadOnlyList<IRCallArg> Args) : IRExpr;
public sealed record IRFieldAccessExpr(IRExpr Object, string Field) : IRExpr;
public sealed record IRIndexExpr(IRExpr Object, IRExpr Index) : IRExpr;
public sealed record IREnumAccessExpr(string TypeName, string Variant) : IRExpr;
public sealed record IRTernaryExpr(IRExpr Cond, IRExpr Then, IRExpr Else) : IRExpr;
public sealed record IRClosureExpr(
    IReadOnlyList<IRParam> Params,
    IRType?                ReturnType,
    IReadOnlyList<IRStmt>  Body)
    : IRExpr;

/// <summary>Wywołanie send / recv jako wyrażenie.</summary>
public sealed record IRSendExpr(IRExpr Value, IRExpr? Target) : IRExpr;
public sealed record IRRecvExpr(IRExpr Source) : IRExpr;
public sealed record IRAwaitExpr(IRExpr Inner) : IRExpr;

/// <summary>Argument wywołania: pozycyjny lub nazwany.</summary>
public sealed record IRCallArg(string? Name, IRExpr Value);

// ─────────────────── Values (literals) ──────────────────────────────────────

/// <summary>Wartość literalna w IR — bez żadnej logiki.</summary>
public abstract record IRValue;

public sealed record IRIntValue(long Value)     : IRValue;
public sealed record IRFloatValue(double Value) : IRValue;
public sealed record IRBoolValue(bool Value)    : IRValue;
public sealed record IRTextValue(string Value)  : IRValue;
/// <summary>Wartość z jednostką: 5s, 32KB, 100ms …</summary>
public sealed record IRUnitValue(double Magnitude, string Unit) : IRValue;
public sealed record IRNullValue                                  : IRValue;
