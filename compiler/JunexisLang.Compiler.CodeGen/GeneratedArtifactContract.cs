namespace JunexisLang.Compiler.CodeGen;

using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Core;

/// <summary>Single violation of the generated artifact inventory contract.</summary>
/// <param name="Platform">Target platform whose artifact is incomplete.</param>
/// <param name="Message">Human-readable contract violation.</param>
public sealed record GeneratedArtifactContractViolation(string Platform, string Message);

/// <summary>Result of validating generated files for one target platform.</summary>
/// <param name="Violations">All detected inventory and path violations.</param>
public sealed record GeneratedArtifactContractResult(
    IReadOnlyList<GeneratedArtifactContractViolation> Violations)
{
    /// <summary>Whether the artifact satisfies the inventory contract.</summary>
    public bool Success => Violations.Count == 0;
}

/// <summary>
/// Defines the minimum self-contained file inventory emitted by each bootstrap backend.
/// Native toolchain compilation is the next acceptance layer and deliberately remains
/// separate from this deterministic, toolchain-independent contract.
/// </summary>
public static class GeneratedArtifactContract
{
    /// <summary>Validates generated files for a target platform.</summary>
    /// <param name="document">Source IR document.</param>
    /// <param name="platform">Target platform.</param>
    /// <param name="files">Files emitted by the platform generator.</param>
    /// <returns>Contract result containing every violation.</returns>
    public static GeneratedArtifactContractResult Validate(
        IRDocument document,
        string platform,
        IReadOnlyList<GeneratedFile> files)
    {
        var violations = new List<GeneratedArtifactContractViolation>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var normalized = file.FilePath.Replace('\\', '/');
            if (Path.IsPathRooted(file.FilePath) || normalized.Split('/').Contains(".."))
                violations.Add(new(platform, $"unsafe generated path '{file.FilePath}'"));
            if (!paths.Add(normalized))
                violations.Add(new(platform, $"duplicate generated path '{file.FilePath}'"));
            if (string.IsNullOrWhiteSpace(file.Content))
                violations.Add(new(platform, $"generated file '{file.FilePath}' is empty"));
        }

        foreach (var required in RequiredPaths(document, platform))
        {
            if (!paths.Contains(required))
                violations.Add(new(platform, $"missing required artifact '{required}'"));
        }

        return new GeneratedArtifactContractResult(violations);
    }

    private static IEnumerable<string> RequiredPaths(IRDocument document, string platform)
    {
        if (document.ExternContracts.Any(contract => contract.Target is null ||
            contract.Target.Equals(platform, StringComparison.OrdinalIgnoreCase)))
        {
            yield return $"{platform.ToLowerInvariant()}/externs/junexis.externs.toml";
        }

        switch (platform.ToLowerInvariant())
        {
            case "embedded":
                if (!document.Tasks.Any(IsEmbedded)) yield break;
                yield return "embedded/junexis_app.cpp";
                yield return "embedded/platformio.ini";
                yield return "embedded/junexis_types.h";
                yield return "embedded/junexis_config.h";
                yield return "embedded/junexis/runtime.h";
                foreach (var task in document.Tasks.Where(IsEmbedded))
                {
                    yield return $"embedded/{task.Name}.h";
                    yield return $"embedded/{task.Name}.cpp";
                }
                break;
            case "server":
                if (!document.Tasks.Any(IsServer)) yield break;
                yield return "server/main.go";
                yield return "server/go.mod";
                break;
            case "report":
                if (!document.Tasks.Any(IsReport)) yield break;
                yield return "report/index.ts";
                yield return "report/types.ts";
                yield return "report/runtime.ts";
                yield return "report/deploy.ts";
                yield return "report/browser.ts";
                yield return "report/report-view.ts";
                yield return "report/report-adapter-contract.ts";
                yield return "report/user-adapter.ts";
                yield return "report/report-view-contract.ts";
                yield return "report/user-view.ts";
                yield return "report/report.css";
                yield return "report/index.html";
                yield return "report/junexis-report-manifest.json";
                yield return "report/serve.mjs";
                yield return "report/package.json";
                yield return "report/tsconfig.json";
                break;
            case "proto":
                if (document.Structs.Count > 0 || document.Enums.Count > 0)
                    yield return "schema.proto";
                break;
        }
    }

    private static bool IsEmbedded(IRTask task) => HasPlatform(task, "embedded");
    private static bool IsServer(IRTask task) => HasPlatform(task, "server");
    private static bool IsReport(IRTask task) => HasPlatform(task, "report");

    private static bool HasPlatform(IRTask task, string platform) =>
        task.Platforms.Any(candidate => candidate.Target.Equals(platform, StringComparison.OrdinalIgnoreCase));
}
