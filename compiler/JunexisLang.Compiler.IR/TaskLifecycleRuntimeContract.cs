namespace JunexisLang.Compiler.IR;

/// <summary>
/// Defines the backend-independent lifecycle, state and time guarantees used by
/// semantic conformance scenarios.
/// </summary>
public static class TaskLifecycleRuntimeContract
{
    /// <summary>Version of the lifecycle runtime contract.</summary>
    public const string Version = "junexis.task-lifecycle/0.1";

    /// <summary>Lifecycle event emitted once before any periodic execution.</summary>
    public const string StartEvent = "start";

    /// <summary>Lifecycle event emitted for a completed periodic execution.</summary>
    public const string TickEvent = "tick";

    /// <summary>Lifecycle event emitted once after cancellation is observed.</summary>
    public const string StopEvent = "stop";

    /// <summary>
    /// Gets the canonical observable lifecycle for a bounded periodic scenario.
    /// Cancellation is observed between completed ticks and never interrupts a
    /// state transition in progress.
    /// </summary>
    /// <param name="completedTicks">Number of completed periodic executions.</param>
    /// <returns>Canonical ordered lifecycle event names.</returns>
    public static IReadOnlyList<string> CanonicalLifecycle(int completedTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedTicks);
        return [StartEvent, .. Enumerable.Repeat(TickEvent, completedTicks), StopEvent];
    }
}
