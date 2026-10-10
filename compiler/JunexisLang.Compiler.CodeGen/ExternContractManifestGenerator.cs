namespace JunexisLang.Compiler.CodeGen;

using System.Text;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

internal static class ExternContractManifestGenerator
{
    internal static GeneratedFile? Generate(IRDocument ir, string platform)
    {
        var contracts = ir.ExternContracts
            .Where(contract => contract.Target is null ||
                contract.Target.Equals(platform, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (contracts.Count == 0)
            return null;

        var toml = new StringBuilder();
        toml.AppendLine("schemaVersion = \"0.2\"");
        toml.AppendLine("kind = \"junexis.externs\"");
        toml.AppendLine($"target = \"{Escape(platform)}\"");
        toml.AppendLine();
        toml.AppendLine("[ownership]");
        toml.AppendLine("generated = \"Generator-owned files may be overwritten.\"");
        toml.AppendLine("user = \"Files referenced by extern.file are developer-owned and must not be overwritten by the generator.\"");

        foreach (var contract in contracts)
        {
            toml.AppendLine();
            toml.AppendLine("[[externs]]");
            toml.AppendLine($"role = \"{Escape(contract.Role)}\"");
            toml.AppendLine($"name = \"{Escape(contract.Name)}\"");
            if (contract.Target is not null)
                toml.AppendLine($"target = \"{Escape(contract.Target)}\"");
            if (contract.Language is not null)
                toml.AppendLine($"language = \"{Escape(contract.Language)}\"");
            if (contract.UserFile is not null)
                toml.AppendLine($"file = \"{Escape(contract.UserFile)}\"");

            foreach (var function in contract.Functions)
            {
                toml.AppendLine();
                toml.AppendLine("[[externs.functions]]");
                toml.AppendLine($"name = \"{Escape(function.Name)}\"");
                if (function.ReturnType is not null)
                    toml.AppendLine($"returns = \"{Escape(FormatType(function.ReturnType))}\"");

                foreach (var parameter in function.Params)
                {
                    toml.AppendLine();
                    toml.AppendLine("[[externs.functions.parameters]]");
                    toml.AppendLine($"name = \"{Escape(parameter.Name)}\"");
                    toml.AppendLine($"type = \"{Escape(FormatType(parameter.Type))}\"");
                }
            }
        }

        return new GeneratedFile(platform, $"{platform}/externs/junexis.externs.toml", toml.ToString());
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

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
