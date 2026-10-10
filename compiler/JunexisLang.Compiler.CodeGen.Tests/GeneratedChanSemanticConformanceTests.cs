namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    [Trait("Category", "SemanticConformanceChan")]
    public async Task GeneratedChan_IsEquivalentAcrossCppGoAndTypeScriptAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-semantic-chan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var cpp = await RunCppChanScenarioAsync(root);
            var go = await RunGoChanScenarioAsync(root);
            var typeScript = await RunTypeScriptChanScenarioAsync(root);

            const string expected = "{\"fifo\":[1,2],\"producerWaited\":true,\"retention\":\"until-consumed\"}";
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

    private static async Task<string> RunCppChanScenarioAsync(string root)
    {
        var output = Path.Combine(root, "cpp");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @embedded -> Int { return 1 }");
        await WriteGeneratedFilesAsync(output, new CppCodeGenerator().Generate(document, new CodeGenOptions(output)));
        await File.WriteAllTextAsync(Path.Combine(output, "Arduino.h"), """
            #pragma once
            class String { public: template <typename T> String(const T&) {} };
            inline unsigned long millis() { return 0; }
            """);
        await File.WriteAllTextAsync(Path.Combine(output, "semantic-chan.cpp"), """
            #include <atomic>
            #include <chrono>
            #include <iostream>
            #include <thread>
            #include <junexis/runtime.h>

            int main() {
                junexis::Channel<int> channel(1);
                channel.send(1);
                std::atomic<bool> started{false};
                std::atomic<bool> completed{false};
                std::thread producer([&] {
                    started = true;
                    channel.send(2);
                    completed = true;
                });
                while (!started.load()) std::this_thread::yield();
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
                const bool waited = !completed.load();
                const int first = channel.recv();
                producer.join();
                const int second = channel.recv();
                std::cout << "JUNEXIS_SEMANTIC_RESULT={\"fifo\":[" << first << ',' << second
                          << "],\"producerWaited\":" << (waited ? "true" : "false")
                          << ",\"retention\":\"until-consumed\"}\n";
            }
            """);

        var executable = Path.Combine(output, OperatingSystem.IsWindows() ? "semantic-chan.exe" : "semantic-chan");
        await RunCheckedAsync("g++", [
            "-std=c++17", "-pthread", "-I", output, "-I", Path.Combine(output, "embedded"),
            Path.Combine(output, "semantic-chan.cpp"), "-o", executable
        ], output);
        return ExtractSemanticResult(await RunCheckedAsync(executable, [], output));
    }

    private static async Task<string> RunGoChanScenarioAsync(string root)
    {
        var output = Path.Combine(root, "go");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @server -> Int { return 1 }");
        await WriteGeneratedFilesAsync(output, new GoCodeGenerator().Generate(document, new CodeGenOptions(output)));
        var server = Path.Combine(output, "server");
        await File.WriteAllTextAsync(Path.Combine(server, "semantic_chan_test.go"), """
            package main

            import (
                "fmt"
                "testing"
            )

            func TestSemanticChan(t *testing.T) {
                channel := make(chan int, 1)
                channel <- 1
                started := make(chan struct{})
                completed := make(chan struct{})
                go func() { close(started); channel <- 2; close(completed) }()
                <-started
                producerWaited := false
                select {
                case <-completed:
                default:
                    producerWaited = true
                }
                first := <-channel
                <-completed
                second := <-channel
                fmt.Printf("JUNEXIS_SEMANTIC_RESULT={\"fifo\":[%d,%d],\"producerWaited\":%t,\"retention\":\"until-consumed\"}\n",
                    first, second, producerWaited)
            }
            """);
        return ExtractSemanticResult(await RunCheckedAsync("go", ["test", "-run", "TestSemanticChan", "-v", "./..."], server));
    }

    private static async Task<string> RunTypeScriptChanScenarioAsync(string root)
    {
        var output = Path.Combine(root, "typescript");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @report -> Int { return 1 }");
        await WriteGeneratedFilesAsync(output, new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions(output)));
        var report = Path.Combine(output, "report");
        await File.WriteAllTextAsync(Path.Combine(report, "semantic-chan.ts"), """
            import { Channel } from './runtime.js';

            const channel = new Channel<number>(1);
            await channel.send(1);
            let completed = false;
            const secondSend = channel.send(2).then(() => { completed = true; });
            await Promise.resolve();
            const producerWaited = !completed;
            const first = await channel.recv();
            await secondSend;
            const second = await channel.recv();
            channel.close();
            console.log(`JUNEXIS_SEMANTIC_RESULT=${JSON.stringify({ fifo: [first, second], producerWaited, retention: 'until-consumed' })}`);
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
        return ExtractSemanticResult(await RunCheckedAsync("node", [Path.Combine("dist", "semantic-chan.js")], report));
    }
}
