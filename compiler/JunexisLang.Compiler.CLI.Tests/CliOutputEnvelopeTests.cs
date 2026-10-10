using System.Text.Json;
using JunexisLang.Compiler.CLI.Output;
using JunexisLang.Compiler.Core;

namespace JunexisLang.Compiler.CLI.Tests;

/// <summary>
/// Testy kontraktowe dla CliOutputEnvelope — weryfikują schema v1.0.
/// Trait Issue: CONT-06 (CLI JSON schema).
/// </summary>
public sealed class CliOutputEnvelopeTests
{
    // ── CliOutputEnvelope — struktura i serializacja ──────────────────────

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void Envelope_SerializesAllRequiredFields()
    {
        var dto = new CliDiagnosticDto("E0001", "error", "Parse", "test error", "file.jnx", 3, 5);
        var envelope = new CliOutputEnvelope(
            SchemaVersion: CliOutputEnvelope.CurrentVersion,
            Command:       "check",
            Success:       false,
            DurationMs:    142,
            InputFile:     "file.jnx",
            Diagnostics:   [dto]);

        var json = JsonSerializer.Serialize(envelope);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal("1.0",      root.GetProperty("schemaVersion").GetString());
        Assert.Equal("check",    root.GetProperty("command").GetString());
        Assert.False(            root.GetProperty("success").GetBoolean());
        Assert.Equal(142,        root.GetProperty("durationMs").GetInt64());
        Assert.Equal("file.jnx", root.GetProperty("inputFile").GetString());
        Assert.Equal(1,          root.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void Envelope_CurrentVersion_Is_1_0()
    {
        Assert.Equal("1.0", CliOutputEnvelope.CurrentVersion);
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void Envelope_EmptyDiagnostics_SerializesAsEmptyArray()
    {
        var envelope = new CliOutputEnvelope("1.0", "check", true, 87, "ok.jnx", []);
        var json = JsonSerializer.Serialize(envelope);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal(0, root.GetProperty("diagnostics").GetArrayLength());
        Assert.True(root.GetProperty("success").GetBoolean());
    }

    // ── CliDiagnosticDto.From — mapowanie poziomów ───────────────────────

    [Theory]
    [Trait("Issue", "CONT-06")]
    [InlineData(DiagnosticLevel.Error,   "error")]
    [InlineData(DiagnosticLevel.Warning, "warning")]
    [InlineData(DiagnosticLevel.Info,    "info")]
    public void DiagnosticDto_From_MapsLevel_ToLowercase(DiagnosticLevel level, string expected)
    {
        var d = new Diagnostic("C0001", level, "Type", "msg", "f.jnx", 1, 1);
        var dto = CliDiagnosticDto.From(d);
        Assert.Equal(expected, dto.Level);
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void DiagnosticDto_From_MapsAllFields()
    {
        var d = new Diagnostic("E0042", DiagnosticLevel.Error, "Communication", "chan requires buffer",
            "sensor.jnx", 12, 9);
        var dto = CliDiagnosticDto.From(d);

        Assert.Equal("E0042",          dto.Code);
        Assert.Equal("error",          dto.Level);
        Assert.Equal("Communication",  dto.Category);
        Assert.Equal("chan requires buffer", dto.Message);
        Assert.Equal("sensor.jnx",    dto.File);
        Assert.Equal(12,               dto.Line);
        Assert.Equal(9,                dto.Column);
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void DiagnosticDto_From_NullFile_SerializesAsNull()
    {
        var d = new Diagnostic("", DiagnosticLevel.Warning, "Global", "global warning");
        var dto = CliDiagnosticDto.From(d);
        Assert.Null(dto.File);
    }

    [Fact]
    [Trait("Issue", "CONT-06")]
    public void DiagnosticDto_JsonPropertyNames_AreCamelCase()
    {
        var d = new Diagnostic("E0001", DiagnosticLevel.Error, "Parse", "msg", "f.jnx", 1, 2);
        var dto = CliDiagnosticDto.From(d);
        var json = JsonSerializer.Serialize(dto);
        var root = JsonDocument.Parse(json).RootElement;

        // Wszystkie klucze muszą być camelCase — kontrakt schema v1.0
        Assert.True(root.TryGetProperty("code",     out _), "missing: code");
        Assert.True(root.TryGetProperty("level",    out _), "missing: level");
        Assert.True(root.TryGetProperty("category", out _), "missing: category");
        Assert.True(root.TryGetProperty("message",  out _), "missing: message");
        Assert.True(root.TryGetProperty("file",     out _), "missing: file");
        Assert.True(root.TryGetProperty("line",     out _), "missing: line");
        Assert.True(root.TryGetProperty("column",   out _), "missing: column");
    }
}
