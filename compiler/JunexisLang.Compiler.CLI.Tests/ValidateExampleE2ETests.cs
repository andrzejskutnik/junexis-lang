using System.Reflection;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

/// <summary>
/// Testy E2E dla komendy <c>validate-example</c> — weryfikują pełną ścieżkę:
/// check → compile (Go/server) → go build.
/// Wymagają zainstalowanego Go w PATH lub zmiennej środowiskowej JUNEXIS_GO_EXE.
/// Jeśli Go nie jest dostępne, testy kończą się niepowodzeniem z czytelnym komunikatem.
/// </summary>
public sealed class ValidateExampleE2ETests
{
    /// <summary>
    /// Ścieżka do katalogu <c>examples/</c> — wyznaczana względem assembly testowego
    /// (compiler/JunexisLang.Compiler.CLI.Tests/bin/...) → 4 poziomy wyżej → root projektu.
    /// </summary>
    private static string ExamplesDir => Path.GetFullPath(
        Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..", "..", "examples"));

    private static string GoExe =>
        Environment.GetEnvironmentVariable("JUNEXIS_GO_EXE") ?? "go";

    // ── helper: resolving go ──────────────────────────────────────────────

    private static bool IsGoAvailable()
    {
        var exe = GoExe;
        if (exe != "go" && File.Exists(exe)) return true;

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "go.exe", "go" }
            : new[] { "go" };
        return paths.Any(dir => candidates.Any(c => File.Exists(Path.Combine(dir, c))));
    }

    private static void RequireGo()
    {
        if (!IsGoAvailable())
            throw new InvalidOperationException(
                "Go toolchain not found in PATH. Install Go from https://go.dev/dl/ or set " +
                "JUNEXIS_GO_EXE environment variable to the path of the go executable.");
    }

    // ── helper: invoke ValidateExampleHandler directly ────────────────────

    private static async Task<int> RunValidateAsync(string jnxPath)
    {
        var fi = new FileInfo(jnxPath);
        return await ValidateExampleHandler.HandleAsync(fi, GoExe);
    }

    // ── tests ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Issue", "E2E-01")]
    public async Task E2E_01_TemperatureMonitor_PassesFullPipeline()
    {
        RequireGo();

        var jnx = Path.Combine(ExamplesDir, "01-temperature-monitor.jnx");
        Assert.True(File.Exists(jnx), $"Example not found: {jnx}");

        var exitCode = await RunValidateAsync(jnx);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Issue", "E2E-01")]
    public async Task E2E_02_EcommerceApi_PassesFullPipeline()
    {
        RequireGo();

        var jnx = Path.Combine(ExamplesDir, "02-ecommerce-api.jnx");
        Assert.True(File.Exists(jnx), $"Example not found: {jnx}");

        var exitCode = await RunValidateAsync(jnx);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Issue", "E2E-01")]
    public async Task E2E_13_MinimalPipeline_PassesFullPipeline()
    {
        RequireGo();

        var jnx = Path.Combine(ExamplesDir, "13-minimal-pipeline.jnx");
        Assert.True(File.Exists(jnx), $"Example not found: {jnx}");

        var exitCode = await RunValidateAsync(jnx);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Issue", "E2E-01")]
    public async Task E2E_05_SimpleSensor_EmbeddedOnly_ReturnsZeroWithoutGoArtifact()
    {
        // 05-simple-sensor ma tylko @embedded — validate-example powinien zwrócić 0
        // (brak artefaktu Go jest oczekiwanym zachowaniem, nie błędem).
        var jnx = Path.Combine(ExamplesDir, "05-simple-sensor.jnx");
        Assert.True(File.Exists(jnx), $"Example not found: {jnx}");

        // Nie wymagamy Go — ten test nigdy nie dojdzie do go build.
        var exitCode = await RunValidateAsync(jnx);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Issue", "E2E-01")]
    public async Task E2E_FileNotFound_ReturnsNonZero()
    {
        var exitCode = await RunValidateAsync("/nonexistent/path/missing.jnx");
        Assert.NotEqual(0, exitCode);
    }
}
