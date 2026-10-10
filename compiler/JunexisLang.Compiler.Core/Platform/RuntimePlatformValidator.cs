namespace JunexisLang.Compiler.Core.Platform;

using System.Globalization;
using JunexisLang.Compiler.Core.Diagnostics;

/// <summary>
/// Validates platform capabilities at runtime during task execution.
/// Complements compile-time validation with dynamic checks.
/// </summary>
public sealed class RuntimePlatformValidator
{
    private readonly PlatformCapabilities _capabilities;
    private readonly List<CompilerDiagnostic> _diagnostics;
    private long _currentMemoryUsage;
    private int _currentTaskCount;

    /// <summary>
    /// Gets the list of diagnostics collected during validation.
    /// </summary>
    public IReadOnlyList<CompilerDiagnostic> Diagnostics => _diagnostics.AsReadOnly();

    /// <summary>
    /// Initializes a new runtime validator for a single platform profile.
    /// </summary>
    /// <param name="capabilities">Platform capabilities to validate against.</param>
    /// <param name="diagnostics">Target diagnostics collection.</param>
    public RuntimePlatformValidator(PlatformCapabilities capabilities, List<CompilerDiagnostic> diagnostics)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _currentMemoryUsage = 0;
        _currentTaskCount = 0;
    }

    /// <summary>
    /// Validates runtime memory usage against platform limits.
    /// Emits E5004 if exceeded and W5005 if usage is above 80%.
    /// </summary>
    /// <param name="currentMemoryBytes">Current memory usage in bytes.</param>
    /// <param name="taskName">Task name.</param>
    /// <param name="line">Source line.</param>
    /// <param name="column">Source column.</param>
    public void ValidateRuntimeMemoryUsage(long currentMemoryBytes, string taskName, int line, int column)
    {
        if (currentMemoryBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentMemoryBytes), "Memory usage cannot be negative");
        }

        if (string.IsNullOrWhiteSpace(taskName))
        {
            throw new ArgumentException("Task name cannot be null or empty", nameof(taskName));
        }

        _currentMemoryUsage = currentMemoryBytes;

        var maxMemory = _capabilities.MaxMemoryBytes;
        var usagePercentage = (double)currentMemoryBytes / maxMemory * 100;
        var location = $"{taskName}:{line}:{column}";

        if (currentMemoryBytes > maxMemory)
        {
            var message = $"Runtime memory usage {FormatBytes(currentMemoryBytes)} exceeds platform limit {FormatBytes(maxMemory)} " +
                          $"({usagePercentage.ToString("F1", CultureInfo.InvariantCulture)}% of limit) in task '{taskName}'";

            _diagnostics.Add(new CompilerDiagnostic(
                DiagnosticSeverity.Error,
                "E5004",
                message,
                location,
                relatedId: taskName,
                metadata: new Dictionary<string, object>
                {
                    ["CurrentMemory"] = currentMemoryBytes,
                    ["MaxMemory"] = maxMemory,
                    ["UsagePercentage"] = usagePercentage,
                }));
        }
        else if (usagePercentage > 80.0)
        {
            var message = $"Runtime memory usage {FormatBytes(currentMemoryBytes)} is high " +
                          $"({usagePercentage.ToString("F1", CultureInfo.InvariantCulture)}% of {FormatBytes(maxMemory)} limit) in task '{taskName}'";

            _diagnostics.Add(new CompilerDiagnostic(
                DiagnosticSeverity.Warning,
                "W5005",
                message,
                location,
                relatedId: taskName,
                metadata: new Dictionary<string, object>
                {
                    ["CurrentMemory"] = currentMemoryBytes,
                    ["MaxMemory"] = maxMemory,
                    ["UsagePercentage"] = usagePercentage,
                }));
        }
    }

    /// <summary>
    /// Validates runtime feature access against platform capabilities.
    /// Emits E5006 if the feature is not supported.
    /// </summary>
    /// <param name="featureName">Feature name.</param>
    /// <param name="taskName">Task name.</param>
    /// <param name="line">Source line.</param>
    /// <param name="column">Source column.</param>
    public void ValidateRuntimeFeatureAccess(string featureName, string taskName, int line, int column)
    {
        if (string.IsNullOrWhiteSpace(featureName))
        {
            throw new ArgumentException("Feature name cannot be null or empty", nameof(featureName));
        }

        if (string.IsNullOrWhiteSpace(taskName))
        {
            throw new ArgumentException("Task name cannot be null or empty", nameof(taskName));
        }

        var location = $"{taskName}:{line}:{column}";
        var featureSupported = featureName.ToLowerInvariant() switch
        {
            "threading" => _capabilities.SupportsThreading,
            "fileio" => _capabilities.SupportsFileIO,
            "networking" => _capabilities.SupportsNetworking,
            _ => throw new ArgumentException($"Unknown feature: {featureName}", nameof(featureName)),
        };

        if (!featureSupported)
        {
            var message = $"Runtime access to feature '{featureName}' denied - not supported by platform " +
                          $"'{_capabilities.Platform}' in task '{taskName}'";

            _diagnostics.Add(new CompilerDiagnostic(
                DiagnosticSeverity.Error,
                "E5006",
                message,
                location,
                relatedId: taskName,
                metadata: new Dictionary<string, object>
                {
                    ["FeatureName"] = featureName,
                    ["PlatformName"] = _capabilities.Platform,
                }));
        }
    }

    /// <summary>
    /// Validates runtime concurrent task count against platform limits.
    /// Emits E5007 if exceeded and W5008 if usage is above 80%.
    /// </summary>
    /// <param name="currentTaskCount">Current concurrent task count.</param>
    /// <param name="taskName">Task name.</param>
    /// <param name="line">Source line.</param>
    /// <param name="column">Source column.</param>
    public void ValidateRuntimeConcurrentTasks(int currentTaskCount, string taskName, int line, int column)
    {
        if (currentTaskCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentTaskCount), "Task count cannot be negative");
        }

        if (string.IsNullOrWhiteSpace(taskName))
        {
            throw new ArgumentException("Task name cannot be null or empty", nameof(taskName));
        }

        _currentTaskCount = currentTaskCount;

        var maxTasks = _capabilities.MaxConcurrentTasks;
        var usagePercentage = (double)currentTaskCount / maxTasks * 100;
        var location = $"{taskName}:{line}:{column}";

        if (currentTaskCount > maxTasks)
        {
            var message = $"Runtime concurrent task count {currentTaskCount} exceeds platform limit {maxTasks} " +
                          $"({usagePercentage.ToString("F1", CultureInfo.InvariantCulture)}% of limit) when starting task '{taskName}'";

            _diagnostics.Add(new CompilerDiagnostic(
                DiagnosticSeverity.Error,
                "E5007",
                message,
                location,
                relatedId: taskName,
                metadata: new Dictionary<string, object>
                {
                    ["CurrentTaskCount"] = currentTaskCount,
                    ["MaxConcurrentTasks"] = maxTasks,
                    ["UsagePercentage"] = usagePercentage,
                }));
        }
        else if (usagePercentage > 80.0)
        {
            var message = $"Runtime concurrent task count {currentTaskCount} is high " +
                          $"({usagePercentage.ToString("F1", CultureInfo.InvariantCulture)}% of {maxTasks} limit) when starting task '{taskName}'";

            _diagnostics.Add(new CompilerDiagnostic(
                DiagnosticSeverity.Warning,
                "W5008",
                message,
                location,
                relatedId: taskName,
                metadata: new Dictionary<string, object>
                {
                    ["CurrentTaskCount"] = currentTaskCount,
                    ["MaxConcurrentTasks"] = maxTasks,
                    ["UsagePercentage"] = usagePercentage,
                }));
        }
    }

    /// <summary>
    /// Gets the current runtime memory usage in bytes.
    /// </summary>
    public long CurrentMemoryUsage => _currentMemoryUsage;

    /// <summary>
    /// Gets the current runtime task count.
    /// </summary>
    public int CurrentTaskCount => _currentTaskCount;

    /// <summary>
    /// Resets runtime tracking counters.
    /// </summary>
    public void ResetCounters()
    {
        _currentMemoryUsage = 0;
        _currentTaskCount = 0;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024} KB";
        }

        if (bytes < 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024 * 1024)} MB";
        }

        return $"{bytes / (1024 * 1024 * 1024)} GB";
    }
}