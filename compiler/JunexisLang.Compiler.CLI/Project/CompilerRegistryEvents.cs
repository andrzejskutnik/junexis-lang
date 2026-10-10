namespace JunexisLang.Compiler.CLI.Project;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

internal interface ICompilerRegistryEventSink
{
    ValueTask WriteAsync(CompilerRegistryEvent value, CancellationToken cancellationToken);
}

internal sealed class TraceCompilerRegistryEventSink : ICompilerRegistryEventSink
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ValueTask WriteAsync(CompilerRegistryEvent value, CancellationToken cancellationToken)
    {
        System.Diagnostics.Trace.TraceInformation(JsonSerializer.Serialize(value, Options));
        return ValueTask.CompletedTask;
    }
}

internal sealed class CompilerRegistryEventEmitter(
    ICompilerRegistryEventSink sink,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    internal async ValueTask<CompilerRegistryEventOperation> BeginAsync(
        string correlationId,
        string operation,
        CompilerRegistryEventArtifact? artifact,
        CancellationToken cancellationToken)
    {
        var startedAt = clock.GetTimestamp();
        await sink.WriteAsync(Create(correlationId, operation, "started", artifact), cancellationToken);
        return new(this, correlationId, operation, artifact, startedAt);
    }

    internal ValueTask CompleteAsync(
        CompilerRegistryEventOperation operation,
        string outcome,
        string? errorCode,
        string? snapshotId)
    {
        var elapsed = clock.GetElapsedTime(operation.StartedAt);
        var durationMs = Math.Clamp((long)Math.Round(elapsed.TotalMilliseconds), 0, 86_400_000);
        return sink.WriteAsync(
            Create(
                operation.CorrelationId,
                operation.Operation,
                "completed",
                operation.Artifact,
                outcome,
                durationMs,
                errorCode,
                snapshotId),
            CancellationToken.None);
    }

    private CompilerRegistryEvent Create(
        string correlationId,
        string operation,
        string phase,
        CompilerRegistryEventArtifact? artifact,
        string? outcome = null,
        long? durationMs = null,
        string? errorCode = null,
        string? snapshotId = null) => new(
            "junexis.registry-event/0.1",
            Guid.NewGuid().ToString("D"),
            correlationId,
            clock.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
            "compiler",
            operation,
            phase,
            outcome,
            durationMs,
            errorCode,
            snapshotId,
            artifact);
}

internal sealed record CompilerRegistryEventOperation(
    CompilerRegistryEventEmitter Emitter,
    string CorrelationId,
    string Operation,
    CompilerRegistryEventArtifact? Artifact,
    long StartedAt)
{
    internal ValueTask CompleteAsync(
        string outcome,
        string? errorCode = null,
        string? snapshotId = null) => Emitter.CompleteAsync(this, outcome, errorCode, snapshotId);
}

internal sealed record CompilerRegistryEventArtifact(
    string ArtifactType,
    string Id,
    string Version,
    string Sha256);

internal sealed record CompilerRegistryEvent(
    string SchemaVersion,
    string EventId,
    string CorrelationId,
    string OccurredAtUtc,
    string Producer,
    string Operation,
    string Phase,
    string? Outcome,
    long? DurationMs,
    string? ErrorCode,
    string? SnapshotId,
    CompilerRegistryEventArtifact? Artifact);
