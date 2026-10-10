namespace JunexisLang.Compiler.CodeGen.Tests;

using System.Text.Json;

public sealed partial class CodeGeneratorTests
{
    private const string ScalarControlFlowSourceTemplate = """
        task Evaluate(value: Int, enabled: Bool) @TARGET -> Int {
            let base = value * 3 + 2
            if enabled and base > 10 {
                return base - 1
            } else if base == 8 {
                return base + 4
            } else {
                return 0 - base
            }
        }

        task ChooseText(value: Int) @TARGET -> Text {
            if value > 0 {
                return "positive"
            } else {
                return "other"
            }
        }

        task Ratio(value: Float) @TARGET -> Float {
            return value / 2.0 + 0.5
        }
        """;

    [Fact]
    [Trait("Category", "SemanticConformanceValues")]
    public async Task GeneratedScalarControlFlow_IsEquivalentAcrossCppGoAndTypeScriptAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-semantic-values-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var cpp = await RunCppScalarScenarioAsync(root);
            var go = await RunGoScalarScenarioAsync(root);
            var typeScript = await RunTypeScriptScalarScenarioAsync(root);

            const string expected = "{\"evaluate\":[13,12,-5],\"choose\":[\"positive\",\"other\"],\"ratio\":2.5}";
            Assert.Equal(expected, CanonicalizeJson(cpp));
            Assert.Equal(expected, CanonicalizeJson(go));
            Assert.Equal(expected, CanonicalizeJson(typeScript));
        }
        finally
        {
            if (Directory.Exists(root))
                await DeleteDirectoryWithRetryAsync(root);
        }
    }

    private static async Task<string> RunCppScalarScenarioAsync(string root)
    {
        var document = await TransformToIrAsync(ScalarControlFlowSourceTemplate.Replace("@TARGET", "@embedded", StringComparison.Ordinal));
        await WriteGeneratedFilesAsync(root, new CppCodeGenerator().Generate(document, new CodeGenOptions(root)));
        await File.WriteAllTextAsync(Path.Combine(root, "Arduino.h"), """
            #pragma once
            #include <cstdint>
            #include <string>
            class String {
            public:
                String() = default;
                String(const char* value) : value_(value) {}
                String(const std::string& value) : value_(value) {}
                const char* c_str() const { return value_.c_str(); }
            private:
                std::string value_;
            };
            inline unsigned long millis() { return 0; }
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "semantic-values.cpp"), """
            #include <iostream>
            #include "embedded/Evaluate.h"
            #include "embedded/ChooseText.h"
            #include "embedded/Ratio.h"

            int main() {
                std::cout << "JUNEXIS_SEMANTIC_RESULT={\"evaluate\":["
                          << task_Evaluate(4, true) << ','
                          << task_Evaluate(2, false) << ','
                          << task_Evaluate(1, false) << "],\"choose\":[\""
                          << task_ChooseText(1).c_str() << "\",\""
                          << task_ChooseText(0).c_str() << "\"],\"ratio\":"
                          << task_Ratio(4.0) << "}\n";
            }
            """);

        var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "semantic-values.exe" : "semantic-values");
        await RunCheckedAsync("g++", [
            "-std=c++20", "-I", root, "-I", Path.Combine(root, "embedded"),
            Path.Combine(root, "embedded", "Evaluate.cpp"),
            Path.Combine(root, "embedded", "ChooseText.cpp"),
            Path.Combine(root, "embedded", "Ratio.cpp"),
            Path.Combine(root, "semantic-values.cpp"), "-o", executable
        ], root);
        return ExtractSemanticResult(await RunCheckedAsync(executable, [], root));
    }

    private static async Task<string> RunGoScalarScenarioAsync(string root)
    {
        var document = await TransformToIrAsync(ScalarControlFlowSourceTemplate.Replace("@TARGET", "@server", StringComparison.Ordinal));
        await WriteGeneratedFilesAsync(root, new GoCodeGenerator().Generate(document, new CodeGenOptions(root)));
        var server = Path.Combine(root, "server");
        await File.WriteAllTextAsync(Path.Combine(server, "semantic_values_test.go"), """
            package main

            import (
                "fmt"
                "testing"
            )

            func TestSemanticValues(t *testing.T) {
                fmt.Printf("JUNEXIS_SEMANTIC_RESULT={\"evaluate\":[%d,%d,%d],\"choose\":[\"%s\",\"%s\"],\"ratio\":%g}\n",
                    Evaluate(4, true), Evaluate(2, false), Evaluate(1, false), ChooseText(1), ChooseText(0), Ratio(4.0))
            }
            """);
        return ExtractSemanticResult(await RunCheckedAsync("go", ["test", "-run", "TestSemanticValues", "-v", "./..."], server));
    }

    private static async Task<string> RunTypeScriptScalarScenarioAsync(string root)
    {
        var document = await TransformToIrAsync(ScalarControlFlowSourceTemplate.Replace("@TARGET", "@report", StringComparison.Ordinal));
        await WriteGeneratedFilesAsync(root, new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions(root)));
        var report = Path.Combine(root, "report");
        await File.WriteAllTextAsync(Path.Combine(report, "semantic-values.ts"), """
            import { Evaluate, ChooseText, Ratio } from './index.js';

            const result = {
              evaluate: [await Evaluate(4, true), await Evaluate(2, false), await Evaluate(1, false)],
              choose: [await ChooseText(1), await ChooseText(0)],
              ratio: await Ratio(4.0)
            };
            console.log(`JUNEXIS_SEMANTIC_RESULT=${JSON.stringify(result)}`);
            """);
        await RunCheckedAsync(
            OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
            OperatingSystem.IsWindows()
                ? ["/d", "/s", "/c", "npm install --ignore-scripts"]
                : ["install", "--ignore-scripts"],
            report);
        await RunCheckedAsync(
            OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
            OperatingSystem.IsWindows()
                ? ["/d", "/s", "/c", "npm exec tsc -- --noEmit false --outDir dist"]
                : ["exec", "tsc", "--", "--noEmit", "false", "--outDir", "dist"],
            report);
        return ExtractSemanticResult(await RunCheckedAsync("node", [Path.Combine("dist", "semantic-values.js")], report));
    }

    private static string ExtractSemanticResult(string output)
    {
        const string marker = "JUNEXIS_SEMANTIC_RESULT=";
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(candidate => candidate.StartsWith(marker, StringComparison.Ordinal));
        Assert.NotNull(line);
        return line[marker.Length..];
    }

    private static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
