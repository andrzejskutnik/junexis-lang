using System.Text;
using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class ExternScaffoldHandler
{
    internal static async Task<int> HandleAsync(string? inputPath, bool dryRun)
    {
        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(inputPath);
        }
        catch (CliInputException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        if (resolved.Project is null)
        {
            await Console.Error.WriteLineAsync("error: extern scaffold requires a Junexis project with junexis.project.toml");
            return 1;
        }

        var parse = await new JunexisCompiler().ParseAsync(new ParseOptions(resolved.SourceFile.FullName));
        if (!parse.Success || parse.Ast is null)
        {
            foreach (var diagnostic in parse.Diagnostics)
                await Console.Error.WriteLineAsync($"error: {diagnostic.Code}: {diagnostic.Message}");
            return 1;
        }

        var project = resolved.Project;
        var root = Normalize(project.RootDirectory.FullName);
        var outDir = Normalize(project.OutDirectory.FullName);
        var contracts = parse.Ast.Declarations
            .OfType<ExternContractNode>()
            .Where(contract => !string.IsNullOrWhiteSpace(contract.UserFile))
            .GroupBy(contract => contract.UserFile!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (contracts.Count == 0)
        {
            Console.WriteLine("no extern files to scaffold");
            return 0;
        }

        var created = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var group in contracts)
        {
            var declaredPath = group.Key.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.IsPathRooted(declaredPath)
                ? Path.GetFullPath(declaredPath)
                : Path.GetFullPath(Path.Combine(project.RootDirectory.FullName, declaredPath));
            var normalized = Normalize(fullPath);

            if (!normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync($"error: refusing extern scaffold outside project root: {group.Key}");
                errors++;
                continue;
            }

            if (normalized.Equals(outDir, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(outDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync($"error: refusing extern scaffold into generated outDir: {group.Key}");
                errors++;
                continue;
            }

            if (File.Exists(normalized))
            {
                Console.WriteLine($"skip existing {group.Key}");
                skipped++;
                continue;
            }

            var language = group.FirstOrDefault(contract =>
                    !string.IsNullOrWhiteSpace(contract.Language))?.Language ?? "";
            var content = language.ToLowerInvariant() switch
            {
                "go" => GenerateGoScaffold(group),
                "cpp" or "c++" => GenerateCppScaffold(group),
                _ => null
            };

            if (content is null)
            {
                Console.WriteLine($"skip unsupported extern language '{language}' for {group.Key}");
                skipped++;
                continue;
            }

            if (dryRun)
            {
                Console.WriteLine($"would create {group.Key}");
                created++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(normalized)!);
            await File.WriteAllTextAsync(normalized, content);
            Console.WriteLine($"created {group.Key}");
            created++;
        }

        Console.WriteLine($"extern scaffold: created={created}, skipped={skipped}, errors={errors}");
        return errors == 0 ? 0 : 1;
    }

    private static string GenerateGoScaffold(IEnumerable<ExternContractNode> contracts)
    {
        var contractList = contracts.ToList();
        var sb = new StringBuilder();
        sb.AppendLine("// Code below is developer-owned extern implementation scaffold.");
        sb.AppendLine("// Junexis generated this file once and will not overwrite it.");
        sb.AppendLine("// Fill TODO sections and keep this file under source control.");
        sb.AppendLine();
        sb.AppendLine("package user");
        sb.AppendLine();
        if (contractList.SelectMany(contract => contract.Functions).Any(UsesTimePackage))
        {
            sb.AppendLine("import \"time\"");
            sb.AppendLine();
        }

        foreach (var contract in contractList)
        {
            sb.AppendLine($"// {contract.Role} {contract.Name}");
            foreach (var function in contract.Functions)
            {
                var externName = $"{contract.Name}_{function.Name}";
                var parameters = string.Join(", ", function.Params.Select(parameter =>
                    $"{SanitizeGoIdentifier(parameter.Name)} {ToGoType(parameter.Type)}"));
                var returnType = function.ReturnType is null ? "" : $" {ToGoType(function.ReturnType)}";
                sb.AppendLine($"func {externName}({parameters}){returnType} {{");
                sb.AppendLine($"\t// TODO: implement {externName}");
                if (function.ReturnType is not null)
                    sb.AppendLine($"\treturn {ZeroValue(function.ReturnType)}");
                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string GenerateCppScaffold(IEnumerable<ExternContractNode> contracts)
    {
        var contractList = contracts.ToList();
        var sb = new StringBuilder();
        sb.AppendLine("// Code below is developer-owned extern implementation scaffold.");
        sb.AppendLine("// Junexis generated this file once and will not overwrite it.");
        sb.AppendLine("// Fill TODO sections and keep this file under source control.");
        if (contractList.Any(contract => contract.Role.Equals("motion", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("// Motion externs are safety-critical: validate units, bounds, limit switches,");
            sb.AppendLine("// emergency-stop behavior and controller state before driving real hardware.");
        }
        sb.AppendLine();
        sb.AppendLine("#include <cstdint>");
        sb.AppendLine("#include <string>");
        sb.AppendLine();
        sb.AppendLine("// TODO: include generated Junexis types header when wiring this scaffold");
        sb.AppendLine("// into the embedded build, for example:");
        sb.AppendLine("// #include \"junexis_types.h\"");
        sb.AppendLine();

        foreach (var contract in contractList)
        {
            sb.AppendLine($"// {contract.Role} {contract.Name}");
            foreach (var function in contract.Functions)
            {
                var externName = $"{contract.Name}_{function.Name}";
                var returnType = function.ReturnType is null ? "void" : ToCppType(function.ReturnType);
                var parameters = string.Join(", ", function.Params.Select(parameter =>
                    $"{ToCppType(parameter.Type)} {SanitizeCppIdentifier(parameter.Name)}"));
                sb.AppendLine($"{returnType} {externName}({parameters}) {{");
                sb.AppendLine($"    // TODO: implement {externName}");
                if (contract.Role.Equals("motion", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("    // TODO: enforce motion safety policy before touching actuators.");
                if (function.ReturnType is not null)
                    sb.AppendLine($"    return {CppZeroValue(function.ReturnType)};");
                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static bool UsesTimePackage(ExternContractFunctionNode function) =>
        function.Params.Any(parameter => UsesTimePackage(parameter.Type)) ||
        function.ReturnType is not null && UsesTimePackage(function.ReturnType);

    private static bool UsesTimePackage(TypeRefNode type) => type switch
    {
        NamedTypeNode { Name: "Time" or "Duration" } => true,
        GenericTypeNode generic => generic.TypeArgs.Any(UsesTimePackage),
        ArrayTypeNode array => UsesTimePackage(array.Element),
        CommTypeNode comm => UsesTimePackage(comm.Element),
        UnionTypeNode union => union.Variants.Any(UsesTimePackage),
        MapTypeNode map => UsesTimePackage(map.Key) || UsesTimePackage(map.Value),
        _ => false
    };

    private static string ToGoType(TypeRefNode type) => type switch
    {
        NamedTypeNode { Name: "Int" } => "int64",
        NamedTypeNode { Name: "Float" } => "float64",
        NamedTypeNode { Name: "Bool" } => "bool",
        NamedTypeNode { Name: "Text" } => "string",
        NamedTypeNode { Name: "Time" } => "time.Time",
        NamedTypeNode { Name: "Duration" } => "time.Duration",
        NamedTypeNode { Name: "f32" } => "float32",
        NamedTypeNode { Name: "f64" } => "float64",
        NamedTypeNode { Name: "i8" } => "int8",
        NamedTypeNode { Name: "i16" } => "int16",
        NamedTypeNode { Name: "i32" } => "int32",
        NamedTypeNode { Name: "i64" } => "int64",
        NamedTypeNode { Name: "u8" } => "uint8",
        NamedTypeNode { Name: "u16" } => "uint16",
        NamedTypeNode { Name: "u32" } => "uint32",
        NamedTypeNode { Name: "u64" } => "uint64",
        NamedTypeNode => "any",
        GenericTypeNode { Name: "List", TypeArgs.Count: 1 } list => $"[]{ToGoType(list.TypeArgs[0])}",
        ArrayTypeNode array => $"[]{ToGoType(array.Element)}",
        CommTypeNode { Kind: CommKind.Chan, Element: var element } => $"chan {ToGoType(element)}",
        CommTypeNode { Kind: CommKind.Queue } => "interface{}",
        CommTypeNode { Kind: CommKind.Stream } => "interface{}",
        _ => "interface{}"
    };

    private static string ToCppType(TypeRefNode type) => type switch
    {
        NamedTypeNode { Name: "Int" } => "int64_t",
        NamedTypeNode { Name: "Float" } => "double",
        NamedTypeNode { Name: "Bool" } => "bool",
        NamedTypeNode { Name: "Text" } => "std::string",
        NamedTypeNode { Name: "Time" or "Duration" } => "int64_t",
        NamedTypeNode { Name: "f32" } => "float",
        NamedTypeNode { Name: "f64" } => "double",
        NamedTypeNode { Name: "i8" } => "int8_t",
        NamedTypeNode { Name: "i16" } => "int16_t",
        NamedTypeNode { Name: "i32" } => "int32_t",
        NamedTypeNode { Name: "i64" } => "int64_t",
        NamedTypeNode { Name: "u8" } => "uint8_t",
        NamedTypeNode { Name: "u16" } => "uint16_t",
        NamedTypeNode { Name: "u32" } => "uint32_t",
        NamedTypeNode { Name: "u64" } => "uint64_t",
        NamedTypeNode named => named.Name,
        GenericTypeNode => "void*",
        ArrayTypeNode => "void*",
        CommTypeNode => "void*",
        _ => "void*"
    };

    private static string ZeroValue(TypeRefNode type) => type switch
    {
        NamedTypeNode { Name: "Bool" } => "false",
        NamedTypeNode { Name: "Text" } => "\"\"",
        NamedTypeNode { Name: "Int" or "Float" or "f32" or "f64" or "i8" or "i16" or "i32" or "i64" or "u8" or "u16" or "u32" or "u64" } => "0",
        GenericTypeNode { Name: "List" } => "nil",
        ArrayTypeNode => "nil",
        CommTypeNode => "nil",
        _ => "nil"
    };

    private static string CppZeroValue(TypeRefNode type) => type switch
    {
        NamedTypeNode { Name: "Bool" } => "false",
        NamedTypeNode { Name: "Text" } => "std::string{}",
        NamedTypeNode { Name: "Int" or "Float" or "Time" or "Duration" or "f32" or "f64" or "i8" or "i16" or "i32" or "i64" or "u8" or "u16" or "u32" or "u64" } => "0",
        NamedTypeNode named => $"{named.Name}{{}}",
        _ => "nullptr"
    };

    private static string SanitizeGoIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "value";

        var builder = new StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var c = name[index];
            if (index == 0 && char.IsDigit(c))
                builder.Append('_');
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

    private static string SanitizeCppIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "value";

        var builder = new StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var c = name[index];
            if (index == 0 && char.IsDigit(c))
                builder.Append('_');
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
