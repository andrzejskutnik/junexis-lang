namespace JunexisLang.Compiler.CodeGen.Tests;

using System.Reflection;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

/// <summary>Executable inventory gate for the four M4 artifact families.</summary>
public sealed class GeneratedArtifactContractTests
{
    private static string ExamplesDirectory => Path.GetFullPath(
        Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..", "..", "examples"));

    /// <summary>Every main example satisfies every applicable artifact inventory contract.</summary>
    [Fact]
    [Trait("Category", "M4Contract")]
    public async Task AllMainExamples_SatisfyApplicableArtifactContractsAsync()
    {
        var examples = Directory.GetFiles(ExamplesDirectory, "*.jnx").OrderBy(path => path).ToList();
        Assert.Equal(13, examples.Count);

        foreach (var example in examples)
        {
            var check = await new JunexisCompiler().CheckAsync(new CheckOptions(example));
            Assert.True(check.Success, $"Check failed for {Path.GetFileName(example)}");
            var document = new AstToIrTransformer().Transform(check.Ast!);

            foreach (var generator in CodeGeneratorFactory.Get(null))
            {
                var files = generator.Generate(document, new CodeGenOptions("out"));
                var result = GeneratedArtifactContract.Validate(document, generator.Platform, files);
                Assert.True(result.Success,
                    $"{Path.GetFileName(example)} [{generator.Platform}]: " +
                    string.Join("; ", result.Violations.Select(violation => violation.Message)));
            }
        }
    }

    /// <summary>The embedded backend resolves a device alias to its concrete hardware profile.</summary>
    [Fact]
    [Trait("Category", "M4Contract")]
    public async Task EmbeddedDeviceAlias_UsesConcreteDeviceProfileAsync()
    {
        var example = Path.Combine(ExamplesDirectory, "01-temperature-monitor.jnx");
        var check = await new JunexisCompiler().CheckAsync(new CheckOptions(example));
        var document = new AstToIrTransformer().Transform(check.Ast!);

        var files = new CppCodeGenerator().Generate(document, new CodeGenOptions("out"));
        var header = files.Single(file => file.FilePath == "embedded/TempSensor.h").Content;
        var platformIo = files.Single(file => file.FilePath == "embedded/platformio.ini").Content;

        Assert.Contains("Variant = \"esp32c3\"", header);
        Assert.Contains("board = esp32-c3-devkitm-1", platformIo);
    }

    /// <summary>Typed extern declarations become exact C++ boundary stubs.</summary>
    [Fact]
    [Trait("Category", "M4Contract")]
    public async Task EmbeddedTypedExtern_EmitsConcreteReturnSignatureAsync()
    {
        var example = Path.Combine(ExamplesDirectory, "09-smart-home-iot.jnx");
        var check = await new JunexisCompiler().CheckAsync(new CheckOptions(example));
        var document = new AstToIrTransformer().Transform(check.Ast!);

        var source = new CppCodeGenerator().Generate(document, new CodeGenOptions("out"))
            .Single(file => file.FilePath == "embedded/SensorTemperatury.cpp").Content;

        Assert.Contains("static SurowyOdczytDht22 odczytaj_dht22(uint8_t pin)", source);
    }

    /// <summary>The report backend binds generated types and its local runtime explicitly.</summary>
    [Fact]
    [Trait("Category", "M4Contract")]
    public async Task ReportArtifact_HasValidBoundImportsAndManifestAsync()
    {
        var example = Path.Combine(ExamplesDirectory, "01-temperature-monitor.jnx");
        var check = await new JunexisCompiler().CheckAsync(new CheckOptions(example));
        var document = new AstToIrTransformer().Transform(check.Ast!);
        var files = new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions("out"));
        var index = files.Single(file => file.FilePath == "report/index.ts").Content;
        var package = files.Single(file => file.FilePath == "report/package.json").Content;

        Assert.DoesNotContain("import type *", index);
        Assert.Contains("import type { TempReading, Alert } from './types.js';", index);
        Assert.Contains("import * as junexis from './runtime.js';", index);
        Assert.Contains("export let render_alert = (alert: Alert): void =>", index);
        Assert.Contains("export function bindRenderAlert", index);
        Assert.DoesNotContain("@junexis/runtime", package);
    }

    /// <summary>Unsafe and duplicate paths are rejected before files reach the filesystem.</summary>
    [Fact]
    [Trait("Category", "M4Contract")]
    public void Contract_RejectsUnsafeAndDuplicatePaths()
    {
        var document = EmptyDocument();
        var files = new[]
        {
            new GeneratedFile("server", "../main.go", "package main"),
            new GeneratedFile("server", "../main.go", "package main")
        };

        var result = GeneratedArtifactContract.Validate(document, "server", files);

        Assert.False(result.Success);
        Assert.Contains(result.Violations, violation => violation.Message.Contains("unsafe generated path"));
        Assert.Contains(result.Violations, violation => violation.Message.Contains("duplicate generated path"));
    }

    private static IRDocument EmptyDocument() => new(
        "test.jnx", [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);
}
