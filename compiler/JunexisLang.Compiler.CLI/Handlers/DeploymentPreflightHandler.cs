using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>Runs technical deployment checks and an explicitly non-blocking Mentor review.</summary>
internal static class DeploymentPreflightHandler
{
    /// <summary>Evaluates an M7.2 package before any deployment side effect.</summary>
    internal static async Task<int> HandleAsync(
        FileInfo bundle,
        FileInfo? receipt,
        string[]? targets,
        string? mentorExecutable,
        string authenticityPolicy,
        FileInfo? signature,
        string[]? trustedSigners,
        FileInfo? output,
        bool json)
    {
        try
        {
            var report = await DeploymentPreflightContract.EvaluateAsync(
                bundle.FullName,
                receipt?.FullName,
                targets,
                mentorExecutable,
                authenticityPolicy,
                signature?.FullName,
                trustedSigners);
            if (output is not null)
                await DeploymentPreflightContract.WriteAtomicAsync(output.FullName, report);
            if (json)
            {
                await Console.Out.WriteLineAsync(
                    DeploymentPreflightContract.Serialize(report));
            }
            else
            {
                await WriteHumanAsync(report, output);
            }
            return report.TechnicalStatus == "ready" ? 0 : 1;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
        {
            await Console.Error.WriteLineAsync(
                $"error: deployment preflight failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task WriteHumanAsync(
        DeploymentPreflightReport report,
        FileInfo? output)
    {
        await Console.Out.WriteLineAsync(
            $"Deployment preflight 0.1: {report.TechnicalStatus.ToUpperInvariant()}");
        await Console.Out.WriteLineAsync($"Build ID: {report.BuildId}");
        await Console.Out.WriteLineAsync($"Bundle ID: {report.BundleId}");
        await Console.Out.WriteLineAsync(
            $"Authenticity: {report.Authenticity.Status.ToUpperInvariant()} · " +
            $"policy {report.Authenticity.Policy} · " +
            (report.Authenticity.PolicySatisfied ? "accepted" : "blocked"));
        if (report.Authenticity.Signer is not null)
            await Console.Out.WriteLineAsync(
                $"Signer: {report.Authenticity.Signer} · " +
                report.Authenticity.PublicKeyFingerprintSha256);
        await Console.Out.WriteLineAsync($"Targets: {string.Join(", ", report.Targets)}");
        foreach (var check in report.Checks)
        {
            var target = check.Target is null ? string.Empty : $" [{check.Target}]";
            await Console.Out.WriteLineAsync(
                $"  {check.Status.ToUpperInvariant(),-7} {check.Code}{target} · {check.Message}");
        }

        await Console.Out.WriteLineAsync(
            $"Mentor · {report.Mentor.Status} · non-blocking");
        await Console.Out.WriteLineAsync($"  {report.Mentor.Summary}");
        foreach (var finding in report.Mentor.Findings
                     .Where(item => item.Severity is "error" or "warning")
                     .Take(3))
        {
            await Console.Out.WriteLineAsync(
                $"  Mentor · {finding.Code} · {finding.Severity}: {finding.Message}");
        }
        if (output is not null)
            await Console.Out.WriteLineAsync($"Report: {output.FullName}");
    }
}
