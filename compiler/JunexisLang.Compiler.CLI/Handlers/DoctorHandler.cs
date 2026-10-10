using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class DoctorHandler
{
    internal static async Task<int> HandleAsync(string? path)
    {
        var reporter = new DoctorReporter();
        var inputPath = string.IsNullOrWhiteSpace(path)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(path);

        reporter.Info($"Junexis doctor: {inputPath}");

        if (File.Exists(inputPath))
            return await DiagnoseFileAsync(new FileInfo(inputPath), reporter);

        var startDirectory = new DirectoryInfo(inputPath);
        if (!startDirectory.Exists)
        {
            reporter.Error($"path not found: {inputPath}");
            return reporter.ExitCode;
        }

        return await DiagnoseProjectAsync(startDirectory, reporter);
    }

    private static async Task<int> DiagnoseFileAsync(FileInfo inputFile, DoctorReporter reporter)
    {
        if (!inputFile.Extension.Equals(".jnx", StringComparison.OrdinalIgnoreCase))
            reporter.Warn("input is a file, but it is not a .jnx source file");

        reporter.Warn("file mode compatibility path; project-first workflow expects junexis.project.toml");
        reporter.Ok($"source file: {inputFile.FullName}");
        reporter.Info($"default file-mode out: {Path.Combine(inputFile.DirectoryName ?? ".", ".junexis", "out", Path.GetFileNameWithoutExtension(inputFile.Name))}");

        var project = await JunexisProjectManifest.DiscoverAsync(inputFile.Directory ?? new DirectoryInfo(Environment.CurrentDirectory));
        if (project is not null)
            reporter.Info($"nearest project manifest: {project.ManifestFile.FullName}");

        await CheckSourceAsync(inputFile, reporter);
        return reporter.ExitCode;
    }

    private static async Task<int> DiagnoseProjectAsync(DirectoryInfo startDirectory, DoctorReporter reporter)
    {
        JunexisProject? project;
        try
        {
            project = await JunexisProjectManifest.DiscoverAsync(startDirectory);
        }
        catch (InvalidDataException ex)
        {
            reporter.Error(ex.Message);
            return reporter.ExitCode;
        }

        if (project is null)
        {
            reporter.Error($"project manifest '{JunexisProjectManifest.FileName}' not found from directory: {startDirectory.FullName}");
            return reporter.ExitCode;
        }

        reporter.Ok($"project root: {project.RootDirectory.FullName}");
        reporter.Ok($"manifest: {project.ManifestFile.FullName}");
        reporter.Ok($"name: {project.Manifest.Name}");

        DiagnoseEntry(project, reporter);
        DiagnoseTargets(project, reporter);
        DiagnoseOutDir(project, reporter);
        DiagnoseGeneratedState(project, reporter);
        DiagnoseProjectOverlays(project, reporter);
        await DiagnoseProviderPackagesAsync(project, reporter);

        if (project.EntryFile.Exists)
        {
            await CheckSourceAsync(project.EntryFile, reporter);
            await DiagnoseTargetApplicabilityAsync(project, reporter);
            await DiagnoseExternOwnershipAsync(project, reporter);
        }

        return reporter.ExitCode;
    }

    private static async Task DiagnoseProviderPackagesAsync(
        JunexisProject project,
        DoctorReporter reporter)
    {
        try
        {
            var resolution = await ProviderPackageResolver.ResolveAsync(project);
            foreach (var package in resolution.Packages)
                reporter.Ok($"provider package: {package.Manifest.Id}@{package.Manifest.Version} [{package.PackageSha256[..12]}]");
            foreach (var binding in resolution.Bindings)
                reporter.Ok($"storage binding: {binding.Binding.Name} -> {binding.Package.Manifest.Id}@{binding.Package.Manifest.Version}");
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
        {
            reporter.Error(ex.Message);
        }
    }

    private static void DiagnoseEntry(JunexisProject project, DoctorReporter reporter)
    {
        if (project.EntryFile.Exists)
            reporter.Ok($"entry: {project.EntryFile.FullName}");
        else
            reporter.Error($"entry file not found: {project.EntryFile.FullName}");
    }

    private static void DiagnoseTargets(JunexisProject project, DoctorReporter reporter)
    {
        foreach (var target in project.Manifest.Targets)
        {
            if (CodeGeneratorFactory.ExecutionPlatforms.Contains(target))
                reporter.Ok($"target: {target}");
            else
                reporter.Error($"unknown target in manifest: {target}");
        }
    }

    private static async Task DiagnoseTargetApplicabilityAsync(
        JunexisProject project,
        DoctorReporter reporter)
    {
        var parse = await new JunexisCompiler().ParseAsync(new ParseOptions(project.EntryFile.FullName));
        if (!parse.Success || parse.Ast is null)
            return;

        var annotated = parse.Ast.Declarations
            .OfType<TaskDeclarationNode>()
            .SelectMany(task => task.Platforms)
            .Concat(parse.Ast.Declarations.OfType<ExternTaskNode>()
                .SelectMany(task => task.Platforms))
            .Concat(parse.Ast.Declarations.OfType<ExternTypeNode>()
                .SelectMany(type => type.Platforms))
            .Select(platform => platform.Target);
        var contracted = parse.Ast.Declarations
            .OfType<ExternContractNode>()
            .Select(contract => contract.Target)
            .OfType<string>();
        var applicable = annotated.Concat(contracted)
            .Where(CodeGeneratorFactory.ExecutionPlatforms.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var target in project.Manifest.Targets
                     .Where(CodeGeneratorFactory.ExecutionPlatforms.Contains)
                     .Where(target => !applicable.Contains(target)))
        {
            reporter.Error(
                $"target declared in manifest has no applicable task: {target}");
        }

        foreach (var target in applicable
                     .Where(target => !project.Manifest.Targets.Contains(
                         target, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(target => target, StringComparer.Ordinal))
        {
            reporter.Info(
                $"source target is not selected by the project manifest: {target}");
        }
    }

    private static void DiagnoseOutDir(JunexisProject project, DoctorReporter reporter)
    {
        var root = Normalize(project.RootDirectory.FullName);
        var outDir = Normalize(project.OutDirectory.FullName);
        var srcDir = Normalize(Path.Combine(project.RootDirectory.FullName, "src"));

        if (!outDir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            reporter.Error($"outDir points outside project root: {project.OutDirectory.FullName}");
            return;
        }

        if (outDir.Equals(root, StringComparison.OrdinalIgnoreCase))
            reporter.Error("outDir cannot be the project root");
        else if (outDir.Equals(srcDir, StringComparison.OrdinalIgnoreCase) ||
                 outDir.StartsWith(srcDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            reporter.Error($"outDir cannot point inside src/: {project.OutDirectory.FullName}");
        else
            reporter.Ok($"outDir: {project.OutDirectory.FullName}");
    }

    private static void DiagnoseGeneratedState(JunexisProject project, DoctorReporter reporter)
    {
        var legacyJson = Path.Combine(project.RootDirectory.FullName, "junexis.project.json");
        if (File.Exists(legacyJson))
            reporter.Warn($"legacy manifest candidate present; TOML is canonical: {legacyJson}");

        var buildDir = Path.Combine(project.RootDirectory.FullName, ".junexis", "build");
        var diagnosticsDir = Path.Combine(project.RootDirectory.FullName, ".junexis", "diagnostics");
        reporter.Info(Directory.Exists(project.OutDirectory.FullName)
            ? $"generated out present: {project.OutDirectory.FullName}"
            : $"generated out not present: {project.OutDirectory.FullName}");
        reporter.Info(Directory.Exists(buildDir)
            ? $"build scratch present: {buildDir}"
            : $"build scratch not present: {buildDir}");
        reporter.Info(Directory.Exists(diagnosticsDir)
            ? $"diagnostics present: {diagnosticsDir}"
            : $"diagnostics not present: {diagnosticsDir}");
    }

    private static void DiagnoseProjectOverlays(JunexisProject project, DoctorReporter reporter)
    {
        foreach (var overlay in ProjectOverlays)
        {
            var userFile = Path.Combine(project.RootDirectory.FullName, overlay.UserPath);
            if (File.Exists(userFile))
                reporter.Info($"project overlay: {NormalizeOverlayPath(overlay.UserPath)} -> out/{NormalizeOverlayPath(overlay.GeneratedPath)}");
        }

        var reportPackage = Path.Combine(project.RootDirectory.FullName, "user", "report", "package.json");
        if (File.Exists(reportPackage))
        {
            if (ReportPackageCustomization.TryValidate(File.ReadAllText(reportPackage), out var error))
                reporter.Ok("report package customization: valid");
            else
                reporter.Error($"invalid user/report/package.json: {error}");
        }
        var reportView = Path.Combine(project.RootDirectory.FullName, "user", "report", "view.json");
        if (File.Exists(reportView))
        {
            if (ReportViewCustomization.TryValidate(File.ReadAllText(reportView), out var error))
                reporter.Ok("declarative report view: valid");
            else
                reporter.Error($"invalid user/report/view.json: {error}");
        }
    }

    private static async Task CheckSourceAsync(FileInfo sourceFile, DoctorReporter reporter)
    {
        var result = await new JunexisCompiler().CheckAsync(new CheckOptions(sourceFile.FullName));
        foreach (var diagnostic in result.Diagnostics)
        {
            var location = $"{diagnostic.File}:{diagnostic.Line}:{diagnostic.Column}";
            if (diagnostic.Level == DiagnosticLevel.Error)
                reporter.Error($"{diagnostic.Code} {location}: {diagnostic.Message}");
            else if (diagnostic.Level == DiagnosticLevel.Warning)
                reporter.Warn($"{diagnostic.Code} {location}: {diagnostic.Message}");
            else
                reporter.Info($"{diagnostic.Code} {location}: {diagnostic.Message}");
        }

        if (result.Success)
            reporter.Ok("source check: pass");
        else
            reporter.Error("source check: failed");
    }

    private static async Task DiagnoseExternOwnershipAsync(JunexisProject project, DoctorReporter reporter)
    {
        var parse = await new JunexisCompiler().ParseAsync(new ParseOptions(project.EntryFile.FullName));
        if (!parse.Success || parse.Ast is null)
            return;

        var root = Normalize(project.RootDirectory.FullName);
        var outDir = Normalize(project.OutDirectory.FullName);
        var contracts = parse.Ast.Declarations.OfType<ExternContractNode>().ToList();

        if (contracts.Count == 0)
            return;

        reporter.Info("extern ownership: generated code stays in out/, developer code should stay outside out/ (prefer user/)");

        foreach (var contract in contracts)
        {
            if (string.IsNullOrWhiteSpace(contract.UserFile))
            {
                reporter.Warn($"extern {contract.Role} {contract.Name}: missing developer-owned file");
                continue;
            }

            var declaredPath = contract.UserFile.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.IsPathRooted(declaredPath)
                ? Path.GetFullPath(declaredPath)
                : Path.GetFullPath(Path.Combine(project.RootDirectory.FullName, declaredPath));
            var normalized = Normalize(fullPath);

            if (normalized.Equals(outDir, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(outDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                reporter.Warn($"extern {contract.Role} {contract.Name}: file points into generated outDir and should be moved to user/: {contract.UserFile}");
                continue;
            }

            if (!normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                reporter.Warn($"extern {contract.Role} {contract.Name}: file points outside project root; Git ownership may be unclear: {contract.UserFile}");
                continue;
            }

            var userDir = Normalize(Path.Combine(project.RootDirectory.FullName, "user"));
            if (!normalized.Equals(userDir, StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                reporter.Info($"extern {contract.Role} {contract.Name}: developer-owned file is outside out/, but user/ is the recommended location: {contract.UserFile}");
            }

            reporter.Info(File.Exists(normalized)
                ? $"extern {contract.Role} {contract.Name}: developer-owned file present: {contract.UserFile}"
                : $"extern {contract.Role} {contract.Name}: developer-owned file not present yet: {contract.UserFile}");
        }
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizeOverlayPath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static readonly ProjectOverlay[] ProjectOverlays =
    [
        new("embedded/platformio.ini", Path.Combine("user", "embedded", "platformio.ini")),
        new("embedded/CMakeLists.txt", Path.Combine("user", "embedded", "CMakeLists.txt")),
        new("server/go.mod", Path.Combine("user", "server", "go.mod")),
        new("report/user-adapter.ts", Path.Combine("user", "report", "adapter.ts")),
        new("report/user-package.json", Path.Combine("user", "report", "package.json")),
        new("report/user-view.json", Path.Combine("user", "report", "view.json"))
    ];

    private sealed record ProjectOverlay(string GeneratedPath, string UserPath);

    private sealed class DoctorReporter
    {
        private bool _hasErrors;

        internal int ExitCode => _hasErrors ? 1 : 0;

        internal void Ok(string message) => Console.WriteLine($"[ok] {message}");

        internal void Info(string message) => Console.WriteLine($"[info] {message}");

        internal void Warn(string message) => Console.WriteLine($"[warn] {message}");

        internal void Error(string message)
        {
            _hasErrors = true;
            Console.WriteLine($"[error] {message}");
        }
    }
}
