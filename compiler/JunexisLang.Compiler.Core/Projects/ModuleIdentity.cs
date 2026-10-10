namespace JunexisLang.Compiler.Core.Projects;

/// <summary>
/// Identifies a Junexis source module independently of its physical location.
/// </summary>
/// <param name="Value">The canonical <c>::</c>-separated module path.</param>
public sealed record ModuleIdentity(string Value)
{
    /// <summary>
    /// Creates a module identity from a source path relative to a source root.
    /// </summary>
    /// <param name="relativeSourcePath">A relative path ending in <c>.jnx</c>.</param>
    /// <returns>The canonical module identity.</returns>
    /// <exception cref="InvalidDataException">The path is absolute, escapes its root, or has invalid segments.</exception>
    public static ModuleIdentity FromRelativeSourcePath(string relativeSourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeSourcePath);

        if (Path.IsPathRooted(relativeSourcePath))
            throw new InvalidDataException($"Module source path must be relative: {relativeSourcePath}");

        var normalized = relativeSourcePath.Replace('\\', '/');
        if (!normalized.EndsWith(".jnx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Module source path must end in '.jnx': {relativeSourcePath}");

        var withoutExtension = normalized[..^4];
        var segments = withoutExtension.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || !IsIdentifier(segment)))
            throw new InvalidDataException($"Module source path contains an invalid module segment: {relativeSourcePath}");

        return new ModuleIdentity(string.Join("::", segments));
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(IsAsciiLetter(value[0]) || value[0] == '_'))
            return false;

        return value.Skip(1).All(character => IsAsciiLetter(character) || char.IsAsciiDigit(character) || character == '_');
    }

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
