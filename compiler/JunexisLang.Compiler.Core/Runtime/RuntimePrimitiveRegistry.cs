namespace JunexisLang.Compiler.Core.Runtime;

/// <summary>
/// Lifecycle state of a Junexis runtime primitive.
/// </summary>
public enum RuntimePrimitiveStatus
{
    /// <summary>The primitive is accepted by semantic analysis for Junexis 0.2.</summary>
    Supported,

    /// <summary>The primitive is intentionally known, but its runtime/codegen contract is not complete yet.</summary>
    Proposed
}

/// <summary>
/// Junexis execution targets a primitive can conceptually run on.
/// </summary>
[Flags]
public enum RuntimePrimitiveTarget
{
    None = 0,
    Server = 1 << 0,
    Report = 1 << 1,
    Embedded = 1 << 2,
    All = Server | Report | Embedded
}

/// <summary>
/// Concrete implementation surfaces known today.
/// </summary>
[Flags]
public enum RuntimePrimitiveImplementation
{
    None = 0,
    GoCodegen = 1 << 0,
    TypeScriptCodegen = 1 << 1,
    CppCodegen = 1 << 2,
    DotNetRuntime = 1 << 3,
    EmbeddedCRuntime = 1 << 4,
    EmbeddedRustRuntime = 1 << 5
}

/// <summary>
/// Language surface where a primitive is valid.
/// </summary>
[Flags]
public enum RuntimePrimitiveSurface
{
    None = 0,
    Function = 1 << 0,
    Statement = 1 << 1,
    Expression = 1 << 2
}

/// <summary>
/// Describes a runtime primitive known to the compiler.
/// </summary>
public sealed record RuntimePrimitive(
    string Name,
    RuntimePrimitiveStatus Status,
    RuntimePrimitiveTarget Targets,
    RuntimePrimitiveImplementation Implementations,
    RuntimePrimitiveSurface Surface,
    string Summary)
{
    public bool SupportsTarget(RuntimePrimitiveTarget target)
        => target != RuntimePrimitiveTarget.None && (Targets & target) == target;

    public bool HasImplementation(RuntimePrimitiveImplementation implementation)
        => implementation != RuntimePrimitiveImplementation.None && (Implementations & implementation) == implementation;

    public bool SupportsSurface(RuntimePrimitiveSurface surface)
        => surface != RuntimePrimitiveSurface.None && (Surface & surface) == surface;
}

/// <summary>
/// Single source of truth for runtime primitive names accepted or recognized by the compiler.
/// </summary>
public static class RuntimePrimitiveRegistry
{
    private static readonly IReadOnlyDictionary<string, RuntimePrimitive> s_primitives =
        new Dictionary<string, RuntimePrimitive>(StringComparer.OrdinalIgnoreCase)
        {
            // Core time / scheduling
            ["now"] = Supported("now", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Returns the current platform time."),
            ["epoch"] = Supported("epoch", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Returns the platform epoch/zero time."),
            ["sleep"] = Supported("sleep", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Embedded, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.CppCodegen | RuntimePrimitiveImplementation.EmbeddedCRuntime, "Suspends execution for a duration."),

            // Logging / diagnostics
            ["println"] = Supported("println", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Writes a value to the platform console."),
            ["print"] = Supported("print", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Writes a value to the platform console."),
            ["log"] = Supported("log", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen | RuntimePrimitiveImplementation.DotNetRuntime, "Emits an observable log/narrative event."),

            // Collections / runtime helpers
            ["len"] = Supported("len", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Returns collection length."),
            ["append"] = Supported("append", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen, "Appends a value to a collection."),
            ["make"] = Supported("make", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Embedded, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.CppCodegen, "Creates a runtime collection or communication object."),
            ["close"] = Supported("close", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen | RuntimePrimitiveImplementation.DotNetRuntime | RuntimePrimitiveImplementation.EmbeddedCRuntime, "Closes a runtime resource."),

            // Communication primitives
            ["send"] = Supported("send", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen | RuntimePrimitiveImplementation.DotNetRuntime | RuntimePrimitiveImplementation.EmbeddedCRuntime, "Sends a value to a communication target.", RuntimePrimitiveSurface.Statement | RuntimePrimitiveSurface.Expression),
            ["recv"] = Supported("recv", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen | RuntimePrimitiveImplementation.DotNetRuntime | RuntimePrimitiveImplementation.EmbeddedCRuntime, "Receives a value from a communication source.", RuntimePrimitiveSurface.Statement | RuntimePrimitiveSurface.Expression),
            ["recv_any"] = Supported("recv_any", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen, "Receives the first available value from multiple communication sources."),
            ["ack"] = Proposed("ack", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Queue acknowledgement candidate; C++/Go/TypeScript preserve the hidden Message ID, while the Junexis syntax remains experimental.", RuntimePrimitiveSurface.Statement),
            ["nack"] = Proposed("nack", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Queue negative acknowledgement candidate for redelivery; C++/Go/TypeScript lowering exists, while retry/DLQ policy remains experimental.", RuntimePrimitiveSurface.Statement),
            ["latest"] = Proposed("latest", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen, "Stream latest-value read candidate; Go/TypeScript lowering exists, but absence semantics are not stable yet."),
            ["batch"] = Proposed("batch", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen, "Queue batch snapshot candidate; Go/TypeScript lowering exists for queue<T>, but batching policy is still experimental."),

            // Polish aliases
            ["dlugosc"] = Supported("dlugosc", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Polish alias for len."),
            ["dodaj"] = Supported("dodaj", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen, "Polish alias for append."),
            ["pobierz"] = Proposed("pobierz", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.None, "Polish runtime alias candidate for future storage/resource read."),
            ["zapisz"] = Proposed("zapisz", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.None, "Polish runtime alias candidate for future storage/resource write."),
            ["usun"] = Proposed("usun", RuntimePrimitiveTarget.Server | RuntimePrimitiveTarget.Report, RuntimePrimitiveImplementation.None, "Polish runtime alias candidate for future storage/resource delete."),

            // Math / conversion
            ["min"] = Supported("min", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Returns the smaller value."),
            ["max"] = Supported("max", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Returns the larger value."),
            ["abs"] = Supported("abs", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Returns absolute value."),
            ["floor"] = Supported("floor", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Rounds down."),
            ["ceil"] = Supported("ceil", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Rounds up."),
            ["round"] = Supported("round", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Rounds to nearest value."),
            ["sqrt"] = Supported("sqrt", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Returns square root."),
            ["pow"] = Supported("pow", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Raises a value to a power."),
            ["to_string"] = Supported("to_string", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Converts a value to text."),
            ["to_int"] = Supported("to_int", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Converts a value to Int."),
            ["to_float"] = Supported("to_float", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Converts a value to Float."),
            ["f32"] = Supported("f32", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to f32."),
            ["f64"] = Supported("f64", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to f64."),
            ["i8"] = Supported("i8", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to i8."),
            ["i16"] = Supported("i16", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to i16."),
            ["i32"] = Supported("i32", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to i32."),
            ["i64"] = Supported("i64", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to i64."),
            ["u8"] = Supported("u8", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to u8."),
            ["u16"] = Supported("u16", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to u16."),
            ["u32"] = Supported("u32", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to u32."),
            ["u64"] = Supported("u64", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to u64."),
            ["float32"] = Supported("float32", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to f32."),
            ["float64"] = Supported("float64", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to f64."),
            ["int8"] = Supported("int8", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to i8."),
            ["int16"] = Supported("int16", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to i16."),
            ["int32"] = Supported("int32", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to i32."),
            ["int64"] = Supported("int64", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to i64."),
            ["uint8"] = Supported("uint8", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to u8."),
            ["uint16"] = Supported("uint16", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to u16."),
            ["uint32"] = Supported("uint32", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to u32."),
            ["uint64"] = Supported("uint64", RuntimePrimitiveTarget.All, RuntimePrimitiveImplementation.GoCodegen | RuntimePrimitiveImplementation.TypeScriptCodegen | RuntimePrimitiveImplementation.CppCodegen, "Casts a value to u64."),
        };

    public static bool TryGet(string name, out RuntimePrimitive primitive)
        => s_primitives.TryGetValue(name, out primitive!);

    public static bool IsSupported(string name)
        => TryGet(name, out var primitive) && primitive.Status == RuntimePrimitiveStatus.Supported;

    public static bool IsKnown(string name)
        => s_primitives.ContainsKey(name);

    public static IReadOnlyCollection<RuntimePrimitive> List()
        => s_primitives.Values.ToArray();

    private static RuntimePrimitive Supported(
        string name,
        RuntimePrimitiveTarget targets,
        RuntimePrimitiveImplementation implementations,
        string summary,
        RuntimePrimitiveSurface surface = RuntimePrimitiveSurface.Function)
        => new(name, RuntimePrimitiveStatus.Supported, targets, implementations, surface, summary);

    private static RuntimePrimitive Proposed(
        string name,
        RuntimePrimitiveTarget targets,
        RuntimePrimitiveImplementation implementations,
        string summary,
        RuntimePrimitiveSurface surface = RuntimePrimitiveSurface.Function)
        => new(name, RuntimePrimitiveStatus.Proposed, targets, implementations, surface, summary);
}
