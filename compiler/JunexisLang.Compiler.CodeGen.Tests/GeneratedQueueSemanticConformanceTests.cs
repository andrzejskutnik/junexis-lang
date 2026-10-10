namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    [Trait("Category", "SemanticConformanceQueue")]
    public async Task GeneratedQueueAckNack_IsEquivalentAcrossCppGoAndTypeScriptAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-semantic-queue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var cpp = await RunCppQueueScenarioAsync(root);
            var go = await RunGoQueueScenarioAsync(root);
            var typeScript = await RunTypeScriptQueueScenarioAsync(root);

            const string expected = "{\"first\":{\"id\":\"1\",\"value\":7},\"repeatId\":\"1\",\"redeliveryId\":\"1\",\"afterInvalidAckId\":\"1\",\"next\":{\"id\":\"2\",\"value\":8}}";
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

    private static async Task<string> RunCppQueueScenarioAsync(string root)
    {
        var output = Path.Combine(root, "cpp");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @embedded -> queue<Int> { send 1 }");
        await WriteGeneratedFilesAsync(output, new CppCodeGenerator().Generate(document, new CodeGenOptions(output)));
        await File.WriteAllTextAsync(Path.Combine(output, "Arduino.h"), """
            #pragma once
            class String { public: template <typename T> String(const T&) {} };
            inline unsigned long millis() { return 0; }
            """);
        await File.WriteAllTextAsync(Path.Combine(output, "semantic-queue.cpp"), """
            #include <iostream>
            #include <junexis/runtime.h>

            int main() {
                junexis::Queue<int> queue;
                queue.enqueue(7);
                queue.enqueue(8);
                const auto first = queue.delivery();
                const auto repeated = queue.delivery();
                queue.nack(first.message_id, "retry");
                const auto redelivery = queue.delivery();
                queue.ack("missing");
                const auto after_invalid = queue.delivery();
                queue.ack(first.message_id);
                const auto next = queue.delivery();
                std::cout << "JUNEXIS_SEMANTIC_RESULT={\"first\":{\"id\":\"" << first.message_id
                          << "\",\"value\":" << first.value << "},\"repeatId\":\"" << repeated.message_id
                          << "\",\"redeliveryId\":\"" << redelivery.message_id
                          << "\",\"afterInvalidAckId\":\"" << after_invalid.message_id
                          << "\",\"next\":{\"id\":\"" << next.message_id << "\",\"value\":" << next.value << "}}\n";
            }
            """);

        var executable = Path.Combine(output, OperatingSystem.IsWindows() ? "semantic-queue.exe" : "semantic-queue");
        await RunCheckedAsync("g++", [
            "-std=c++17", "-pthread", "-I", output, "-I", Path.Combine(output, "embedded"),
            Path.Combine(output, "semantic-queue.cpp"), "-o", executable
        ], output);
        return ExtractSemanticResult(await RunCheckedAsync(executable, [], output));
    }

    private static async Task<string> RunGoQueueScenarioAsync(string root)
    {
        var output = Path.Combine(root, "go");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @server -> queue<Int> { send 1 }");
        await WriteGeneratedFilesAsync(output, new GoCodeGenerator().Generate(document, new CodeGenOptions(output)));
        var server = Path.Combine(output, "server");
        await File.WriteAllTextAsync(Path.Combine(server, "semantic_queue_test.go"), """
            package main

            import (
                "fmt"
                "testing"
            )

            func TestSemanticQueue(t *testing.T) {
                queue := NewQueue[int]()
                queue.Enqueue(7)
                queue.Enqueue(8)
                first, firstID := queue.Recv()
                _, repeatedID := queue.Recv()
                queue.Nack(*firstID, "retry")
                _, redeliveryID := queue.Recv()
                queue.Ack("missing")
                _, afterInvalidID := queue.Recv()
                queue.Ack(*firstID)
                next, nextID := queue.Recv()
                fmt.Printf("JUNEXIS_SEMANTIC_RESULT={\"first\":{\"id\":\"%s\",\"value\":%d},\"repeatId\":\"%s\",\"redeliveryId\":\"%s\",\"afterInvalidAckId\":\"%s\",\"next\":{\"id\":\"%s\",\"value\":%d}}\n",
                    *firstID, first.Value, *repeatedID, *redeliveryID, *afterInvalidID, *nextID, next.Value)
            }
            """);
        return ExtractSemanticResult(await RunCheckedAsync("go", ["test", "-run", "TestSemanticQueue", "-v", "./..."], server));
    }

    private static async Task<string> RunTypeScriptQueueScenarioAsync(string root)
    {
        var output = Path.Combine(root, "typescript");
        Directory.CreateDirectory(output);
        var document = await TransformToIrAsync("task Source() @report -> queue<Int> { send 1 }");
        await WriteGeneratedFilesAsync(output, new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions(output)));
        var report = Path.Combine(output, "report");
        await File.WriteAllTextAsync(Path.Combine(report, "semantic-queue.ts"), """
            import { Queue } from './runtime.js';

            const queue = new Queue<number>();
            queue.enqueue(7);
            queue.enqueue(8);
            const first = await queue.recv();
            const repeated = await queue.recv();
            if (!first || !repeated) throw new Error('missing initial delivery');
            queue.nack(first.messageId, 'retry');
            const redelivery = await queue.recv();
            queue.ack('missing');
            const afterInvalid = await queue.recv();
            queue.ack(first.messageId);
            const next = await queue.recv();
            if (!redelivery || !afterInvalid || !next) throw new Error('missing queue evidence');
            console.log(`JUNEXIS_SEMANTIC_RESULT=${JSON.stringify({
              first: { id: first.messageId, value: first.value },
              repeatId: repeated.messageId,
              redeliveryId: redelivery.messageId,
              afterInvalidAckId: afterInvalid.messageId,
              next: { id: next.messageId, value: next.value }
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
        return ExtractSemanticResult(await RunCheckedAsync("node", [Path.Combine("dist", "semantic-queue.js")], report));
    }
}
