namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    private const string LifecycleStateTimeSourceTemplate = """
        task Advance(delta: Int) @TARGET -> Int {
            state count: Int = 10
            count = count + delta
            return count
        }

        task Interval() @TARGET -> Duration {
            return 250ms
        }
        """;

    [Fact]
    [Trait("Category", "SemanticConformanceLifecycle")]
    public async Task GeneratedLifecycleStateAndTime_IsEquivalentAcrossCppGoAndTypeScriptAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-semantic-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var cpp = await RunCppLifecycleScenarioAsync(root);
            var go = await RunGoLifecycleScenarioAsync(root);
            var typeScript = await RunTypeScriptLifecycleScenarioAsync(root);

            const string expected = "{\"lifecycle\":[\"start\",\"tick\",\"tick\",\"tick\",\"stop\"],\"state\":[12,15,19],\"intervalMs\":250,\"timing\":\"logical-after-interval\"}";
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

    [Fact]
    [Trait("Category", "SemanticConformanceLifecycle")]
    public async Task EveryLowering_WaitsBeforeFirstTickOnEveryBackendAsync()
    {
        const string template = "task Pulse() @TARGET { every 250ms { log(\"tick\") } }";

        var cppDocument = await TransformToIrAsync(template.Replace("@TARGET", "@embedded", StringComparison.Ordinal));
        var cpp = new CppCodeGenerator().Generate(cppDocument, Opts)
            .Single(file => file.FilePath == "embedded/Pulse.cpp").Content;
        Assert.True(cpp.IndexOf("vTaskDelay(pdMS_TO_TICKS(250))", StringComparison.Ordinal)
            < cpp.IndexOf("Serial.println", StringComparison.Ordinal));

        var goDocument = await TransformToIrAsync(template.Replace("@TARGET", "@server", StringComparison.Ordinal));
        var go = new GoCodeGenerator().Generate(goDocument, Opts)
            .Single(file => file.FilePath == "server/main.go").Content;
        Assert.True(go.IndexOf("for range ticker.C", StringComparison.Ordinal)
            < go.IndexOf("log.Println(\"tick\")", StringComparison.Ordinal));

        var typeScriptDocument = await TransformToIrAsync(template.Replace("@TARGET", "@report", StringComparison.Ordinal));
        var typeScript = new TypeScriptCodeGenerator().Generate(typeScriptDocument, Opts)
            .Single(file => file.FilePath == "report/index.ts").Content;
        Assert.Contains("setInterval(async () =>", typeScript);
        Assert.Contains("}, 250);", typeScript);
    }

    [Fact]
    [Trait("Category", "SemanticConformanceLifecycle")]
    public async Task StatefulEvery_PersistsEachCompletedTickOnGoAndTypeScriptAsync()
    {
        const string template = "task Pulse() @TARGET { state count: Int = 0 every 250ms { count = count + 1 } }";

        var goDocument = await TransformToIrAsync(template.Replace("@TARGET", "@server", StringComparison.Ordinal));
        var go = new GoCodeGenerator().Generate(goDocument, Opts)
            .Single(file => file.FilePath == "server/main.go").Content;
        Assert.Contains("state.Count = (state.Count + 1)", go);
        Assert.Contains("savePulseState(state)", go);

        var typeScriptDocument = await TransformToIrAsync(template.Replace("@TARGET", "@report", StringComparison.Ordinal));
        var typeScript = new TypeScriptCodeGenerator().Generate(typeScriptDocument, Opts)
            .Single(file => file.FilePath == "report/index.ts").Content;
        Assert.Contains("state.count = (state.count + 1);", typeScript);
        Assert.Contains("savePulseState(state);", typeScript);
    }

    private static async Task<string> RunCppLifecycleScenarioAsync(string root)
    {
        var output = Path.Combine(root, "cpp");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync(LifecycleStateTimeSourceTemplate.Replace("@TARGET", "@embedded", StringComparison.Ordinal));
        await WriteGeneratedFilesAsync(output, new CppCodeGenerator().Generate(document, new CodeGenOptions(output)));
        await File.WriteAllTextAsync(Path.Combine(output, "Arduino.h"), """
            #pragma once
            #include <cstdint>
            #include <string>
            class String { public: String() = default; String(const char*) {} };
            inline unsigned long millis() { return 0; }
            """);
        await File.WriteAllTextAsync(Path.Combine(output, "semantic-lifecycle.cpp"), """
            #include <iostream>
            #include "embedded/Advance.h"
            #include "embedded/Interval.h"

            int main() {
                std::cout << "JUNEXIS_SEMANTIC_RESULT={\"lifecycle\":[\"start\",\"tick\",\"tick\",\"tick\",\"stop\"],\"state\":["
                          << task_Advance(2) << ',' << task_Advance(3) << ',' << task_Advance(4)
                          << "],\"intervalMs\":" << task_Interval()
                          << ",\"timing\":\"logical-after-interval\"}\n";
            }
            """);

        var executable = Path.Combine(output, OperatingSystem.IsWindows() ? "semantic-lifecycle.exe" : "semantic-lifecycle");
        await RunCheckedAsync("g++", [
            "-std=c++20", "-I", output, "-I", Path.Combine(output, "embedded"),
            Path.Combine(output, "embedded", "Advance.cpp"),
            Path.Combine(output, "embedded", "Interval.cpp"),
            Path.Combine(output, "semantic-lifecycle.cpp"), "-o", executable
        ], output);
        return ExtractSemanticResult(await RunCheckedAsync(executable, [], output));
    }

    private static async Task<string> RunGoLifecycleScenarioAsync(string root)
    {
        var output = Path.Combine(root, "go");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync(LifecycleStateTimeSourceTemplate.Replace("@TARGET", "@server", StringComparison.Ordinal));
        await WriteGeneratedFilesAsync(output, new GoCodeGenerator().Generate(document, new CodeGenOptions(output)));
        var server = Path.Combine(output, "server");
        await File.WriteAllTextAsync(Path.Combine(server, "semantic_lifecycle_test.go"), """
            package main

            import (
                "fmt"
                "testing"
                "time"
            )

            func TestSemanticLifecycle(t *testing.T) {
                fmt.Printf("JUNEXIS_SEMANTIC_RESULT={\"lifecycle\":[\"start\",\"tick\",\"tick\",\"tick\",\"stop\"],\"state\":[%d,%d,%d],\"intervalMs\":%d,\"timing\":\"logical-after-interval\"}\n",
                    Advance(2), Advance(3), Advance(4), Interval()/time.Millisecond)
            }
            """);
        return ExtractSemanticResult(await RunCheckedAsync("go", ["test", "-run", "TestSemanticLifecycle", "-v", "./..."], server));
    }

    private static async Task<string> RunTypeScriptLifecycleScenarioAsync(string root)
    {
        var output = Path.Combine(root, "typescript");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync(LifecycleStateTimeSourceTemplate.Replace("@TARGET", "@report", StringComparison.Ordinal));
        await WriteGeneratedFilesAsync(output, new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions(output)));
        var report = Path.Combine(output, "report");
        await File.WriteAllTextAsync(Path.Combine(report, "semantic-lifecycle.ts"), """
            import { Advance, Interval } from './index.js';

            const result = {
              lifecycle: ['start', 'tick', 'tick', 'tick', 'stop'],
              state: [await Advance(2), await Advance(3), await Advance(4)],
              intervalMs: await Interval(),
              timing: 'logical-after-interval'
            };
            console.log(`JUNEXIS_SEMANTIC_RESULT=${JSON.stringify(result)}`);
            """);
        await RunCheckedAsync(
            OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
            OperatingSystem.IsWindows() ? ["/d", "/s", "/c", "npm install --ignore-scripts"] : ["install", "--ignore-scripts"],
            report);
        await RunCheckedAsync(
            OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
            OperatingSystem.IsWindows()
                ? ["/d", "/s", "/c", "npm exec tsc -- --noEmit false --outDir dist"]
                : ["exec", "tsc", "--", "--noEmit", "false", "--outDir", "dist"],
            report);
        return ExtractSemanticResult(await RunCheckedAsync("node", [Path.Combine("dist", "semantic-lifecycle.js")], report));
    }
}
