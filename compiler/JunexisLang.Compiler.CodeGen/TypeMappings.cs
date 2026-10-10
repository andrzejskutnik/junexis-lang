namespace JunexisLang.Compiler.CodeGen;

using JunexisLang.Compiler.IR;

/// <summary>Mapowanie IRType na typ w języku docelowym.</summary>
internal static class TypeMappings
{
    // ─── C++ ─────────────────────────────────────────────────────────────────

    public static string ToCpp(IRType t) => t switch
    {
        IRNamedType { Name: "Int"   }             => "int32_t",
        IRNamedType { Name: "Float" }             => "float",
        IRNamedType { Name: "Bool"  }             => "bool",
        IRNamedType { Name: "Text"  }             => "std::string",
        IRNamedType { Name: "Time" or "Duration" } => "int64_t",
        IRNamedType { Name: "f32"   }             => "float",
        IRNamedType { Name: "f64"   }             => "double",
        IRNamedType { Name: "i8"    }             => "int8_t",
        IRNamedType { Name: "i16"   }             => "int16_t",
        IRNamedType { Name: "i32"   }             => "int32_t",
        IRNamedType { Name: "i64"   }             => "int64_t",
        IRNamedType { Name: "u8"    }             => "uint8_t",
        IRNamedType { Name: "u16"   }             => "uint16_t",
        IRNamedType { Name: "u32"   }             => "uint32_t",
        IRNamedType { Name: "u64"   }             => "uint64_t",
        IRNamedType n                             => n.Name,
        IRCommType { Kind: IRCommKind.Chan,   Element: var el } => ChanRuntimeContracts.ForCpp(el).HandleType,
        IRCommType { Kind: IRCommKind.Queue,  Element: var el } => QueueRuntimeContracts.ForCpp(el).HandleType,
        IRCommType { Kind: IRCommKind.Stream, Element: var el } => StreamRuntimeContracts.ForCpp(el).HandleType,
        IRArrayType n                             => $"std::vector<{ToCpp(n.Element)}>",
        IRUnionType n                             => $"std::variant<{string.Join(", ", n.Variants.Select(ToCpp))}>",
        _                                         => "void"
    };

    // ─── Go ──────────────────────────────────────────────────────────────────

    public static string ToGo(IRType t) => t switch
    {
        IRNamedType { Name: "Int"   }             => "int64",
        IRNamedType { Name: "Float" }             => "float64",
        IRNamedType { Name: "Bool"  }             => "bool",
        IRNamedType { Name: "Text"  }             => "string",
        IRNamedType { Name: "Bytes" }             => "[]byte",
        IRNamedType { Name: "Decimal" or "Uuid" } => "string",
        IRNamedType { Name: "Time"  }             => "time.Time",
        IRNamedType { Name: "Duration" }          => "time.Duration",
        IRNamedType { Name: "f32"   }             => "float32",
        IRNamedType { Name: "f64"   }             => "float64",
        IRNamedType { Name: "i8"    }             => "int8",
        IRNamedType { Name: "i16"   }             => "int16",
        IRNamedType { Name: "i32"   }             => "int32",
        IRNamedType { Name: "i64"   }             => "int64",
        IRNamedType { Name: "u8"    }             => "uint8",
        IRNamedType { Name: "u16"   }             => "uint16",
        IRNamedType { Name: "u32"   }             => "uint32",
        IRNamedType { Name: "u64"   }             => "uint64",
        IRNamedType n                             => n.Name,
        IRGenericType { Name: "List", TypeArgs.Count: 1 } n => $"[]{ToGo(n.TypeArgs[0])}",
        IRCommType { Kind: IRCommKind.Chan,   Element: var el } => ChanRuntimeContracts.ForGo(el).HandleType,
        IRCommType { Kind: IRCommKind.Queue,  Element: var el } => QueueRuntimeContracts.ForGo(el).HandleType,
        IRCommType { Kind: IRCommKind.Stream, Element: var el } => StreamRuntimeContracts.ForGo(el).HandleType,
        IRArrayType n                             => $"[]{ToGo(n.Element)}",
        _                                         => "interface{}"
    };

    // ─── TypeScript ───────────────────────────────────────────────────────────

    public static string ToTypeScript(IRType t) => t switch
    {
        IRNamedType { Name: "Int"   }             => "number",
        IRNamedType { Name: "Float" }             => "number",
        IRNamedType { Name: "Bool"  }             => "boolean",
        IRNamedType { Name: "Text"  }             => "string",
        IRNamedType { Name: "Time" or "Duration" } => "number",
        IRNamedType { Name: "f32" or "f64" }      => "number",
        IRNamedType { Name: "i8" or "i16" or "i32" or "i64" } => "number",
        IRNamedType { Name: "u8" or "u16" or "u32" or "u64" } => "number",
        IRNamedType n                             => n.Name,
        IRGenericType { Name: "List", TypeArgs.Count: 1 } n => $"{ToTypeScript(n.TypeArgs[0])}[]",
        IRCommType { Kind: IRCommKind.Chan,   Element: var el } => ChanRuntimeContracts.ForTypeScript(el).HandleType,
        IRCommType { Kind: IRCommKind.Queue,  Element: var el } => QueueRuntimeContracts.ForTypeScript(el).HandleType,
        IRCommType { Kind: IRCommKind.Stream, Element: var el } => StreamRuntimeContracts.ForTypeScript(el).HandleType,
        IRArrayType n                             => $"{ToTypeScript(n.Element)}[]",
        IRUnionType n                             => string.Join(" | ", n.Variants.Select(ToTypeScript)),
        _                                         => "unknown"
    };
}
