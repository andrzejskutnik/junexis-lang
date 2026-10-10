using System.Text;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>
/// Obsługuje komendę "deploy": generuje docker-compose.yml na podstawie pliku .jnx.
/// Uruchamia TraceCollector (junexis-narrative) obok wygenerowanych serwisów @server.
/// </summary>
internal static class DeployHandler
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    internal static async Task<int> HandleAsync(string? inputPath, DirectoryInfo? outDir, string traceImageTag)
    {
        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(inputPath, outDir);
        }
        catch (CliInputException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        var input = resolved.SourceFile;
        var checkResult = await new JunexisCompiler()
            .CheckAsync(new CheckOptions(input.FullName));

        if (!checkResult.Success || checkResult.Ast is null)
        {
            DiagnosticFormatter.Write(checkResult.Diagnostics);
            return 1;
        }

        var structural = StructuralAnalysisStage.Analyze(checkResult.Ast);
        if (!structural.Success || structural.Document is null)
        {
            DiagnosticFormatter.Write(structural.Diagnostics);
            return 1;
        }
        DiagnosticFormatter.Write(structural.Diagnostics);
        var ir = structural.Document;
        var distributed = structural.DistributedTopology
            ?? throw new InvalidOperationException("Distributed topology lowering did not produce a plan.");

        // Katalog wyjściowy
        var baseDir = resolved.OutDirectory?.FullName
                      ?? Path.Combine(input.DirectoryName ?? ".", "out");
        Directory.CreateDirectory(baseDir);

        var composeFile = Path.Combine(baseDir, "docker-compose.yml");
        var yaml = BuildDockerCompose(ir, distributed, traceImageTag);
        await File.WriteAllTextAsync(composeFile, yaml, Utf8NoBom);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await File.WriteAllTextAsync(
            Path.Combine(baseDir, "deployment-plan.json"),
            JsonSerializer.Serialize(distributed.Placements, jsonOptions), Utf8NoBom);
        await File.WriteAllTextAsync(
            Path.Combine(baseDir, "wire-contract.json"),
            JsonSerializer.Serialize(distributed.Edges, jsonOptions), Utf8NoBom);

        await WriteServerBuildArtifactsAsync(ir, baseDir);

        Console.WriteLine($"  wrote {composeFile}");
        return 0;
    }

    private static async Task WriteServerBuildArtifactsAsync(IRDocument ir, string baseDir)
    {
        var hasServerTasks = ir.Tasks.Any(t => t.Platforms.Any(p => p.Target.Equals("server", StringComparison.OrdinalIgnoreCase)));
        if (!hasServerTasks)
            return;

        var codeGen = new GoCodeGenerator();
        var generatedFiles = codeGen.Generate(ir, new CodeGenOptions(baseDir));
        var contract = GeneratedArtifactContract.Validate(ir, codeGen.Platform, generatedFiles);
        if (!contract.Success)
            throw new InvalidOperationException(
                "Go artifact contract failed: " + string.Join("; ", contract.Violations.Select(v => v.Message)));
        var mainFile = generatedFiles.SingleOrDefault(file => file.FilePath == "server/main.go");
        var moduleFile = generatedFiles.SingleOrDefault(file => file.FilePath == "server/go.mod");
        if (mainFile is null)
            throw new InvalidOperationException("Go code generator did not produce server/main.go for deploy output.");
        if (moduleFile is null)
            throw new InvalidOperationException("Go code generator did not produce server/go.mod for deploy output.");

        var serverDir = Path.Combine(baseDir, "server");
        Directory.CreateDirectory(serverDir);

        await File.WriteAllTextAsync(Path.Combine(serverDir, "main.go"), mainFile.Content, Utf8NoBom);
        await File.WriteAllTextAsync(Path.Combine(serverDir, "go.mod"), moduleFile.Content, Utf8NoBom);
        await File.WriteAllTextAsync(Path.Combine(serverDir, "Dockerfile"), BuildDockerfile(), Utf8NoBom);
    }

    private static string BuildDockerfile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("FROM golang:1.22");
        sb.AppendLine("WORKDIR /app");
        sb.AppendLine("COPY go.mod ./");
        sb.AppendLine("COPY main.go ./");
        sb.AppendLine("RUN go mod tidy && go build -o /app/junexis-generated-server ./main.go");
        sb.AppendLine("CMD [\"/app/junexis-generated-server\"]");
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Budowanie pliku docker-compose.yml
    // ──────────────────────────────────────────────────────────────────────────

    private static string BuildDockerCompose(
        JunexisLang.Compiler.IR.IRDocument ir,
        JunexisLang.Compiler.Topology.DistributedTopologyPlan distributed,
        string traceImageTag)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Generated by: junexis deploy");
        sb.AppendLine("# Do NOT edit manually — regenerate from .jnx source.");
        sb.AppendLine();
        sb.AppendLine("services:");
        sb.AppendLine();

        // ── trace-collector (junexis-narrative) ──────────────────────────────
        sb.AppendLine("  trace-collector:");
        sb.AppendLine($"    image: {traceImageTag}");
        sb.AppendLine("    ports:");
        sb.AppendLine("      - \"8082:8080\"");
        sb.AppendLine("    healthcheck:");
        sb.AppendLine("      test: [\"CMD\", \"curl\", \"-f\", \"http://localhost:8080/health\"]");
        sb.AppendLine("      interval: 10s");
        sb.AppendLine("      retries: 5");
        sb.AppendLine();

        // ── @server tasks ─────────────────────────────────────────────────────
        var serverTasks = ir.Tasks
            .Where(t => t.Platforms.Any(p => p.Target.Equals("server", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (serverTasks.Count > 0)
        {
            // Jeden serwis Go na projekt (wszystkie taski server idą do jednego main.go)
            var serviceName = ToServiceName(Path.GetFileNameWithoutExtension(ir.SourceFile));
            sb.AppendLine($"  {serviceName}:");
            sb.AppendLine("    build:");
            sb.AppendLine("      context: ./server");
            sb.AppendLine("      dockerfile: Dockerfile");
            sb.AppendLine("    environment:");
            sb.AppendLine("      JUNEXIS_TRACE_URL: http://trace-collector:8080");
            sb.AppendLine("      JUNEXIS_WIRE_VERSION: junexis-wire/0.2");
            var deploymentName = distributed.Placements.FirstOrDefault()?.Deployment ?? "implicit";
            sb.AppendLine($"      JUNEXIS_DEPLOYMENT: {deploymentName}");
            sb.AppendLine("    depends_on:");
            sb.AppendLine("      trace-collector:");
            sb.AppendLine("        condition: service_healthy");
            var replicas = distributed.Placements
                .Where(placement => placement.Platform == "server")
                .Select(placement => placement.Instances)
                .DefaultIfEmpty(1)
                .Max();
            sb.AppendLine("    deploy:");
            sb.AppendLine($"      replicas: {replicas}");
            if (distributed.Edges.Any(edge => edge.SourcePlatform == "server" && edge.TargetPlatform == "report"))
            {
                sb.AppendLine("    ports:");
                sb.AppendLine("      - \"8080:8080\"");
                sb.AppendLine("      - \"8081:8081\"");
            }
            sb.AppendLine();
        }

        // ── wolumeny / sieci (domyślna bridge sieć) ─────────────────────────
        sb.AppendLine("networks:");
        sb.AppendLine("  default:");
        sb.AppendLine("    driver: bridge");

        return sb.ToString();
    }

    /// <summary>Konwertuje nazwę modułu na bezpieczną nazwę serwisu Docker.</summary>
    private static string ToServiceName(string name) =>
        name.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-')
            .TrimStart('-');
}
