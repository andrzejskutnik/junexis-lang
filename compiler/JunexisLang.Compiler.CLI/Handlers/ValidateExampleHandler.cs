using System.Diagnostics;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.CodeGen;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.CLI.Handlers;

/// <summary>
/// Obsługuje komendę <c>validate-example</c> — przeprowadza pełną walidację E2E:
/// check → compile (Go/server) → go build.
/// </summary>
internal static class ValidateExampleHandler
{
    /// <summary>
    /// Główny punkt wejścia dla komendy <c>validate-example</c>.
    /// </summary>
    /// <param name="input">Plik źródłowy <c>.jnx</c> do walidacji.</param>
    /// <param name="goExe">Ścieżka do pliku wykonywalnego <c>go</c> (domyślnie <c>go</c> z PATH).</param>
    /// <returns>0 gdy wszystkie kroki zakończą się sukcesem; niezerowy kod w przypadku błędu.</returns>
    internal static async Task<int> HandleAsync(FileInfo input, string goExe)
    {
        var name = Path.GetFileNameWithoutExtension(input.Name);
        Console.WriteLine($"=== validate-example: {name} ===");

        // ── 1. check ──────────────────────────────────────────────────────
        Console.Write("  [1/3] check ... ");
        if (!input.Exists)
        {
            Console.WriteLine("FAIL");
            await Console.Error.WriteLineAsync($"error: file not found: {input.FullName}");
            return 1;
        }

        var checkResult = await new JunexisCompiler().CheckAsync(new CheckOptions(input.FullName));

        var errors = checkResult.Diagnostics
            .Where(d => d.Level == DiagnosticLevel.Error)
            .ToList();

        if (!checkResult.Success || errors.Count > 0)
        {
            Console.WriteLine("FAIL");
            DiagnosticFormatter.Write(checkResult.Diagnostics);
            return 1;
        }
        Console.WriteLine("OK");

        // ── 2. compile → Go/server ────────────────────────────────────────
        Console.Write("  [2/3] compile (server) ... ");

        var tmpOut = Path.Combine(Path.GetTempPath(), $"junexis_validate_{name}_{Guid.NewGuid():N}");
        var codeOpts = new CodeGenOptions(tmpOut);

        var structural = StructuralAnalysisStage.Analyze(checkResult.Ast!);
        if (!structural.Success || structural.Document is null)
        {
            Console.WriteLine("FAIL");
            DiagnosticFormatter.Write(structural.Diagnostics);
            return 1;
        }
        DiagnosticFormatter.Write(structural.Diagnostics);
        var ir = structural.Document;

        var generators = CodeGeneratorFactory.Get(["server"]);
        try
        {
            var generatedFiles = new List<GeneratedFile>();
            foreach (var generator in generators)
            {
                var generated = generator.Generate(ir, codeOpts);
                var contract = GeneratedArtifactContract.Validate(ir, generator.Platform, generated);
                if (!contract.Success)
                    throw new InvalidOperationException(string.Join("; ", contract.Violations.Select(v => v.Message)));
                generatedFiles.AddRange(generated);
            }

            // zapisz pliki na dysk (Generate zwraca content, nie zapisuje)
            foreach (var file in generatedFiles)
            {
                var fullPath = Path.Combine(tmpOut, file.FilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, file.Content);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL");
            await Console.Error.WriteLineAsync($"error: codegen failed: {ex.Message}");
            TryCleanup(tmpOut);
            return 1;
        }

        var serverDir = Path.Combine(tmpOut, "server");
        var mainGo = Path.Combine(serverDir, "main.go");

        if (!File.Exists(mainGo))
        {
            Console.WriteLine("SKIP");
            Console.WriteLine($"  note: no @server tasks found in {name} — no Go artifact generated (expected for @embedded-only examples)");
            TryCleanup(tmpOut);
            return 0;
        }

        Console.WriteLine($"OK  → {mainGo}");

        // ── 3. go build ───────────────────────────────────────────────────
        Console.Write("  [3/3] go build ... ");

        var goResolved = ResolveGo(goExe);
        if (goResolved is null)
        {
            Console.WriteLine("FAIL");
            await Console.Error.WriteLineAsync(
                "error: Go toolchain not found. Install Go from https://go.dev/dl/ or set --go-exe.");
            TryCleanup(tmpOut);
            return 1;
        }

        // go mod init / tidy + go build
        // When the compiler already emitted a go.mod (e.g. with external dependencies),
        // skip `go mod init` (which would overwrite it) and run `go mod tidy` to fetch modules.
        var goModPath = Path.Combine(serverDir, "go.mod");
        if (File.Exists(goModPath))
        {
            var tidyCode = await RunProcessAsync(goResolved, "mod tidy", serverDir);
            if (tidyCode != 0)
            {
                Console.WriteLine("FAIL (go mod tidy)");
                TryCleanup(tmpOut);
                return 1;
            }
        }
        else
        {
            var initCode = await RunProcessAsync(goResolved, "mod init junexis_validate", serverDir);
            if (initCode != 0)
            {
                Console.WriteLine("FAIL (go mod init)");
                TryCleanup(tmpOut);
                return 1;
            }
        }

        var (buildCode, buildOutput) = await RunProcessWithOutputAsync(goResolved, "build .", serverDir);
        if (buildCode != 0)
        {
            Console.WriteLine("FAIL");
            await Console.Error.WriteLineAsync(buildOutput);
            TryCleanup(tmpOut);
            return 1;
        }

        Console.WriteLine("OK");
        Console.WriteLine($"  artifact: {serverDir}");
        TryCleanup(tmpOut);
        return 0;
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static string? ResolveGo(string goExe)
    {
        if (!string.IsNullOrWhiteSpace(goExe) && goExe != "go")
            return File.Exists(goExe) ? goExe : null;

        // sprawdź PATH
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "go.exe", "go" }
            : new[] { "go" };

        foreach (var dir in paths)
            foreach (var candidate in candidates)
            {
                var full = Path.Combine(dir, candidate);
                if (File.Exists(full)) return full;
            }

        return null;
    }

    private static async Task<int> RunProcessAsync(string exe, string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync();
        return proc.ExitCode;
    }

    private static async Task<(int exitCode, string output)> RunProcessWithOutputAsync(
        string exe, string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var combined = (stdout + stderr).Trim();
        return (proc.ExitCode, combined);
    }

    private static void TryCleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* ignoruj — katalog tymczasowy */ }
    }
}
