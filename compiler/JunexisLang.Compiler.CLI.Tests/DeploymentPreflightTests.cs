using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class DeploymentPreflightTests
{
    [Fact]
    [Trait("Issue", "39")]
    public async Task Preflight_VerifiedPackageAndMatchingMentor_IsReady()
    {
        var fixture = await CreatePackagedProjectAsync();
        try
        {
            var bundle = await SystemBundleContract.VerifyAsync(fixture.BundlePath);
            var mentor = await CreateFakeMentorAsync(
                fixture.Root,
                bundle.Descriptor.BuildId);

            var report = await DeploymentPreflightContract.EvaluateAsync(
                fixture.BundlePath,
                null,
                ["server"],
                mentor);

            Assert.Equal("ready", report.TechnicalStatus);
            Assert.Equal("reviewed", report.Mentor.Status);
            Assert.True(report.Mentor.NonBlocking);
            var finding = Assert.Single(report.Mentor.Findings);
            Assert.Equal("M1001", finding.Code);
            Assert.Contains(report.Checks, check =>
                check.Code == "DP0002" && check.Status == "passed");
            Assert.DoesNotContain(report.Checks, check => check.Status == "blocked");
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "39")]
    public async Task Preflight_MissingPackageReceipt_IsBlocked()
    {
        var fixture = await CreatePackagedProjectAsync();
        try
        {
            File.Delete(PackageReceiptContract.DefaultPath(fixture.BundlePath));

            var report = await DeploymentPreflightContract.EvaluateAsync(
                fixture.BundlePath,
                null,
                ["server"],
                null);

            Assert.Equal("blocked", report.TechnicalStatus);
            Assert.Contains(report.Checks, check =>
                check.Code == "DP1001" && check.Status == "blocked");
            Assert.Equal("not-configured", report.Mentor.Status);
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "39")]
    public async Task Preflight_ReceiptForDifferentBundle_IsBlocked()
    {
        var fixture = await CreatePackagedProjectAsync();
        try
        {
            var receiptPath = PackageReceiptContract.DefaultPath(fixture.BundlePath);
            var receipt = await PackageReceiptContract.ReadAsync(receiptPath);
            await File.WriteAllTextAsync(
                receiptPath,
                JsonSerializer.Serialize(
                    receipt with { BundleId = new string('b', 64) }));

            var report = await DeploymentPreflightContract.EvaluateAsync(
                fixture.BundlePath,
                null,
                ["server"],
                null);

            Assert.Equal("blocked", report.TechnicalStatus);
            Assert.Contains(report.Checks, check =>
                check.Code == "DP1001"
                && check.Message.Contains(
                    "does not belong",
                    StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "39")]
    public async Task Preflight_StaleMentorOpinion_DoesNotBlockTechnicalReadiness()
    {
        var fixture = await CreatePackagedProjectAsync();
        try
        {
            var mentor = await CreateFakeMentorAsync(
                fixture.Root,
                new string('a', 64));

            var report = await DeploymentPreflightContract.EvaluateAsync(
                fixture.BundlePath,
                null,
                ["server"],
                mentor);

            Assert.Equal("ready", report.TechnicalStatus);
            Assert.Equal("stale", report.Mentor.Status);
            Assert.Empty(report.Mentor.Findings);
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "39")]
    public async Task Preflight_TargetOutsideBundle_IsBlocked()
    {
        var fixture = await CreatePackagedProjectAsync();
        try
        {
            var report = await DeploymentPreflightContract.EvaluateAsync(
                fixture.BundlePath,
                null,
                ["report"],
                null);

            Assert.Equal("blocked", report.TechnicalStatus);
            Assert.Contains(report.Checks, check =>
                check.Code == "DP1002"
                && check.Target == "report"
                && check.Status == "blocked");
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "39")]
    public async Task DeployPreflightCommand_EmitsJsonThroughProcessBoundary()
    {
        var fixture = await CreatePackagedProjectAsync();
        try
        {
            var bundle = await SystemBundleContract.VerifyAsync(fixture.BundlePath);
            var mentor = await CreateFakeMentorAsync(
                fixture.Root,
                bundle.Descriptor.BuildId);
            var reportPath = Path.Combine(fixture.Root, "deployment-preflight.json");

            var result = await RunCliAsync(
                [
                    "deploy", "preflight", fixture.BundlePath,
                    "--target", "server",
                    "--mentor-exe", mentor,
                    "--report-out", reportPath,
                    "--json"
                ],
                fixture.Root);

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal(
                DeploymentPreflightReport.CurrentVersion,
                document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(
                "ready",
                document.RootElement.GetProperty("technicalStatus").GetString());
            Assert.Equal(
                "reviewed",
                document.RootElement.GetProperty("mentor").GetProperty("status").GetString());
            Assert.Equal(
                "unsigned",
                document.RootElement.GetProperty("authenticity").GetProperty("status").GetString());
            Assert.True(
                document.RootElement.GetProperty("authenticity").GetProperty("policySatisfied").GetBoolean());
            Assert.True(File.Exists(reportPath));
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.9")]
    public async Task Preflight_TrustedPolicy_RequiresExplicitTrustedSigner()
    {
        var fixture = await CreatePackagedProjectAsync();
        var keyPath = Path.Combine(fixture.Root, "release-key.pem");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            await File.WriteAllTextAsync(keyPath, key.ExportECPrivateKeyPem());
            var signed = await SystemBundleAuthenticityContract.SignAsync(
                fixture.BundlePath,
                keyPath,
                "Junexis release");

            var untrusted = await DeploymentPreflightContract.EvaluateAsync(
                fixture.BundlePath,
                null,
                ["server"],
                null,
                authenticityPolicy: "trusted");
            var trusted = await DeploymentPreflightContract.EvaluateAsync(
                fixture.BundlePath,
                null,
                ["server"],
                null,
                authenticityPolicy: "trusted",
                trustedSignerFingerprints: [signed.Signature.PublicKeyFingerprintSha256]);

            Assert.Equal("blocked", untrusted.TechnicalStatus);
            Assert.Equal("verified", untrusted.Authenticity.Status);
            Assert.Contains(untrusted.Checks, check => check.Code == "DP1006");
            Assert.Equal("ready", trusted.TechnicalStatus);
            Assert.Equal("trusted", trusted.Authenticity.Status);
            Assert.Contains(trusted.Checks, check => check.Code == "DP0005");
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    private static async Task<(string Root, string BundlePath)> CreatePackagedProjectAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"junexis-deployment-preflight-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "m7-deployment"
            entry = "src/main.jnx"
            targets = ["server"]
            outDir = "out"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            """
            task Main() @server {
                log("M7 deployment preflight");
            }

            pipeline MainPipeline {
                use Main;
            }
            """);
        var bundlePath = Path.Combine(root, "m7-deployment.system-bundle.zip");
        var fakeGo = await CreateFakeGoAsync(root);
        var exitCode = await PackageHandler.HandleAsync(
            root,
            new FileInfo(bundlePath),
            ["server"],
            fakeGo,
            "missing",
            "missing");
        Assert.Equal(0, exitCode);
        return (root, bundlePath);
    }

    private static async Task<string> CreateFakeGoAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-go.cmd");
            await File.WriteAllTextAsync(path, "@echo off\r\nexit /b 0\r\n");
            return path;
        }

        var shellPath = Path.Combine(root, "fake-go.sh");
        await File.WriteAllTextAsync(shellPath, "#!/usr/bin/env sh\nexit 0\n");
        File.SetUnixFileMode(
            shellPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static async Task<string> CreateFakeMentorAsync(
        string root,
        string buildId)
    {
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.1",
            success = true,
            buildId,
            findings = new[]
            {
                new
                {
                    code = "M1001",
                    severity = "warning",
                    message = "Review the deployment responsibility boundary.",
                    relatedSymbolId = "task:Main",
                    recommendedAction = "Confirm ownership before deployment."
                }
            }
        });
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, $"fake-mentor-{Guid.NewGuid():N}.cmd");
            await File.WriteAllTextAsync(
                path,
                $"@echo off\r\necho {json}\r\nexit /b 0\r\n");
            return path;
        }

        var shellPath = Path.Combine(root, $"fake-mentor-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(
            shellPath,
            $"#!/usr/bin/env sh\nprintf '%s\\n' '{json}'\n");
        File.SetUnixFileMode(
            shellPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string[] arguments,
        string workingDirectory)
    {
        var assembly = typeof(CompileHandler).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Junexis CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await standardOutput, await standardError);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
