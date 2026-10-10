using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class LocalDeploymentTests
{
    [Fact]
    [Trait("Issue", "M7.6")]
    public void ReserveServerPortSet_ReservesHttpHealthAndCompleteTcpBridgeRange()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-ports-{Guid.NewGuid():N}");
        var serverRoot = Path.Combine(root, "server");
        Directory.CreateDirectory(serverRoot);
        try
        {
            File.WriteAllText(
                Path.Combine(serverRoot, "main.go"),
                "port := *tcpBasePort+3\n");

            var ports = LocalDeploymentHandler.ReserveServerPortSet(root);

            Assert.Equal(ports.HttpPort + 1, ports.HealthPort);
            Assert.Equal(ports.HttpPort + 2, ports.TcpPort);
            var listeners = Enumerable.Range(ports.HttpPort, 6)
                .Select(port => new TcpListener(IPAddress.Any, port))
                .ToArray();
            try
            {
                foreach (var listener in listeners)
                    listener.Start();
            }
            finally
            {
                foreach (var listener in listeners)
                    listener.Stop();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "40")]
    public async Task Prepare_VerifiesEvidenceAndExtractsOnlyRequestedTarget()
    {
        var fixture = await CreateFixtureAsync();
        var work = Path.Combine(fixture.Root, "local-work");
        try
        {
            var plan = await LocalDeploymentContract.PrepareAsync(
                fixture.Bundle,
                null,
                fixture.Preflight,
                ["report"],
                work);

            Assert.Equal(["report"], plan.Targets);
            Assert.False(plan.OwnsWorkDirectory);
            Assert.True(File.Exists(Path.Combine(work, "report", "deploy.ts")));
            Assert.True(File.Exists(Path.Combine(work, "report", "package.json")));
            Assert.False(Directory.Exists(Path.Combine(work, "server")));
            Assert.True(plan.ExtractedArtifactCount >= 6);
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "40")]
    public async Task Prepare_RejectsPreflightForDifferentBundle()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var report = await DeploymentPreflightContract.ReadAsync(fixture.Preflight);
            await DeploymentPreflightContract.WriteAtomicAsync(
                fixture.Preflight,
                report with { BundleId = new string('a', 64) });

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                LocalDeploymentContract.PrepareAsync(
                    fixture.Bundle,
                    null,
                    fixture.Preflight,
                    ["server"],
                    Path.Combine(fixture.Root, "mismatch-work")));

            Assert.Contains("does not belong", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "40")]
    public async Task Prepare_RejectsEmbeddedBeforeAnyExtraction()
    {
        var fixture = await CreateFixtureAsync();
        var work = Path.Combine(fixture.Root, "embedded-work");
        try
        {
            var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
                LocalDeploymentContract.PrepareAsync(
                    fixture.Bundle,
                    null,
                    fixture.Preflight,
                    ["embedded"],
                    work));

            Assert.Contains("M7.7", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(work));
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "40")]
    public async Task DeployLocalCommand_RejectsStaleEvidenceBeforeToolchains()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var report = await DeploymentPreflightContract.ReadAsync(fixture.Preflight);
            await DeploymentPreflightContract.WriteAtomicAsync(
                fixture.Preflight,
                report with { BuildId = new string('b', 64) });

            var result = await RunCliAsync(
                [
                    "deploy", "local", fixture.Bundle,
                    "--preflight", fixture.Preflight,
                    "--target", "server",
                    "--go-exe", "toolchain-that-must-not-run",
                    "--duration", "1"
                ],
                fixture.Root);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "does not belong",
                result.StandardError,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Toolchain executable",
                result.StandardError,
                StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalCommand_WritesDeploymentReceipt()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var reportTool = await CreateReportToolAsync(fixture.Root);
            var deploymentReceipt = Path.Combine(fixture.Root, "deployment-receipt.json");

            var result = await RunCliAsync(
                [
                    "deploy", "local", fixture.Bundle,
                    "--preflight", fixture.Preflight,
                    "--receipt-out", deploymentReceipt,
                    "--target", "report",
                    "--npm-exe", reportTool,
                    "--duration", "1"
                ],
                fixture.Root);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(deploymentReceipt));

            var preflight = await DeploymentPreflightContract.ReadAsync(fixture.Preflight);
            Assert.Contains(preflight.BuildId, result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains(preflight.BundleId, result.StandardOutput, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(deploymentReceipt));
            Assert.Equal(
                "junexis.deployment-receipt/0.1",
                document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(
                "succeeded",
                document.RootElement.GetProperty("lifecycle").GetProperty("status").GetString());
            Assert.Equal(
                1,
                document.RootElement.GetProperty("targets").GetArrayLength());
            Assert.Equal(
                "report",
                document.RootElement.GetProperty("targets")[0].GetProperty("target").GetString());
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalCommand_WritesDeploymentSession_AndStatusReadsIt()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var reportTool = await CreateReportToolAsync(fixture.Root);
            var deploymentSession = Path.Combine(fixture.Root, "deployment-session.json");

            var runResult = await RunCliAsync(
                [
                    "deploy", "local", fixture.Bundle,
                    "--preflight", fixture.Preflight,
                    "--session-out", deploymentSession,
                    "--target", "report",
                    "--npm-exe", reportTool,
                    "--duration", "1"
                ],
                fixture.Root);

            Assert.Equal(0, runResult.ExitCode);
            Assert.True(File.Exists(deploymentSession));

            using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(deploymentSession)))
            {
                Assert.Equal(
                    DeploymentSession.CurrentVersion,
                    document.RootElement.GetProperty("schemaVersion").GetString());
                Assert.Equal(
                    "stopped",
                    document.RootElement.GetProperty("state").GetString());
                Assert.False(string.IsNullOrWhiteSpace(
                    document.RootElement.GetProperty("owner").GetProperty("instanceId").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(
                    document.RootElement.GetProperty("owner").GetProperty("leaseExpiresAtUtc").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(
                    document.RootElement.GetProperty("lastHeartbeatUtc").GetString()));
                Assert.Equal(
                    "report",
                    document.RootElement.GetProperty("targets")[0].GetProperty("target").GetString());
                Assert.Equal(
                    "ready",
                    document.RootElement.GetProperty("targets")[0].GetProperty("status").GetString());
            }

            var statusResult = await RunCliAsync(
                [
                    "deploy", "local", "status",
                    "--session", deploymentSession,
                    "--json"
                ],
                fixture.Root);

            Assert.Equal(0, statusResult.ExitCode);
            Assert.Contains(DeploymentSession.CurrentVersion, statusResult.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"stopped\"", statusResult.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"target\": \"report\"", statusResult.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(fixture.Root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalStatus_ReportsLost_WhenOwnerLeaseExpired()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                5,
                DateTimeOffset.UtcNow.AddMinutes(-2).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-cli",
                    999999,
                    "dead-owner",
                    DateTimeOffset.UtcNow.AddSeconds(-10).UtcDateTime.ToString("O")),
                [new DeploymentSessionTarget("report", "ready", "Reported runtime readiness.", null, null, null)]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                ["deploy", "local", "status", "--session", sessionPath, "--json"],
                root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"state\": \"lost\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("lease expired", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalStop_StopsOrphanedTargetProcess_AndUpdatesSession()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-stop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        using var target = StartLongRunningProcess(root);
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddSeconds(-20).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddSeconds(-20).UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-cli",
                    999999,
                    "dead-owner",
                    DateTimeOffset.UtcNow.AddSeconds(-10).UtcDateTime.ToString("O")),
                [CreateProcessTarget("report", "ready", target)]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                ["deploy", "local", "stop", "--session", sessionPath],
                root);

            Assert.Equal(0, result.ExitCode);
            target.WaitForExit(5000);
            Assert.True(target.HasExited);

            var stored = await DeploymentSessionContract.ReadAsync(sessionPath);
            Assert.Equal("stopped", stored.State);
            Assert.Equal("stopped", stored.Targets[0].Status);
        }
        finally
        {
            if (!target.HasExited)
                target.Kill(entireProcessTree: true);
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalStop_RejectsActiveOwnerLease()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-stop-active-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-cli",
                    Environment.ProcessId,
                    "active-owner",
                    DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.ToString("O")),
                [new DeploymentSessionTarget("report", "ready", "Reported runtime readiness.", null, null, null)]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                ["deploy", "local", "stop", "--session", sessionPath],
                root);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("active owner lease", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalStop_AllowsMatchingActiveOwnerIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-stop-owned-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        using var target = StartLongRunningProcess(root);
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-studio",
                    Environment.ProcessId,
                    "studio-instance",
                    DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.ToString("O")),
                [CreateProcessTarget("report", "running", target)]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                [
                    "deploy", "local", "stop",
                    "--session", sessionPath,
                    "--owner-tool", "junexis-studio",
                    "--owner-instance-id", "studio-instance",
                    "--json"
                ],
                root);

            Assert.Equal(0, result.ExitCode);
            target.WaitForExit(5000);
            Assert.True(target.HasExited);
            Assert.Contains("\"state\": \"stopped\"", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (!target.HasExited)
                target.Kill(entireProcessTree: true);
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalStatus_UsesActiveHealthProbe_ForServerTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        using var server = StartHealthServer();
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-studio",
                    Environment.ProcessId,
                    "studio-instance",
                    DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.ToString("O")),
                [new DeploymentSessionTarget("server", "ready", "HTTP runtime", null, server.RuntimeUrl, server.HealthUrl)]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                ["deploy", "local", "status", "--session", sessionPath, "--json"],
                root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"state\": \"running\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Active probe OK", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains(server.HealthUrl, result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalAdopt_RewritesOwner_ForLiveOrphanedSession()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-adopt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        using var target = StartLongRunningProcess(root);
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddSeconds(-20).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddSeconds(-20).UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-cli",
                    999999,
                    "dead-owner",
                    DateTimeOffset.UtcNow.AddSeconds(-10).UtcDateTime.ToString("O")),
                [CreateProcessTarget("report", "ready", target)]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                [
                    "deploy", "local", "adopt",
                    "--session", sessionPath,
                    "--owner-tool", "junexis-studio",
                    "--owner-instance-id", "studio-01",
                    "--owner-process-id", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--lease-seconds", "45",
                    "--json"
                ],
                root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"tool\": \"junexis-studio\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"instanceId\": \"studio-01\"", result.StandardOutput, StringComparison.Ordinal);

            var stored = await DeploymentSessionContract.ReadAsync(sessionPath);
            Assert.Equal("junexis-studio", stored.Owner.Tool);
            Assert.Equal("studio-01", stored.Owner.InstanceId);
        }
        finally
        {
            if (!target.HasExited)
                target.Kill(entireProcessTree: true);
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalAdopt_RejectsActiveOwnerLease()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-adopt-active-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-studio",
                    Environment.ProcessId,
                    "studio-active",
                    DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.ToString("O")),
                [new DeploymentSessionTarget("report", "ready", "Reported runtime readiness.", null, null, null)]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                [
                    "deploy", "local", "adopt",
                    "--session", sessionPath,
                    "--owner-tool", "junexis-studio",
                    "--owner-instance-id", "studio-02",
                    "--owner-process-id", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ],
                root);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("active owner lease", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalAdopt_RenewsLease_ForSameLongLivedOwner()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-renew-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        using var ownerProcess = Process.GetCurrentProcess();
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-studio",
                    Environment.ProcessId,
                    "studio-active",
                    DateTimeOffset.UtcNow.AddSeconds(10).UtcDateTime.ToString("O")),
                [CreateProcessTarget("report", "ready", ownerProcess)]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                [
                    "deploy", "local", "adopt",
                    "--session", sessionPath,
                    "--owner-tool", "junexis-studio",
                    "--owner-instance-id", "studio-active",
                    "--owner-process-id", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--lease-seconds", "45",
                    "--json"
                ],
                root);

            Assert.Equal(0, result.ExitCode);
            var renewed = await DeploymentSessionContract.ReadAsync(sessionPath);
            Assert.Equal(Environment.ProcessId, renewed.Owner.ProcessId);
            Assert.True(DateTimeOffset.Parse(renewed.Owner.LeaseExpiresAtUtc) > DateTimeOffset.UtcNow.AddSeconds(30));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalStop_RejectsLiveProcess_WhenIdentityIsForged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-stop-forged-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        using var target = Process.GetCurrentProcess();
        try
        {
            var forgedTarget = CreateProcessTarget("report", "ready", target) with
            {
                Details = "Forged process identity.",
                ProcessStartedAtUtc = target.StartTime.ToUniversalTime().AddSeconds(-1).ToString("O")
            };
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddSeconds(-20).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddSeconds(-20).UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-cli",
                    999999,
                    "dead-owner",
                    DateTimeOffset.UtcNow.AddSeconds(-10).UtcDateTime.ToString("O")),
                [forgedTarget]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                ["deploy", "local", "stop", "--session", sessionPath],
                root);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("process identity cannot be confirmed", result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.False(target.HasExited);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.6")]
    public async Task DeployLocalStatus_RejectsNonLoopbackHealthUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-local-probe-untrusted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sessionPath = Path.Combine(root, "deployment-session.json");
        try
        {
            var session = new DeploymentSession(
                DeploymentSession.CurrentVersion,
                Guid.NewGuid().ToString("N"),
                "running",
                new string('a', 64),
                new string('b', 64),
                Path.Combine(root, "bundle.package-receipt.json"),
                Path.Combine(root, "deployment-preflight.json"),
                root,
                30,
                DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                new DeploymentSessionOwner(
                    "junexis-studio",
                    Environment.ProcessId,
                    "studio-active",
                    DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.ToString("O")),
                [new DeploymentSessionTarget("server", "ready", "Untrusted probe.", null, null, "http://example.com/health/ready")]);
            await DeploymentSessionContract.WriteAtomicAsync(sessionPath, session);

            var result = await RunCliAsync(
                ["deploy", "local", "status", "--session", sessionPath, "--json"],
                root);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("loopback", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DeploymentSessionTarget CreateProcessTarget(string targetName, string status, Process process) =>
        new(
            targetName,
            status,
            "Reported runtime readiness.",
            process.Id,
            null,
            null,
            process.StartTime.ToUniversalTime().ToString("O"),
            Path.GetFullPath(process.MainModule?.FileName
                ?? (Path.IsPathFullyQualified(process.StartInfo.FileName)
                    ? process.StartInfo.FileName
                    : throw new InvalidOperationException("Test process executable path is unavailable."))));

    private static async Task<(string Root, string Bundle, string Preflight)>
        CreateFixtureAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"junexis-local-deployment-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "m7-local-deployment"
            entry = "src/main.jnx"
            targets = ["server", "report"]
            outDir = "out"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            """
            task Service() @server {
                log("server ready");
            }

            task Dashboard() @report {
                log("report ready");
            }

            pipeline ServicePipeline {
                use Service;
            }

            pipeline DashboardPipeline {
                use Dashboard;
            }
            """);
        var bundle = Path.Combine(root, "m7-local.system-bundle.zip");
        var exitCode = await CompileHandler.HandleAsync(
            root,
            outDir: null,
            targets: ["server", "report"],
            json: false,
            outputSchema: "2.0",
            expectedBuildId: null);
        Assert.Equal(0, exitCode);
        var created = await SystemBundleContract.CreateAsync(root, new FileInfo(bundle));
        await PackageReceiptContract.WriteAsync(created);

        var report = await DeploymentPreflightContract.EvaluateAsync(
            bundle,
            null,
            ["server", "report"],
            Path.Combine(root, "mentor-not-configured"));
        Assert.Equal("ready", report.TechnicalStatus);
        var preflight = Path.Combine(root, "deployment-preflight.json");
        await DeploymentPreflightContract.WriteAtomicAsync(preflight, report);
        return (root, bundle, preflight);
    }

    private static async Task<string> CreateSuccessfulToolAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "successful-tool.cmd");
            await File.WriteAllTextAsync(path, "@echo off\r\nexit /b 0\r\n");
            return path;
        }
        var shell = Path.Combine(root, "successful-tool.sh");
        await File.WriteAllTextAsync(shell, "#!/usr/bin/env sh\nexit 0\n");
        File.SetUnixFileMode(
            shell,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shell;
    }

    private static async Task<string> CreateServerToolAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "go-success.cmd");
            await File.WriteAllTextAsync(
                path,
                "@echo off\r\nif \"%1\"==\"build\" type nul > junexis-server.exe\r\nexit /b 0\r\n");
            return path;
        }

        var shell = Path.Combine(root, "go-success.sh");
        await File.WriteAllTextAsync(
            shell,
            "#!/usr/bin/env sh\nif [ \"$1\" = \"build\" ]; then : > junexis-server; chmod +x junexis-server; fi\nexit 0\n");
        File.SetUnixFileMode(
            shell,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shell;
    }

    private static async Task<string> CreateReportToolAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "npm-success.cmd");
            await File.WriteAllTextAsync(
                path,
                "@echo off\r\nif not exist dist mkdir dist\r\n> dist\\deploy.js echo console.log(JSON.stringify({schemaVersion:'junexis.local-runtime/0.1',target:'report',status:'ready',buildId:process.env.JUNEXIS_BUILD_ID,bundleId:process.env.JUNEXIS_BUNDLE_ID}));\r\n>> dist\\deploy.js echo setInterval(() =^> {}, 1000);\r\nexit /b 0\r\n");
            return path;
        }

        var shell = Path.Combine(root, "npm-success.sh");
        await File.WriteAllTextAsync(
            shell,
            "#!/usr/bin/env sh\nmkdir -p dist\ncat <<'EOF' > dist/deploy.js\nconsole.log(JSON.stringify({ schemaVersion: 'junexis.local-runtime/0.1', target: 'report', status: 'ready', buildId: process.env.JUNEXIS_BUILD_ID, bundleId: process.env.JUNEXIS_BUNDLE_ID }));\nsetInterval(() => {}, 1000);\nEOF\nexit 0\n");
        File.SetUnixFileMode(
            shell,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shell;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)>
        RunCliAsync(string[] arguments, string workingDirectory)
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

    private static Process StartLongRunningProcess(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var startInfo = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "PING.EXE"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("300");
            startInfo.ArgumentList.Add("127.0.0.1");
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start long-running test process.");
        }

        return Process.Start(new ProcessStartInfo("/bin/sleep")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "300" }
        }) ?? throw new InvalidOperationException("Could not start long-running test process.");
    }

    private static TestHealthServer StartHealthServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cancellation = new CancellationTokenSource();
        var loop = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellation.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                    {
                    }

                    var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK";
                    var bytes = System.Text.Encoding.ASCII.GetBytes(response);
                    await stream.WriteAsync(bytes, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                finally
                {
                    client?.Dispose();
                }
            }
        }, cancellation.Token);
        return new TestHealthServer(
            listener,
            cancellation,
            loop,
            $"http://127.0.0.1:{port}",
            $"http://127.0.0.1:{port}/health/ready");
    }

    private sealed class TestHealthServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation;
        private readonly Task _loop;

        public TestHealthServer(
            TcpListener listener,
            CancellationTokenSource cancellation,
            Task loop,
            string runtimeUrl,
            string healthUrl)
        {
            _listener = listener;
            _cancellation = cancellation;
            _loop = loop;
            RuntimeUrl = runtimeUrl;
            HealthUrl = healthUrl;
        }

        public string RuntimeUrl { get; }

        public string HealthUrl { get; }

        public void Dispose()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                _loop.GetAwaiter().GetResult();
            }
            catch
            {
            }
            _cancellation.Dispose();
        }
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }
}
