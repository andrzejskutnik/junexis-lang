using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Ast;
using JunexisLang.Compiler.Core.Projects;
using JunexisLang.Compiler.Topology;

namespace JunexisLang.Compiler.LspHost;

/// <summary>
/// Thread-safe store otwartych dokumentów.
/// Każdy dokument = (surowy tekst, sparsowany AST, lista diagnostyk).
/// </summary>
internal sealed class DocumentStore
{
    private sealed record Entry(
        string                    Source,
        ProgramNode?              Ast,
        IReadOnlyList<Diagnostic> Diagnostics);

    private readonly ConcurrentDictionary<string, Entry> _docs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, StorageQueryIdeSymbol>>
        _storageQuerySymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly JunexisCompiler _compiler = new();

    // ── Aktualizacja ─────────────────────────────────────────────────────

    public async Task UpdateAsync(string uri, string source)
    {
        var path   = UriToPath(uri);
        await StoreAsync(uri, path, source);
        await LoadProjectDocumentsAsync(path, uri);
    }

    private async Task StoreAsync(string uri, string path, string source)
    {
        var result = await _compiler.CheckSourceAsync(source, path);
        var diagnostics = result.Diagnostics;

        if (result.Success && result.Ast is not null)
        {
            var structural = StructuralAnalysisPipeline.Analyze(result.Ast);
            diagnostics = diagnostics.Concat(structural.Diagnostics).ToList();
        }

        _docs[uri] = new Entry(source, result.Ast, diagnostics);
        _storageQuerySymbols[uri] = result.Ast is null
            ? new Dictionary<string, StorageQueryIdeSymbol>(StringComparer.Ordinal)
            : StorageQueryIdeIndex.FromProgram(result.Ast);
    }

    private async Task LoadProjectDocumentsAsync(string activePath, string activeUri)
    {
        var root = FindProjectRoot(Path.GetDirectoryName(activePath));
        if (root is null)
            return;

        foreach (var path in ReadSourceRoots(root)
                     .Select(sourceRoot => Path.GetFullPath(Path.Combine(root, sourceRoot)))
                     .Where(sourceRoot => sourceRoot.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                     .Where(Directory.Exists)
                     .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.jnx", SearchOption.AllDirectories))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var uri = PathToUri(path);
            if (uri.Equals(activeUri, StringComparison.OrdinalIgnoreCase) || _docs.ContainsKey(uri))
                continue;
            await StoreAsync(uri, path, await File.ReadAllTextAsync(path));
        }

        await RefreshProjectDiagnosticsAsync(root);
    }

    private async Task RefreshProjectDiagnosticsAsync(string root)
    {
        try
        {
            var catalog = ProjectModuleCatalog.Discover(
                new DirectoryInfo(root), ReadSourceRoots(root));
            var resolution = await ProjectImportResolver.ResolveAsync(catalog);
            if (!resolution.Success)
            {
                MergeProjectDiagnostics(resolution.Diagnostics);
                return;
            }

            var binding = ProjectSymbolBinder.Bind(resolution.Graph);
            if (!binding.Success)
            {
                MergeProjectDiagnostics(binding.Diagnostics);
                return;
            }

            foreach (var document in resolution.Graph.Modules)
            {
                var uri = PathToUri(document.Module.SourceFile.FullName);
                if (!_docs.TryGetValue(uri, out var entry) || entry.Ast is null)
                    continue;
                var scope = binding.Scopes[document.Module.Identity.Value].Values;
                var check = _compiler.CheckAst(entry.Ast, scope);
                var structural = StructuralAnalysisPipeline.Analyze(entry.Ast);
                _docs[uri] = entry with
                {
                    Diagnostics = check.Diagnostics.Concat(structural.Diagnostics).ToArray()
                };
                _storageQuerySymbols[uri] = StorageQueryIdeIndex.FromScope(scope);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // File-local language features remain available when the workspace cannot be indexed.
        }
    }

    private void MergeProjectDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (var group in diagnostics.Where(diagnostic => diagnostic.File is not null)
                     .GroupBy(diagnostic => PathToUri(diagnostic.File!), StringComparer.OrdinalIgnoreCase))
        {
            if (_docs.TryGetValue(group.Key, out var entry))
                _docs[group.Key] = entry with { Diagnostics = entry.Diagnostics.Concat(group).ToArray() };
        }
    }

    public void Remove(string uri)
    {
        _docs.TryRemove(uri, out _);
        _storageQuerySymbols.TryRemove(uri, out _);
    }

    // ── Odczyt ───────────────────────────────────────────────────────────

    public (string? Source, ProgramNode? Ast, IReadOnlyList<Diagnostic> Diagnostics) Get(string uri)
    {
        if (_docs.TryGetValue(uri, out var e))
            return (e.Source, e.Ast, e.Diagnostics);
        return (null, null, []);
    }

    /// <summary>Zwraca stabilny snapshot tekstu wszystkich otwartych dokumentów.</summary>
    public IReadOnlyList<(string Uri, string Source)> Snapshot() =>
        _docs.Select(item => (item.Key, item.Value.Source)).ToArray();

    /// <summary>Returns a stable snapshot of parsed workspace documents.</summary>
    public IReadOnlyList<(string Uri, ProgramNode Ast)> ParsedSnapshot() =>
        _docs.Where(item => item.Value.Ast is not null)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => (item.Key, item.Value.Ast!))
            .ToArray();

    /// <summary>Zwraca pochodne symbole named query widoczne w zakresie dokumentu.</summary>
    public IReadOnlyList<StorageQueryIdeSymbol> StorageQuerySymbols(string uri) =>
        _storageQuerySymbols.TryGetValue(uri, out var symbols)
            ? symbols.Values.OrderBy(symbol => symbol.Name, StringComparer.Ordinal).ToArray()
            : [];

    /// <summary>Znajduje pochodny symbol named query widoczny pod wskazaną nazwą.</summary>
    public StorageQueryIdeSymbol? FindStorageQuerySymbol(string uri, string name) =>
        _storageQuerySymbols.TryGetValue(uri, out var symbols) && symbols.TryGetValue(name, out var symbol)
            ? symbol
            : null;

    // ── URI → ścieżka pliku ───────────────────────────────────────────────

    private static string UriToPath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var documentUri) && documentUri.IsFile)
            return documentUri.LocalPath;
        return uri;
    }

    private static string PathToUri(string path) =>
        new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static string? FindProjectRoot(string? start)
    {
        for (var directory = string.IsNullOrWhiteSpace(start) ? null : new DirectoryInfo(start);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "junexis.project.toml")))
                return directory.FullName;
        }
        return null;
    }

    private static IReadOnlyList<string> ReadSourceRoots(string projectRoot)
    {
        var manifest = File.ReadAllText(Path.Combine(projectRoot, "junexis.project.toml"));
        var assignment = Regex.Match(
            manifest,
            @"(?m)^\s*sourceRoots\s*=\s*\[(?<value>[^\]]*)\]");
        if (!assignment.Success)
            return ["src"];

        var values = Regex.Matches(assignment.Groups["value"].Value, "\"(?<value>[^\"]+)\"")
            .Select(match => match.Groups["value"].Value)
            .ToArray();
        return values.Length == 0 ? ["src"] : values;
    }
}
