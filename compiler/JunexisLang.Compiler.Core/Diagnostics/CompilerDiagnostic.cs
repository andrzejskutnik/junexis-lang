namespace JunexisLang.Compiler.Core.Diagnostics;

/// <summary>
/// Represents a compiler diagnostic (error, warning, or information message).
/// </summary>
public sealed class CompilerDiagnostic
{
    /// <summary>
    /// Diagnostic severity level.
    /// </summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>
    /// Diagnostic code (e.g., "E5001", "W5002", "E5003").
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Human-readable diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Location in source code (file:line:column or Line X, Column Y), if available.
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// Related task or component ID.
    /// </summary>
    public string? RelatedId { get; }

    /// <summary>
    /// Additional context or metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; }

    /// <summary>
    /// Initializes a new compiler diagnostic.
    /// </summary>
    /// <param name="severity">Diagnostic severity.</param>
    /// <param name="code">Diagnostic code.</param>
    /// <param name="message">Diagnostic message.</param>
    /// <param name="location">Optional location string.</param>
    /// <param name="relatedId">Optional related identifier.</param>
    /// <param name="metadata">Optional metadata.</param>
    public CompilerDiagnostic(
        DiagnosticSeverity severity,
        string code,
        string message,
        string? location = null,
        string? relatedId = null,
        Dictionary<string, object>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Diagnostic code cannot be empty.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Diagnostic message cannot be empty.", nameof(message));
        }

        Severity = severity;
        Code = code;
        Message = message;
        Location = location;
        RelatedId = relatedId;
        Metadata = metadata ?? new Dictionary<string, object>();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var location = Location != null ? $"{Location}: " : string.Empty;
        return $"{location}{Severity} {Code}: {Message}";
    }
}

/// <summary>
/// Diagnostic severity levels.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Compilation error - prevents code generation.
    /// </summary>
    Error,

    /// <summary>
    /// Warning - potential issue but compilation continues.
    /// </summary>
    Warning,

    /// <summary>
    /// Informational message - optimization suggestion or insight.
    /// </summary>
    Information
}