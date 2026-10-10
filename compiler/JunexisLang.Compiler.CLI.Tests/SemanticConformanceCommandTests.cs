using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class SemanticConformanceCommandTests
{
    [Fact]
    public async Task PartialReport_IsVisibleButDoesNotFailTheCommandAsync()
    {
        var report = await WriteReportAsync("partial", 2, 1, 1, targetSatisfied: false);
        try
        {
            var result = await RunCliAsync("conformance", report);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("PARTIAL", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Non-blocking conformance gaps", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("scenario.open: S2", result.StandardOutput, StringComparison.Ordinal);
            Assert.Empty(result.StandardError);
        }
        finally
        {
            File.Delete(report);
        }
    }

    [Fact]
    public async Task JsonReport_ExposesStatusCountsAndNonBlockingMeaningAsync()
    {
        var report = await WriteReportAsync("satisfied", 1, 1, 0, targetSatisfied: true);
        try
        {
            var result = await RunCliAsync("conformance", report, "--json");

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("satisfied", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("targetSatisfiedCount").GetInt32());
            Assert.False(document.RootElement.GetProperty("blocking").GetBoolean());
        }
        finally
        {
            File.Delete(report);
        }
    }

    [Fact]
    public async Task BoundReport_ValidatesBuildBundleAndBindingHashAsync()
    {
        var report = await WriteBoundReportAsync(tamperBundle: false);
        try
        {
            var result = await RunCliAsync("conformance", report, "--json");

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal(new string('a', 64), document.RootElement.GetProperty("identity").GetProperty("buildId").GetString());
            Assert.Empty(result.StandardError);
        }
        finally
        {
            File.Delete(report);
        }
    }

    [Fact]
    public async Task TamperedBoundReport_IsRejectedAsync()
    {
        var report = await WriteBoundReportAsync(tamperBundle: true);
        try
        {
            var result = await RunCliAsync("conformance", report, "--json");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("binding hash is invalid", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(report);
        }
    }

    private static async Task<string> WriteReportAsync(
        string status,
        int scenarios,
        int satisfied,
        int gaps,
        bool targetSatisfied)
    {
        var path = Path.Combine(Path.GetTempPath(), $"junexis-conformance-{Guid.NewGuid():N}.json");
        var payload = new
        {
            schemaVersion = "junexis.semantic-conformance/0.1",
            conformanceStatus = status,
            semanticContract = "junexis.semantic-runtime/0.1",
            targetLevel = "S3",
            summary = new { scenarioCount = scenarios, targetSatisfiedCount = satisfied, gapCount = gaps },
            scenarios = new[]
            {
                new { id = targetSatisfied ? "scenario.closed" : "scenario.open", achievedLevel = targetSatisfied ? "S3" : "S2", targetLevel = "S3", targetSatisfied }
            }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload));
        return path;
    }

    private static async Task<string> WriteBoundReportAsync(bool tamperBundle)
    {
        var path = Path.Combine(Path.GetTempPath(), $"junexis-bound-conformance-{Guid.NewGuid():N}.json");
        var buildId = new string('a', 64);
        var originalBundleId = new string('b', 64);
        var bundleId = tamperBundle ? new string('c', 64) : originalBundleId;
        var ledgerSha256 = new string('d', 64);
        var material = Encoding.UTF8.GetBytes($"{buildId}\n{originalBundleId}\n{ledgerSha256}");
        var bindingSha256 = Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant();
        var payload = new
        {
            schemaVersion = "junexis.semantic-conformance/0.2",
            conformanceStatus = "satisfied",
            semanticContract = "junexis.semantic-runtime/0.1",
            targetLevel = "S3",
            ledgerSha256,
            identity = new { status = "bound", buildId, bundleId, ledgerSha256, bindingSha256, algorithm = "sha256" },
            summary = new { scenarioCount = 1, targetSatisfiedCount = 1, gapCount = 0 },
            scenarios = new[] { new { id = "scenario.closed", achievedLevel = "S3", targetLevel = "S3", targetSatisfied = true } }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload));
        return path;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(typeof(CompileHandler).Assembly.Location);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Junexis CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
    }
}
