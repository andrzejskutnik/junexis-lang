namespace JunexisLang.Compiler.CodeGen;

using System.Text.Json;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

internal static class StorageContractManifestGenerator
{
    internal static GeneratedFile? Generate(IRDocument ir, string platform)
    {
        var providers = IRStorageContractProjection.Project(ir)
            .Where(contract => contract.Target is null ||
                contract.Target.Equals(platform, StringComparison.OrdinalIgnoreCase))
            .Select(contract => new
            {
                name = contract.Name,
                binding = new
                {
                    target = contract.Target,
                    language = contract.Language,
                    file = contract.UserFile
                },
                operations = contract.Operations.Select(operation => new
                {
                    name = operation.Name,
                    kind = ToWireName(operation.Kind),
                    effect = ToWireName(operation.Effect),
                    parameters = operation.Params.Select(parameter => new
                    {
                        name = parameter.Name,
                        type = FormatType(parameter.Type)
                    }),
                    returns = operation.ReturnType is null ? null : FormatType(operation.ReturnType)
                })
            })
            .ToList();

        if (providers.Count == 0)
            return null;

        var manifest = new
        {
            schemaVersion = IRStorageContractProjection.SchemaVersion,
            kind = "junexis.storage.contracts",
            target = platform,
            result = new
            {
                states = new[] { "ok", "error" },
                errorReference = "errors[].code",
                unknownOutcomeRequiresReconciliation = true
            },
            lifecycle = new
            {
                initialState = "closed",
                states = new[] { "closed", "open", "transaction-active", "closing" },
                rules = new[]
                {
                    "operations require open state",
                    "close waits for or cancels in-flight operations",
                    "close rolls back an active transaction"
                }
            },
            transaction = new
            {
                boundaries = new[] { "begin", "commit", "rollback" },
                outcomes = new[] { "committed", "rolled-back", "unknown" },
                nesting = "not-supported",
                concurrency = "one-active-transaction-per-handle"
            },
            timeoutAndCancellation = new
            {
                timeout = "bounded-by-provider-configuration",
                cancellation = "cooperative",
                interruptedCommitMayReturn = "unknown-outcome"
            },
            errors = StandardErrors,
            guarantees = new[]
            {
                "parameterized-values",
                "confirmed-write-is-durable",
                "explicit-transaction-boundary",
                "immutable-applied-migration",
                "bounded-lock-wait",
                "cancellation-aware",
                "secret-safe-telemetry"
            },
            runtimeProviders = new object[]
            {
                new
                {
                    id = "sqlite",
                    durability = "persistent",
                    migrations = "sha256-versioned",
                    domainContract = "junexis.storage.Store"
                },
                new
                {
                    id = "memory",
                    durability = "volatile",
                    migrations = "not-applicable",
                    domainContract = "junexis.storage.Store"
                },
                new
                {
                    id = "postgresql",
                    durability = "remote-persistent",
                    migrations = "sha256-versioned",
                    domainContract = "junexis.storage.Store"
                }
            },
            providers
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        return new GeneratedFile(platform, $"{platform}/storage/junexis.storage.contracts.json", json);
    }

    private static readonly object[] StandardErrors =
    [
        Error("not-found", false, "known"),
        Error("conflict", false, "known"),
        Error("constraint", false, "known"),
        Error("busy", true, "known"),
        Error("timeout", true, "unknown"),
        Error("cancelled", true, "unknown"),
        Error("migration", false, "known"),
        Error("unavailable", true, "known"),
        Error("unknown-outcome", false, "unknown"),
        Error("internal", false, "unknown")
    ];

    private static object Error(string code, bool retryable, string outcome) => new { code, retryable, outcome };

    private static string ToWireName<T>(T value) where T : struct, Enum =>
        value.ToString().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();

    private static string FormatType(IRType type) => type switch
    {
        IRNamedType named => named.Name,
        IRCommType comm => $"{comm.Kind.ToString().ToLowerInvariant()}<{FormatType(comm.Element)}>",
        IRMapType map => $"Map<{FormatType(map.Key)}, {FormatType(map.Value)}>",
        IRGenericType generic => $"{generic.Name}<{string.Join(", ", generic.TypeArgs.Select(FormatType))}>",
        IRArrayType array => $"[{FormatType(array.Element)}]",
        IRUnionType union => string.Join(" | ", union.Variants.Select(FormatType)),
        _ => type.ToString() ?? "unknown"
    };
}
