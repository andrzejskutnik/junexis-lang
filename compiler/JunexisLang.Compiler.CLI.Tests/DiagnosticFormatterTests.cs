using System.Text.Json;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Tests;

/// <summary>
/// Testy kontraktowe dla DiagnosticFormatter — weryfikują JSON envelope i format tekstowy.
/// Trait Issue: CONT-06 (CLI JSON schema).
/// </summary>
public sealed class DiagnosticFormatterTests
{
    // ── BuildJson — envelope ─────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void BuildJson_Check_ProducesValidEnvelope()
    {
        var diagnostics = new List<Diagnostic>
        {
            new("E0001", DiagnosticLevel.Error, "Parse", "undeclared identifier", "app.jnx", 5, 3)
        };

        var json = DiagnosticFormatter.BuildJson("check", false, 142, "app.jnx", diagnostics);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal("1.0",     root.GetProperty("schemaVersion").GetString());
        Assert.Equal("check",   root.GetProperty("command").GetString());
        Assert.False(           root.GetProperty("success").GetBoolean());
        Assert.Equal("app.jnx", root.GetProperty("inputFile").GetString());
        Assert.Equal(142,       root.GetProperty("durationMs").GetInt64());
        Assert.Equal(1,         root.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void BuildJson_Compile_CommandField_IsCompile()
    {
        var json = DiagnosticFormatter.BuildJson("compile", true, 412, "main.jnx", []);
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("compile", root.GetProperty("command").GetString());
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void BuildJson_Success_True_WithWarningsOnly()
    {
        var diagnostics = new List<Diagnostic>
        {
            new("W0201", DiagnosticLevel.Warning, "Type", "implicit conversion", "f.jnx", 2, 1)
        };

        var json = DiagnosticFormatter.BuildJson("check", true, 50, "f.jnx", diagnostics);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("diagnostics").GetArrayLength());

        var diag = root.GetProperty("diagnostics")[0];
        Assert.Equal("warning", diag.GetProperty("level").GetString());
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void BuildJson_Success_False_WithErrorDiagnostic()
    {
        var diagnostics = new List<Diagnostic>
        {
            new("E0001", DiagnosticLevel.Error, "Parse", "syntax error", "broken.jnx", 1, 1)
        };

        var json = DiagnosticFormatter.BuildJson("check", false, 88, "broken.jnx", diagnostics);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void BuildJson_EmptyDiagnostics_Success_True()
    {
        var json = DiagnosticFormatter.BuildJson("check", true, 37, "clean.jnx", []);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void BuildJson_OutputIsValidJson()
    {
        var diagnostics = new List<Diagnostic>
        {
            new("E0001", DiagnosticLevel.Error,   "Parse", "error",   "f.jnx", 1, 1),
            new("W0001", DiagnosticLevel.Warning, "Type",  "warning", "f.jnx", 2, 3),
            new("I0001", DiagnosticLevel.Info,    "Flow",  "info",    null,    0, 0)
        };

        var json = DiagnosticFormatter.BuildJson("compile", false, 200, "f.jnx", diagnostics);

        // Nie rzuca wyjątku → valid JSON
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal(3, root.GetProperty("diagnostics").GetArrayLength());
    }

    // ── Write (text format) ──────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void Write_TextFormat_PrintsErrorToStderr()
    {
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var diagnostics = new List<Diagnostic>
            {
                new("E0001", DiagnosticLevel.Error, "Parse", "syntax error", "app.jnx", 3, 1)
            };
            DiagnosticFormatter.Write(diagnostics);
        }
        finally
        {
            Console.SetError(original);
        }

        var output = sw.ToString();
        Assert.Contains("error", output);
        Assert.Contains("E0001", output);
        Assert.Contains("syntax error", output);
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void Write_TextFormat_PrintsWarningToStdout()
    {
        var original = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var diagnostics = new List<Diagnostic>
            {
                new("W0101", DiagnosticLevel.Warning, "Type", "implicit cast", "app.jnx", 5, 2)
            };
            DiagnosticFormatter.Write(diagnostics);
        }
        finally
        {
            Console.SetOut(original);
        }

        var output = sw.ToString();
        Assert.Contains("warning", output);
        Assert.Contains("W0101", output);
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void Write_TextFormat_SkipsInfoDiagnostics()
    {
        var original = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var diagnostics = new List<Diagnostic>
            {
                new("I0001", DiagnosticLevel.Info, "Flow", "info only message", null, 0, 0)
            };
            DiagnosticFormatter.Write(diagnostics);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal("", sw.ToString());
    }
}
