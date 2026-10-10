using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>Runs a verified server/report package as a bounded local session.</summary>
internal static class LocalDeploymentHandler
{
    private const string ReportReadyMarker =
        "\"schemaVersion\":\"junexis.local-runtime/0.1\"";
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SessionHeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SessionLeaseGrace = TimeSpan.FromSeconds(5);

    internal static async Task<int> HandleAsync(
        FileInfo bundle,
        FileInfo preflight,
        FileInfo? receipt,
        FileInfo? receiptOut,
        FileInfo? sessionOut,
        string[]? targets,
        string goExecutable,
        string npmExecutable,
        int durationSeconds,
        DirectoryInfo? workDirectory)
    {
        if (durationSeconds is < 1 or > 300)
        {
            await Console.Error.WriteLineAsync(
                "error: local deployment duration must be between 1 and 300 seconds.");
            return 1;
        }

        LocalDeploymentPlan? plan = null;
        var processes = new List<LocalProcess>();
        var startedAt = DateTimeOffset.UtcNow;
        var targetStates = new Dictionary<string, DeploymentTargetReceipt>(StringComparer.Ordinal);
        var targetProcesses = new Dictionary<string, TargetProcessIdentity?>(StringComparer.Ordinal);
        var deploymentReceiptPath = string.Empty;
        var deploymentSessionPath = string.Empty;
        var sessionId = Guid.NewGuid().ToString("N");
        var ownerInstanceId = $"junexis-cli-{Environment.ProcessId}";
        var sessionState = "prepared";
        var exitCode = 1;
        var lifecycleStatus = "failed";
        var lifecycleSummary = "Local deployment did not complete.";
        CancellationTokenSource? heartbeatCts = null;
        Task? heartbeatTask = null;
        try
        {
            plan = await LocalDeploymentContract.PrepareAsync(
                bundle.FullName,
                receipt?.FullName,
                preflight.FullName,
                targets,
                workDirectory?.FullName);
            deploymentReceiptPath = receiptOut is null
                ? DeploymentReceiptContract.DefaultPath(preflight.FullName)
                : Path.GetFullPath(receiptOut.FullName);
            deploymentSessionPath = sessionOut is null
                ? DeploymentSessionContract.DefaultPath(preflight.FullName)
                : Path.GetFullPath(sessionOut.FullName);
            foreach (var target in plan.Targets)
            {
                targetStates[target] = new DeploymentTargetReceipt(
                    target,
                    "prepared",
                    "Verified bundle extracted for bounded local deployment.");
                targetProcesses[target] = null;
            }
            sessionState = "prepared";
            await PublishSessionAsync(
                deploymentSessionPath,
                sessionId,
                sessionState,
                ownerInstanceId,
                plan,
                preflight.FullName,
                durationSeconds,
                startedAt,
                targetStates,
                targetProcesses).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(
                $"Local deployment 0.1 · verified {plan.BundleId} · build {plan.BuildId}");
            await Console.Out.WriteLineAsync(
                $"Workspace: {plan.WorkDirectory} ({plan.ExtractedArtifactCount} artifacts)");

            if (plan.Targets.Contains("server", StringComparer.Ordinal))
            {
                if (!HasPackagedServer(plan))
                    await BuildServerAsync(plan, goExecutable);
                else
                    await Console.Out.WriteLineAsync("USING packaged server executable");
                targetStates["server"] = new DeploymentTargetReceipt(
                    "server",
                    "built",
                    HasPackagedServer(plan)
                        ? "Packaged native server verified."
                        : "Go server build completed.");
                sessionState = "building";
                await PublishSessionAsync(
                    deploymentSessionPath,
                    sessionId,
                    sessionState,
                    ownerInstanceId,
                    plan,
                    preflight.FullName,
                    durationSeconds,
                    startedAt,
                    targetStates,
                    targetProcesses).ConfigureAwait(false);
            }
            if (plan.Targets.Contains("report", StringComparer.Ordinal))
            {
                if (!HasPackagedReport(plan))
                    await BuildReportAsync(plan, npmExecutable);
                else
                    await Console.Out.WriteLineAsync("USING packaged report application");
                targetStates["report"] = new DeploymentTargetReceipt(
                    "report",
                    "built",
                    HasPackagedReport(plan)
                        ? "Packaged browser application verified."
                        : "TypeScript report build completed.");
                sessionState = "building";
                await PublishSessionAsync(
                    deploymentSessionPath,
                    sessionId,
                    sessionState,
                    ownerInstanceId,
                    plan,
                    preflight.FullName,
                    durationSeconds,
                    startedAt,
                    targetStates,
                    targetProcesses).ConfigureAwait(false);
            }

            var (httpPort, healthPort, tcpPort) = ReserveServerPortSet(plan.WorkDirectory);
            if (plan.Targets.Contains("server", StringComparer.Ordinal))
            {
                var runtimeIdentityEnvironment = new Dictionary<string, string>
                {
                    ["JUNEXIS_BUILD_ID"] = plan.BuildId,
                    ["JUNEXIS_BUNDLE_ID"] = plan.BundleId
                };
                var server = StartProcess(
                    ServerExecutablePath(plan),
                    [
                        "--server-mode", "persistent",
                        "--http-port", httpPort.ToString(),
                        "--health-port", healthPort.ToString(),
                        "--tcp-port", tcpPort.ToString()
                    ],
                    Path.Combine(plan.WorkDirectory, "server"),
                    environment: runtimeIdentityEnvironment,
                    label: "server",
                    readySignal: null);
                processes.Add(server);
                targetProcesses["server"] = CaptureProcessIdentity(server.Process);
                await WaitForHealthAsync(server.Process, healthPort);
                targetStates["server"] = new DeploymentTargetReceipt(
                    "server",
                    "ready",
                    $"HTTP http://127.0.0.1:{httpPort} · health:{healthPort}");
                sessionState = "starting";
                await PublishSessionAsync(
                    deploymentSessionPath,
                    sessionId,
                    sessionState,
                    ownerInstanceId,
                    plan,
                    preflight.FullName,
                    durationSeconds,
                    startedAt,
                    targetStates,
                    targetProcesses).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(
                    $"READY server · http://127.0.0.1:{httpPort} · health:{healthPort}");
            }

            if (plan.Targets.Contains("report", StringComparer.Ordinal))
            {
                if (HasPackagedReport(plan))
                {
                    var reportPort = ReserveSinglePort();
                    var reportEnvironment = new Dictionary<string, string>
                    {
                        ["JUNEXIS_BUILD_ID"] = plan.BuildId,
                        ["JUNEXIS_BUNDLE_ID"] = plan.BundleId,
                        ["JUNEXIS_REPORT_PORT"] = reportPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    };
                    var reportServerUrl = plan.Targets.Contains("server", StringComparer.Ordinal)
                        ? $"http://127.0.0.1:{httpPort}"
                        : "http://localhost:8080";
                    reportEnvironment["JUNEXIS_SERVER_URL"] = reportServerUrl;
                    var report = StartProcess(
                        ResolveRequiredExecutable("node"),
                        ["serve.mjs"],
                        Path.Combine(plan.WorkDirectory, "report", "runtime"),
                        reportEnvironment,
                        "report",
                        readySignal: null);
                    processes.Add(report);
                    targetProcesses["report"] = CaptureProcessIdentity(report.Process);
                    await WaitForHttpHealthAsync(
                        report.Process,
                        $"http://127.0.0.1:{reportPort}/health/ready",
                        "report application");
                    var panelUrl = $"http://127.0.0.1:{reportPort}/?server={Uri.EscapeDataString(reportServerUrl)}";
                    targetStates["report"] = new DeploymentTargetReceipt(
                        "report",
                        "ready",
                        $"Panel {panelUrl}");
                    sessionState = "starting";
                    await PublishSessionAsync(
                        deploymentSessionPath,
                        sessionId,
                        sessionState,
                        ownerInstanceId,
                        plan,
                        preflight.FullName,
                        durationSeconds,
                        startedAt,
                        targetStates,
                        targetProcesses).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"READY report · {panelUrl}");
                }
                else
                {
                var reportReady = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var reportEnvironment = new Dictionary<string, string>
                {
                    ["JUNEXIS_BUILD_ID"] = plan.BuildId,
                    ["JUNEXIS_BUNDLE_ID"] = plan.BundleId
                };
                if (plan.Targets.Contains("server", StringComparer.Ordinal))
                    reportEnvironment["JUNEXIS_SERVER_URL"] = $"http://127.0.0.1:{httpPort}";
                var report = StartProcess(
                    ResolveRequiredExecutable("node"),
                    [Path.Combine("dist", "deploy.js")],
                    Path.Combine(plan.WorkDirectory, "report"),
                    reportEnvironment,
                    "report",
                    line =>
                    {
                        if (line.Contains(ReportReadyMarker, StringComparison.Ordinal))
                            reportReady.TrySetResult();
                    });
                processes.Add(report);
                targetProcesses["report"] = CaptureProcessIdentity(report.Process);
                await WaitForSignalAsync(
                    reportReady.Task,
                    report.Process,
                    "report runtime readiness");
                targetStates["report"] = new DeploymentTargetReceipt(
                    "report",
                    "ready",
                    $"Reported runtime readiness for build {plan.BuildId}.");
                sessionState = "starting";
                await PublishSessionAsync(
                    deploymentSessionPath,
                    sessionId,
                    sessionState,
                    ownerInstanceId,
                    plan,
                    preflight.FullName,
                    durationSeconds,
                    startedAt,
                    targetStates,
                    targetProcesses).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(
                    $"READY report · build {plan.BuildId}");
                }
            }

            sessionState = "running";
            await PublishSessionAsync(
                deploymentSessionPath,
                sessionId,
                sessionState,
                ownerInstanceId,
                plan,
                preflight.FullName,
                durationSeconds,
                startedAt,
                targetStates,
                targetProcesses).ConfigureAwait(false);
            heartbeatCts = new CancellationTokenSource();
            heartbeatTask = StartHeartbeatAsync(
                deploymentSessionPath,
                sessionId,
                ownerInstanceId,
                () => sessionState,
                plan,
                preflight.FullName,
                durationSeconds,
                startedAt,
                targetStates,
                targetProcesses,
                heartbeatCts.Token);
            await Console.Out.WriteLineAsync(
                $"RUNNING bounded local session · {durationSeconds}s");
            var duration = Task.Delay(TimeSpan.FromSeconds(durationSeconds));
            var exited = processes
                .Select(process => process.Process.WaitForExitAsync())
                .ToArray();
            var completed = await Task.WhenAny([duration, .. exited]);
            if (completed != duration)
                throw new InvalidOperationException(
                    "A local deployment process exited before the bounded session completed.");
            await Console.Out.WriteLineAsync("STOPPING bounded local session");
            exitCode = 0;
            lifecycleStatus = "succeeded";
            lifecycleSummary = "Bounded local deployment completed successfully.";
            return 0;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException
                or InvalidOperationException
                or TimeoutException)
        {
            lifecycleStatus = "failed";
            lifecycleSummary = $"Local deployment failed: {ex.Message}";
            await Console.Error.WriteLineAsync(
                $"error: local deployment failed: {ex.Message}");
            return 1;
        }
        finally
        {
            if (heartbeatCts is not null)
            {
                await heartbeatCts.CancelAsync().ConfigureAwait(false);
                try
                {
                    if (heartbeatTask is not null)
                        await heartbeatTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                heartbeatCts.Dispose();
            }
            foreach (var process in processes.AsEnumerable().Reverse())
                await StopAsync(process);
            if (processes.Count > 0)
                await Console.Out.WriteLineAsync("STOPPED all owned local processes");
            if (plan is not null)
            {
                var sessionPath = string.IsNullOrWhiteSpace(deploymentSessionPath)
                    ? DeploymentSessionContract.DefaultPath(preflight.FullName)
                    : deploymentSessionPath;
                sessionState = exitCode == 0 ? "stopped" : "failed";
                try
                {
                    await PublishSessionAsync(
                        sessionPath,
                        sessionId,
                        sessionState,
                        ownerInstanceId,
                        plan,
                        preflight.FullName,
                        durationSeconds,
                        startedAt,
                        targetStates,
                        targetProcesses).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"warning: final deployment session could not be written: {ex.Message}");
                }
                var receiptPath = string.IsNullOrWhiteSpace(deploymentReceiptPath)
                    ? DeploymentReceiptContract.DefaultPath(preflight.FullName)
                    : deploymentReceiptPath;
                var receiptValue = new DeploymentReceipt(
                    DeploymentReceipt.CurrentVersion,
                    plan.BundleId,
                    plan.BuildId,
                    Path.GetFullPath(plan.PackageReceiptPath),
                    Path.GetFullPath(preflight.FullName),
                    Path.GetFullPath(plan.WorkDirectory),
                    plan.Targets
                        .Select(target => targetStates.TryGetValue(target, out var state)
                            ? state
                            : new DeploymentTargetReceipt(
                                target,
                                "prepared",
                                "No runtime status was recorded for this target."))
                        .ToArray(),
                    new DeploymentLifecycleReceipt(
                        lifecycleStatus,
                        startedAt.UtcDateTime.ToString("O"),
                        DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                        exitCode == 0 ? 0 : exitCode,
                        lifecycleSummary,
                        durationSeconds));
                try
                {
                    await DeploymentReceiptContract.WriteAtomicAsync(receiptPath, receiptValue);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"warning: final deployment receipt could not be written: {ex.Message}");
                }
            }
            if (plan is not null)
            {
                try
                {
                    LocalDeploymentContract.Cleanup(plan);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"warning: local deployment workspace cleanup failed: {ex.Message}");
                }
            }
        }
    }

    internal static async Task<int> ReportStatusAsync(
        FileInfo session,
        bool json)
    {
        try
        {
            var value = await DeploymentSessionContract.ReadAsync(session.FullName).ConfigureAwait(false);
            var evaluated = await EvaluateSessionAsync(value).ConfigureAwait(false);
            if (json)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(
                    evaluated,
                    new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            await Console.Out.WriteLineAsync(
                $"Local deployment session {evaluated.SessionId[..12]} · {evaluated.State} · build {evaluated.BuildId[..12]}");
            await Console.Out.WriteLineAsync(
                $"Owner: {evaluated.Owner.Tool} pid {evaluated.Owner.ProcessId} · lease {evaluated.Owner.LeaseExpiresAtUtc} · heartbeat {evaluated.LastHeartbeatUtc}");
            foreach (var target in evaluated.Targets)
            {
                var processPart = target.ProcessId is int processId ? $" · pid {processId}" : string.Empty;
                await Console.Out.WriteLineAsync(
                    $"- {target.Target}: {target.Status}{processPart} · {target.Details}");
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Console.Error.WriteLineAsync($"error: deployment session status failed: {ex.Message}");
            return 1;
        }
    }

    internal static async Task<int> StopSessionAsync(
        FileInfo session,
        string? ownerTool,
        string? ownerInstanceId,
        bool json)
    {
        if (string.IsNullOrWhiteSpace(ownerTool) != string.IsNullOrWhiteSpace(ownerInstanceId))
        {
            await Console.Error.WriteLineAsync(
                "error: stop requires both owner-tool and owner-instance-id when caller identity is provided.");
            return 1;
        }

        try
        {
            var current = await DeploymentSessionContract.ReadAsync(session.FullName).ConfigureAwait(false);
            var evaluated = await EvaluateSessionAsync(current).ConfigureAwait(false);
            if (string.Equals(evaluated.State, "stopped", StringComparison.Ordinal)
                || string.Equals(evaluated.State, "failed", StringComparison.Ordinal))
            {
                if (json)
                {
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(
                        evaluated,
                        new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    await Console.Out.WriteLineAsync(
                        $"Local deployment session {evaluated.SessionId[..12]} is already {evaluated.State}.");
                }
                return 0;
            }

            if (IsOwnerActive(evaluated))
            {
                if (!IsMatchingOwner(evaluated, ownerTool, ownerInstanceId))
                {
                    await Console.Error.WriteLineAsync(
                        "error: refusing to stop a session with an active owner lease unless the caller provides the matching owner-tool and owner-instance-id.");
                    return 1;
                }
            }

            var untrustedLiveTarget = evaluated.Targets.FirstOrDefault(target =>
                target.ProcessId is int processId
                && IsProcessAlive(processId)
                && !IsMatchingTargetProcess(target));
            if (untrustedLiveTarget is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"error: refusing to stop target '{untrustedLiveTarget.Target}' because its process identity cannot be confirmed.");
                return 1;
            }

            var killedCount = 0;
            var stoppedTargets = evaluated.Targets.Select(target =>
            {
                if (TryKillTargetProcess(target))
                {
                    killedCount++;
                    return target with { Status = "stopped", Details = "Stopped by 'deploy local stop --session'." };
                }

                return target with
                {
                    Status = target.ProcessId is int ? "stopped" : target.Status,
                    Details = target.ProcessId is int
                        ? "No live target process remained; session was closed from its deterministic snapshot."
                        : target.Details
                };
            }).ToArray();

            var stopped = evaluated with
            {
                State = "stopped",
                LastHeartbeatUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                LastUpdatedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                Targets = stoppedTargets,
                Owner = evaluated.Owner with
                {
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O")
                }
            };
            await DeploymentSessionContract.WriteAtomicAsync(session.FullName, stopped).ConfigureAwait(false);
            if (json)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(
                    stopped,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                await Console.Out.WriteLineAsync(
                    $"Stopped local deployment session {stopped.SessionId[..12]} · targets signalled: {killedCount}");
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Console.Error.WriteLineAsync($"error: deployment session stop failed: {ex.Message}");
            return 1;
        }
    }

    private static bool IsMatchingOwner(
        DeploymentSession session,
        string? ownerTool,
        string? ownerInstanceId) =>
        !string.IsNullOrWhiteSpace(ownerTool)
        && !string.IsNullOrWhiteSpace(ownerInstanceId)
        && string.Equals(session.Owner.Tool, ownerTool, StringComparison.Ordinal)
        && string.Equals(session.Owner.InstanceId, ownerInstanceId, StringComparison.Ordinal);

    internal static async Task<int> AdoptSessionAsync(
        FileInfo session,
        string ownerTool,
        string ownerInstanceId,
        int ownerProcessId,
        int leaseSeconds,
        bool json)
    {
        if (leaseSeconds is < 5 or > 3600)
        {
            await Console.Error.WriteLineAsync(
                "error: adopt lease must be between 5 and 3600 seconds.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(ownerTool) || string.IsNullOrWhiteSpace(ownerInstanceId))
        {
            await Console.Error.WriteLineAsync(
                "error: adopt requires non-empty owner-tool and owner-instance-id.");
            return 1;
        }
        if (ownerProcessId <= 0 || !IsProcessAlive(ownerProcessId))
        {
            await Console.Error.WriteLineAsync(
                "error: adopt requires a live long-lived owner-process-id.");
            return 1;
        }

        try
        {
            var current = await DeploymentSessionContract.ReadAsync(session.FullName).ConfigureAwait(false);
            if (IsOwnerActive(current) && !IsMatchingOwner(current, ownerTool, ownerInstanceId))
            {
                await Console.Error.WriteLineAsync(
                    "error: refusing to adopt a session with an active owner lease.");
                return 1;
            }

            var evaluated = await EvaluateSessionAsync(current).ConfigureAwait(false);
            if (string.Equals(evaluated.State, "lost", StringComparison.Ordinal)
                && evaluated.Targets.All(target => !string.Equals(target.Status, "running", StringComparison.Ordinal)
                    && !string.Equals(target.Status, "ready", StringComparison.Ordinal)))
            {
                await Console.Error.WriteLineAsync(
                    "error: refusing to adopt a session without any live target evidence.");
                return 1;
            }

            var now = DateTimeOffset.UtcNow;
            var adopted = evaluated with
            {
                State = "running",
                LastHeartbeatUtc = now.UtcDateTime.ToString("O"),
                LastUpdatedAtUtc = now.UtcDateTime.ToString("O"),
                Owner = new DeploymentSessionOwner(
                    ownerTool,
                    ownerProcessId,
                    ownerInstanceId,
                    now.AddSeconds(leaseSeconds).UtcDateTime.ToString("O"))
            };
            await DeploymentSessionContract.WriteAtomicAsync(session.FullName, adopted).ConfigureAwait(false);

            if (json)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(
                    adopted,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                await Console.Out.WriteLineAsync(
                    $"Adopted local deployment session {adopted.SessionId[..12]} for {ownerTool}:{ownerInstanceId} until {adopted.Owner.LeaseExpiresAtUtc}");
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Console.Error.WriteLineAsync($"error: deployment session adopt failed: {ex.Message}");
            return 1;
        }
    }

    private static Task PublishSessionAsync(
        string path,
        string sessionId,
        string state,
        string ownerInstanceId,
        LocalDeploymentPlan plan,
        string preflightReportPath,
        int durationSeconds,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<string, DeploymentTargetReceipt> targetStates,
        IReadOnlyDictionary<string, TargetProcessIdentity?> targetProcesses)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new DeploymentSession(
            DeploymentSession.CurrentVersion,
            sessionId,
            state,
            plan.BuildId,
            plan.BundleId,
            Path.GetFullPath(plan.PackageReceiptPath),
            Path.GetFullPath(preflightReportPath),
            Path.GetFullPath(plan.WorkDirectory),
            durationSeconds,
            startedAt.UtcDateTime.ToString("O"),
            now.UtcDateTime.ToString("O"),
            now.UtcDateTime.ToString("O"),
            new DeploymentSessionOwner(
                "junexis-cli",
                Environment.ProcessId,
                ownerInstanceId,
                now.AddSeconds(durationSeconds).Add(SessionLeaseGrace).UtcDateTime.ToString("O")),
            plan.Targets.Select(target =>
            {
                var targetState = targetStates[target];
                targetProcesses.TryGetValue(target, out var process);
                return new DeploymentSessionTarget(
                    target,
                    targetState.Status,
                    targetState.Details,
                    process?.ProcessId,
                    target == "server" && targetState.Status == "ready"
                        ? TryExtractServerRuntimeUrl(targetState.Details)
                        : null,
                    target == "server" && targetState.Status == "ready"
                        ? TryExtractServerHealthUrl(targetState.Details)
                        : null,
                    process?.StartedAtUtc,
                    process?.ExecutablePath);
            }).ToArray());
        return DeploymentSessionContract.WriteAtomicAsync(path, session);
    }

    private static Task StartHeartbeatAsync(
        string path,
        string sessionId,
        string ownerInstanceId,
        Func<string> stateProvider,
        LocalDeploymentPlan plan,
        string preflightReportPath,
        int durationSeconds,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<string, DeploymentTargetReceipt> targetStates,
        IReadOnlyDictionary<string, TargetProcessIdentity?> targetProcesses,
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(SessionHeartbeatInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await PublishSessionAsync(
                    path,
                    sessionId,
                    stateProvider(),
                    ownerInstanceId,
                    plan,
                    preflightReportPath,
                    durationSeconds,
                    startedAt,
                    targetStates,
                    targetProcesses).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    private static async Task<DeploymentSession> EvaluateSessionAsync(DeploymentSession session)
    {
        if (string.Equals(session.State, "stopped", StringComparison.Ordinal)
            || string.Equals(session.State, "failed", StringComparison.Ordinal))
            return session;

        var evaluatedTargets = await Task.WhenAll(session.Targets.Select(EvaluateTargetAsync)).ConfigureAwait(false);
        var hasLiveTarget = evaluatedTargets.Any(target =>
            string.Equals(target.Status, "running", StringComparison.Ordinal)
            || string.Equals(target.Status, "ready", StringComparison.Ordinal));

        if (IsOwnerActive(session))
        {
            return session with
            {
                State = hasLiveTarget ? "running" : "lost",
                Targets = evaluatedTargets
            };
        }

        var now = DateTimeOffset.UtcNow;
        if (!DateTimeOffset.TryParse(session.Owner.LeaseExpiresAtUtc, out var leaseExpiresAt)
            || leaseExpiresAt <= now)
        {
            return session with
            {
                State = hasLiveTarget ? "running" : "lost",
                Targets = evaluatedTargets.Select(target =>
                    hasLiveTarget && (string.Equals(target.Status, "running", StringComparison.Ordinal)
                        || string.Equals(target.Status, "ready", StringComparison.Ordinal))
                        ? target with { Details = AppendDetail(target.Details, "Owner lease expired; live target evidence remains.") }
                        : target with { Status = "lost", Details = "Owner lease expired; no trusted live session remains." })
                    .ToArray()
            };
        }

        return session with
        {
            State = "lost",
            Targets = evaluatedTargets.Select(target =>
                hasLiveTarget && (string.Equals(target.Status, "running", StringComparison.Ordinal)
                    || string.Equals(target.Status, "ready", StringComparison.Ordinal))
                    ? target with { Details = AppendDetail(target.Details, "Owner process is no longer active; live target evidence remains orphaned.") }
                    : target with { Status = "lost", Details = "Owner process is no longer active; session snapshot is orphaned." })
                .ToArray()
        };
    }

    private static async Task<DeploymentSessionTarget> EvaluateTargetAsync(DeploymentSessionTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.HealthUrl))
        {
            if (await ProbeHealthUrlAsync(target.HealthUrl).ConfigureAwait(false))
            {
                return target with
                {
                    Status = "running",
                    Details = AppendDetail(target.Details, $"Active probe OK: {target.HealthUrl}")
                };
            }

            return target with
            {
                Status = "lost",
                Details = $"Active probe failed: {target.HealthUrl}"
            };
        }

        if (target.ProcessId is int processId)
        {
            return IsProcessAlive(processId)
                ? target with
                {
                    Status = "running",
                    Details = AppendDetail(target.Details, $"Process {processId} is alive.")
                }
                : target with
                {
                    Status = "lost",
                    Details = $"Process {processId} is no longer alive."
                };
        }

        return target with
        {
            Status = "lost",
            Details = "No active probe endpoint or live process evidence is available for this target."
        };
    }

    private static async Task<bool> ProbeHealthUrlAsync(string healthUrl)
    {
        if (!IsAllowedHealthUrl(healthUrl))
            return false;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        try
        {
            using var response = await client.GetAsync(healthUrl).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static string? TryExtractServerRuntimeUrl(string details)
    {
        const string marker = "HTTP ";
        var index = details.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;
        var end = details.IndexOf(" ·", index, StringComparison.Ordinal);
        return end > index
            ? details.Substring(index + marker.Length, end - index - marker.Length)
            : details[(index + marker.Length)..].Trim();
    }

    private static string? TryExtractServerHealthUrl(string details)
    {
        const string marker = "health:";
        var index = details.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;
        var portText = details[(index + marker.Length)..].Trim();
        return int.TryParse(portText, out var port)
            ? $"http://127.0.0.1:{port}/health/ready"
            : null;
    }

    private static string AppendDetail(string current, string extra) =>
        current.Contains(extra, StringComparison.Ordinal)
            ? current
            : string.IsNullOrWhiteSpace(current)
                ? extra
                : current + " " + extra;

    private static bool IsOwnerActive(DeploymentSession session)
    {
        if (!DateTimeOffset.TryParse(session.Owner.LeaseExpiresAtUtc, out var leaseExpiresAt))
            return false;
        return leaseExpiresAt > DateTimeOffset.UtcNow && IsProcessAlive(session.Owner.ProcessId);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsAllowedHealthUrl(string healthUrl) =>
        Uri.TryCreate(healthUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.IsLoopback
        && string.Equals(uri.AbsolutePath, "/health/ready", StringComparison.Ordinal);

    private static TargetProcessIdentity CaptureProcessIdentity(Process process)
    {
        var executablePath = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath)
            && Path.IsPathFullyQualified(process.StartInfo.FileName))
        {
            executablePath = process.StartInfo.FileName;
        }
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException($"Cannot capture executable identity for process {process.Id}.");
        return new TargetProcessIdentity(
            process.Id,
            process.StartTime.ToUniversalTime().ToString("O"),
            Path.GetFullPath(executablePath));
    }

    private static bool IsMatchingTargetProcess(DeploymentSessionTarget target)
    {
        if (target.ProcessId is not int processId
            || !DateTimeOffset.TryParse(target.ProcessStartedAtUtc, out var expectedStart)
            || string.IsNullOrWhiteSpace(target.ProcessExecutablePath))
        {
            return false;
        }
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return false;
            var actualStart = process.StartTime.ToUniversalTime();
            if ((actualStart - expectedStart.UtcDateTime).Duration() > TimeSpan.FromMilliseconds(100))
                return false;
            var actualPath = process.MainModule?.FileName;
            return actualPath is not null
                && string.Equals(
                    Path.GetFullPath(actualPath),
                    Path.GetFullPath(target.ProcessExecutablePath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryKillTargetProcess(DeploymentSessionTarget target)
    {
        if (!IsMatchingTargetProcess(target) || target.ProcessId is not int processId)
            return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return false;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task BuildServerAsync(
        LocalDeploymentPlan plan,
        string goExecutable)
    {
        var root = Path.Combine(plan.WorkDirectory, "server");
        var output = Path.Combine(
            root,
            OperatingSystem.IsWindows() ? "junexis-server.exe" : "junexis-server");
        await RunCheckedAsync(
            goExecutable,
            ["mod", "tidy"],
            root,
            "Go module resolution");
        await RunCheckedAsync(
            goExecutable,
            ["build", "-buildvcs=false", "-o", output, "."],
            root,
            "Go server build");
        await Console.Out.WriteLineAsync("BUILT server");
    }

    private static async Task BuildReportAsync(
        LocalDeploymentPlan plan,
        string npmExecutable)
    {
        var root = Path.Combine(plan.WorkDirectory, "report");
        await RunCheckedAsync(
            npmExecutable,
            ["install", "--ignore-scripts"],
            root,
            "report dependency install");
        await RunCheckedAsync(
            npmExecutable,
            ["run", "app:build"],
            root,
            "TypeScript report application build");
        await Console.Out.WriteLineAsync("BUILT report");
    }

    private static async Task RunCheckedAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string operation)
    {
        var processStart = CreateStartInfo(
            executable,
            arguments,
            workingDirectory,
            environment: null);
        using var process = Process.Start(processStart)
            ?? throw new InvalidOperationException($"Could not start {operation}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(BuildTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{operation} exceeded {BuildTimeout.TotalMinutes:0} minutes.");
        }
        var output = ((await stdout) + Environment.NewLine + (await stderr)).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{operation} failed with exit code {process.ExitCode}: {Limit(output)}");
    }

    private static LocalProcess StartProcess(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        string label,
        Action<string>? readySignal)
    {
        var process = Process.Start(CreateStartInfo(
            executable,
            arguments,
            workingDirectory,
            environment))
            ?? throw new InvalidOperationException(
                $"Could not start local {label} process.");
        var output = PumpAsync(
            process.StandardOutput,
            label,
            readySignal);
        var error = PumpAsync(
            process.StandardError,
            label,
            readySignal: null);
        return new LocalProcess(process, output, error);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        string label,
        Action<string>? readySignal)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            readySignal?.Invoke(line);
            await Console.Out.WriteLineAsync($"[{label}] {line}");
        }
    }

    private static async Task WaitForHealthAsync(Process process, int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTimeOffset.UtcNow + ReadinessTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"Server exited before readiness with code {process.ExitCode}.");
            try
            {
                using var response = await client.GetAsync(
                    $"http://127.0.0.1:{port}/health/ready");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // The listener may not have bound yet.
            }
            catch (TaskCanceledException)
            {
                // Retry until the bounded readiness deadline.
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("Server readiness probe timed out.");
    }

    private static Task WaitForHttpHealthAsync(Process process, string url, string label) =>
        WaitForHttpHealthCoreAsync(process, url, label);

    private static async Task WaitForHttpHealthCoreAsync(Process process, string url, string label)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTimeOffset.UtcNow + ReadinessTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"{label} exited before readiness with code {process.ExitCode}.");
            try
            {
                using var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(100);
        }
        throw new TimeoutException($"{label} readiness probe timed out.");
    }

    private static bool HasPackagedServer(LocalDeploymentPlan plan) =>
        File.Exists(ServerExecutablePath(plan));

    private static string ServerExecutablePath(LocalDeploymentPlan plan)
    {
        var name = OperatingSystem.IsWindows() ? "junexis-server.exe" : "junexis-server";
        var packaged = Path.Combine(plan.WorkDirectory, "server", "runtime", name);
        return File.Exists(packaged)
            ? packaged
            : Path.Combine(plan.WorkDirectory, "server", name);
    }

    private static bool HasPackagedReport(LocalDeploymentPlan plan) =>
        new[]
        {
            "index.html",
            "report.css",
            "serve.mjs",
            "junexis-report-manifest.json",
            Path.Combine("dist", "browser.js")
        }.All(relative => File.Exists(Path.Combine(
            plan.WorkDirectory,
            "report",
            "runtime",
            relative)));

    private static int ReserveSinglePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForSignalAsync(
        Task signal,
        Process process,
        string operation)
    {
        var timeout = Task.Delay(ReadinessTimeout);
        var exit = process.WaitForExitAsync();
#pragma warning disable VSTHRD003 // The signal is an executor-owned TaskCompletionSource.
        var completed = await Task.WhenAny(signal, timeout, exit);
#pragma warning restore VSTHRD003
        if (completed == signal)
            return;
        if (completed == exit)
            throw new InvalidOperationException(
                $"Process exited before {operation} with code {process.ExitCode}.");
        throw new TimeoutException($"{operation} timed out.");
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var resolved = ResolveRequiredExecutable(executable);
        var effectiveArguments = arguments.ToList();
        if (OperatingSystem.IsWindows()
            && Path.GetFileName(resolved).Equals(
                "npm.cmd",
                StringComparison.OrdinalIgnoreCase))
        {
            var nodeDirectory = Path.GetDirectoryName(resolved)!;
            var node = Path.Combine(nodeDirectory, "node.exe");
            var npmCli = Path.Combine(
                nodeDirectory,
                "node_modules",
                "npm",
                "bin",
                "npm-cli.js");
            if (File.Exists(node) && File.Exists(npmCli))
            {
                resolved = node;
                effectiveArguments.Insert(0, npmCli);
            }
        }
        var requiresShell = OperatingSystem.IsWindows()
            && Path.GetExtension(resolved) is ".cmd" or ".bat";
        var info = new ProcessStartInfo(
            requiresShell
                ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
                : resolved)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (requiresShell)
        {
            var command =
                $"\"{resolved}\" {string.Join(" ", effectiveArguments.Select(QuoteCommandArgument))}";
            info.Arguments = $"/d /c \"{command}\"";
        }
        else
        {
            foreach (var argument in effectiveArguments)
                info.ArgumentList.Add(argument);
        }
        if (environment is not null)
            foreach (var item in environment)
                info.Environment[item.Key] = item.Value;
        return info;
    }

    private static string ResolveRequiredExecutable(string executable)
    {
        if (Path.IsPathRooted(executable)
            || executable.Contains(Path.DirectorySeparatorChar)
            || executable.Contains(Path.AltDirectorySeparatorChar))
        {
            if (File.Exists(executable))
                return Path.GetFullPath(executable);
            throw new InvalidOperationException(
                $"Toolchain executable was not found: {executable}");
        }
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(
                directory,
                executable.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    ? executable
                    : executable + extension);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new InvalidOperationException(
            $"Toolchain executable was not found: {executable}");
    }

    internal static (int HttpPort, int HealthPort, int TcpPort) ReserveServerPortSet(
        string workDirectory)
    {
        var tcpPortCount = RequiredTcpBridgePortCount(workDirectory);
        var portCount = checked(2 + tcpPortCount);
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var basePort = Random.Shared.Next(20000, 60000 - portCount);
            var listeners = new List<TcpListener>(portCount);
            try
            {
                for (var offset = 0; offset < portCount; offset++)
                {
                    var listener = new TcpListener(IPAddress.Any, basePort + offset);
                    listener.Server.ExclusiveAddressUse = true;
                    listener.Start();
                    listeners.Add(listener);
                }
                return (basePort, basePort + 1, basePort + 2);
            }
            catch (SocketException)
            {
            }
            finally
            {
                foreach (var listener in listeners)
                    listener.Stop();
            }
        }
        throw new InvalidOperationException(
            $"Could not reserve a contiguous local port set for {tcpPortCount} TCP bridge listener(s).");
    }

    internal static int RequiredTcpBridgePortCount(string workDirectory)
    {
        var serverRoot = Path.Combine(workDirectory, "server");
        if (!Directory.Exists(serverRoot))
            return 1;
        var maximumOffset = -1;
        foreach (var path in Directory.EnumerateFiles(serverRoot, "*.go", SearchOption.TopDirectoryOnly))
        {
            var source = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(source, @"\*tcpBasePort\+(\d+)", RegexOptions.CultureInvariant))
            {
                if (int.TryParse(match.Groups[1].Value, out var offset))
                    maximumOffset = Math.Max(maximumOffset, offset);
            }
        }
        return Math.Max(1, maximumOffset + 1);
    }

    private static async Task StopAsync(LocalProcess owned)
    {
        var process = owned.Process;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(owned.Output, owned.Error);
        }
        catch
        {
            // Best effort: only processes created by this bounded session are touched.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string QuoteCommandArgument(string argument) =>
        argument.Any(character =>
            char.IsWhiteSpace(character)
            || character is '&' or '|' or '<' or '>')
            ? $"\"{argument.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : argument;

    private static string Limit(string text) =>
        text.Length <= 4000 ? text : text[..4000] + "…";

    private sealed record LocalProcess(
        Process Process,
        Task Output,
        Task Error);

    private sealed record TargetProcessIdentity(
        int ProcessId,
        string StartedAtUtc,
        string ExecutablePath);
}
