namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    [Trait("Category", "SemanticConformanceStream")]
    public async Task GeneratedStreamLatestWinsFanout_IsEquivalentAcrossCppGoAndTypeScriptAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-semantic-stream-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var cpp = await RunCppStreamScenarioAsync(root);
            var go = await RunGoStreamScenarioAsync(root);
            var typeScript = await RunTypeScriptStreamScenarioAsync(root);

            const string expected = "{\"coalesced\":[2,2],\"noDuplicate\":true,\"noHistoryReplay\":true,\"fanout\":[4,4,4],\"latest\":4}";
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

    private static async Task<string> RunCppStreamScenarioAsync(string root)
    {
        var output = Path.Combine(root, "cpp");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @embedded -> stream<Int> { send 1 }");
        await WriteGeneratedFilesAsync(output, new CppCodeGenerator().Generate(document, new CodeGenOptions(output)));
        await File.WriteAllTextAsync(Path.Combine(output, "Arduino.h"), """
            #pragma once
            class String { public: template <typename T> String(const T&) {} };
            inline unsigned long millis() { return 0; }
            """);
        await File.WriteAllTextAsync(Path.Combine(output, "semantic-stream.cpp"), """
            #include <iostream>
            #include <junexis/runtime.h>

            int main() {
                junexis::Stream<int> stream;
                auto first = stream.subscribe();
                auto second = stream.subscribe();
                stream.publish(1);
                stream.publish(2);
                const auto first_latest = first.poll();
                const auto second_latest = second.poll();
                const bool no_duplicate = !first.poll().has_value();
                stream.publish(3);
                auto late = stream.subscribe();
                const bool no_history = !late.poll().has_value();
                stream.publish(4);
                const auto fanout_a = first.poll();
                const auto fanout_b = second.poll();
                const auto fanout_late = late.poll();
                std::cout << "JUNEXIS_SEMANTIC_RESULT={\"coalesced\":[" << *first_latest << ',' << *second_latest
                          << "],\"noDuplicate\":" << (no_duplicate ? "true" : "false")
                          << ",\"noHistoryReplay\":" << (no_history ? "true" : "false")
                          << ",\"fanout\":[" << *fanout_a << ',' << *fanout_b << ',' << *fanout_late
                          << "],\"latest\":" << stream.recv() << "}\n";
            }
            """);

        var executable = Path.Combine(output, OperatingSystem.IsWindows() ? "semantic-stream.exe" : "semantic-stream");
        await RunCheckedAsync("g++", [
            "-std=c++17", "-pthread", "-I", output, "-I", Path.Combine(output, "embedded"),
            Path.Combine(output, "semantic-stream.cpp"), "-o", executable
        ], output);
        return ExtractSemanticResult(await RunCheckedAsync(executable, [], output));
    }

    private static async Task<string> RunGoStreamScenarioAsync(string root)
    {
        var output = Path.Combine(root, "go");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @server -> stream<Int> { send 1 }");
        await WriteGeneratedFilesAsync(output, new GoCodeGenerator().Generate(document, new CodeGenOptions(output)));
        var server = Path.Combine(output, "server");
        await File.WriteAllTextAsync(Path.Combine(server, "semantic_stream_test.go"), """
            package main

            import (
                "fmt"
                "testing"
            )

            func TestSemanticStream(t *testing.T) {
                stream := NewStream[int]()
                first := stream.Subscribe()
                second := stream.Subscribe()
                stream.Publish(1)
                stream.Publish(2)
                firstLatest, _ := first.Poll()
                secondLatest, _ := second.Poll()
                _, duplicate := first.Poll()
                stream.Publish(3)
                late := stream.Subscribe()
                _, history := late.Poll()
                stream.Publish(4)
                fanoutA, _ := first.Poll()
                fanoutB, _ := second.Poll()
                fanoutLate, _ := late.Poll()
                fmt.Printf("JUNEXIS_SEMANTIC_RESULT={\"coalesced\":[%d,%d],\"noDuplicate\":%t,\"noHistoryReplay\":%t,\"fanout\":[%d,%d,%d],\"latest\":%d}\n",
                    firstLatest, secondLatest, !duplicate, !history, fanoutA, fanoutB, fanoutLate, stream.Latest())
            }
            """);
        return ExtractSemanticResult(await RunCheckedAsync("go", ["test", "-run", "TestSemanticStream", "-v", "./..."], server));
    }

    private static async Task<string> RunTypeScriptStreamScenarioAsync(string root)
    {
        var output = Path.Combine(root, "typescript");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @report -> stream<Int> { send 1 }");
        await WriteGeneratedFilesAsync(output, new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions(output)));
        var report = Path.Combine(output, "report");
        await File.WriteAllTextAsync(Path.Combine(report, "semantic-stream.ts"), """
            import { Stream } from './runtime.js';

            const stream = new Stream<number>();
            const first = stream.subscribe();
            const second = stream.subscribe();
            stream.publish(1);
            stream.publish(2);
            const firstLatest = first.poll();
            const secondLatest = second.poll();
            const noDuplicate = first.poll() === undefined;
            stream.publish(3);
            const late = stream.subscribe();
            const noHistoryReplay = late.poll() === undefined;
            stream.publish(4);
            console.log(`JUNEXIS_SEMANTIC_RESULT=${JSON.stringify({
              coalesced: [firstLatest, secondLatest],
              noDuplicate,
              noHistoryReplay,
              fanout: [first.poll(), second.poll(), late.poll()],
              latest: stream.latest()
            })}`);
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
        return ExtractSemanticResult(await RunCheckedAsync("node", [Path.Combine("dist", "semantic-stream.js")], report));
    }
}
