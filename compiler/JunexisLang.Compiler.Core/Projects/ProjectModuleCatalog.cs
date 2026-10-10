namespace JunexisLang.Compiler.Core.Projects;

/// <summary>
/// Describes one source module discovered beneath a project source root.
/// </summary>
/// <param name="Identity">The canonical logical identity.</param>
/// <param name="SourceFile">The physical source file.</param>
/// <param name="SourceRoot">The source root that owns the file.</param>
public sealed record ProjectModule(
    ModuleIdentity Identity,
    FileInfo SourceFile,
    DirectoryInfo SourceRoot);

/// <summary>Defines a physical module source root and an optional logical namespace prefix.</summary>
/// <param name="Directory">The physical source directory.</param>
/// <param name="NamespacePrefix">The namespace prepended to every discovered module.</param>
public sealed record ModuleSourceRoot(DirectoryInfo Directory, ModuleIdentity? NamespacePrefix = null);

/// <summary>
/// Provides a deterministic, portable mapping between Junexis module identities and source files.
/// </summary>
public sealed class ProjectModuleCatalog
{
    private readonly IReadOnlyDictionary<string, ProjectModule> exactModules;
    private readonly IReadOnlyDictionary<string, ProjectModule> portableModules;

    private ProjectModuleCatalog(IReadOnlyList<ProjectModule> modules)
    {
        Modules = modules;
        exactModules = modules.ToDictionary(module => module.Identity.Value, StringComparer.Ordinal);
        portableModules = modules.ToDictionary(module => module.Identity.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets modules ordered by canonical identity and then physical path.
    /// </summary>
    public IReadOnlyList<ProjectModule> Modules { get; }

    /// <summary>
    /// Discovers every <c>.jnx</c> module under the configured project source roots.
    /// </summary>
    /// <param name="projectRoot">The project root containing the manifest.</param>
    /// <param name="sourceRoots">Source root paths relative to the project root.</param>
    /// <returns>A deterministic module catalog.</returns>
    /// <exception cref="InvalidDataException">A root is invalid or two sources have a non-portable identity collision.</exception>
    public static ProjectModuleCatalog Discover(DirectoryInfo projectRoot, IEnumerable<string> sourceRoots)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(sourceRoots);

        var projectPath = Normalize(projectRoot.FullName);
        var roots = sourceRoots.Select(root => ResolveSourceRoot(projectPath, root)).ToArray();
        if (roots.Length == 0)
            throw new InvalidDataException("A Junexis project must declare at least one source root.");

        var duplicateRoot = roots
            .GroupBy(root => root.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRoot is not null)
            throw new InvalidDataException($"Duplicate project source root: {duplicateRoot.Key}");

        return Discover(roots.Select(root => new ModuleSourceRoot(root)));
    }

    /// <summary>Discovers modules across validated project and local-library roots.</summary>
    /// <param name="sourceRoots">Physical roots with optional library namespace prefixes.</param>
    /// <returns>A deterministic combined module catalog.</returns>
    public static ProjectModuleCatalog Discover(IEnumerable<ModuleSourceRoot> sourceRoots)
    {
        ArgumentNullException.ThrowIfNull(sourceRoots);
        var roots = sourceRoots.ToArray();
        if (roots.Length == 0)
            throw new InvalidDataException("A Junexis project must declare at least one source root.");

        var duplicateRoot = roots.GroupBy(root => root.Directory.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRoot is not null)
            throw new InvalidDataException($"Duplicate module source root: {duplicateRoot.Key}");

        var modules = roots
            .SelectMany(DiscoverRoot)
            .OrderBy(module => module.Identity.Value, StringComparer.Ordinal)
            .ThenBy(module => module.SourceFile.FullName, StringComparer.Ordinal)
            .ToArray();

        var multiplyOwnedSource = modules
            .GroupBy(module => module.SourceFile.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (multiplyOwnedSource is not null)
        {
            var owners = string.Join(", ", multiplyOwnedSource.Select(module => module.SourceRoot.FullName));
            throw new InvalidDataException(
                $"Module source is owned by overlapping source roots: {multiplyOwnedSource.Key} ({owners})");
        }

        var collision = modules
            .GroupBy(module => module.Identity.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
        {
            var paths = string.Join(", ", collision.Select(module => module.SourceFile.FullName));
            throw new InvalidDataException(
                $"Module identity '{collision.Key}' is ambiguous or differs only by letter case: {paths}");
        }

        return new ProjectModuleCatalog(modules);
    }

    /// <summary>
    /// Resolves an exact, case-sensitive module identity.
    /// </summary>
    /// <param name="identity">The canonical module identity.</param>
    /// <param name="module">The resolved source module when found.</param>
    /// <returns><see langword="true"/> when the exact identity exists.</returns>
    public bool TryResolve(ModuleIdentity identity, out ProjectModule? module)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return exactModules.TryGetValue(identity.Value, out module);
    }

    /// <summary>
    /// Finds a module using portable case-insensitive comparison for diagnostic purposes.
    /// </summary>
    /// <param name="identity">The requested module identity.</param>
    /// <param name="module">The matching source module when found.</param>
    /// <returns><see langword="true"/> when an identity differs at most by letter case.</returns>
    public bool TryResolvePortable(ModuleIdentity identity, out ProjectModule? module)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return portableModules.TryGetValue(identity.Value, out module);
    }

    private static IEnumerable<ProjectModule> DiscoverRoot(ModuleSourceRoot sourceRoot)
    {
        if (!sourceRoot.Directory.Exists)
            throw new InvalidDataException($"Project source root not found: {sourceRoot.Directory.FullName}");

        foreach (var path in Directory.EnumerateFiles(sourceRoot.Directory.FullName, "*.jnx", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot.Directory.FullName, path);
            var relativeIdentity = ModuleIdentity.FromRelativeSourcePath(relativePath);
            var identity = sourceRoot.NamespacePrefix is null
                ? relativeIdentity
                : new ModuleIdentity($"{sourceRoot.NamespacePrefix}::{relativeIdentity}");
            yield return new ProjectModule(
                identity,
                new FileInfo(Path.GetFullPath(path)),
                sourceRoot.Directory);
        }
    }

    private static DirectoryInfo ResolveSourceRoot(string projectPath, string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || Path.IsPathRooted(sourceRoot))
            throw new InvalidDataException($"Project source root must be a non-empty relative path: {sourceRoot}");

        var resolved = Normalize(Path.Combine(projectPath, sourceRoot));
        if (!IsWithin(projectPath, resolved))
            throw new InvalidDataException($"Project source root escapes the project: {sourceRoot}");

        return new DirectoryInfo(resolved);
    }

    private static bool IsWithin(string root, string candidate) =>
        candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
