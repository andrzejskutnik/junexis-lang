namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    [Trait("Category", "RuntimeConformance02")]
    public async Task GeneratedCppRuntime_ConformsToJunexis02CommunicationContractAsync()
    {
        AssertJunexis02CommunicationContract();

        var document = await TransformToIrAsync("task Source() @embedded -> Int { 1 }");
        var root = Path.Combine(Path.GetTempPath(), $"junexis-runtime02-cpp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            await WriteGeneratedFilesAsync(root, new CppCodeGenerator().Generate(document, new CodeGenOptions(root)));

            await File.WriteAllTextAsync(Path.Combine(root, "Arduino.h"), """
                #pragma once
                class String {
                public:
                    template <typename T> String(const T&) {}
                };
                inline unsigned long millis() { return 0; }
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "runtime_conformance.cpp"), """
                #include <cassert>
                #include <atomic>
                #include <thread>
                #include <junexis/runtime.h>

                int main() {
                    // chan<T>: FIFO, retained until consumed, producer waits at capacity.
                    junexis::Channel<int> channel;
                    channel.send(1);
                    std::atomic<bool> second_sent{false};
                    std::thread producer([&] { channel.send(2); second_sent = true; });
                    std::this_thread::yield();
                    assert(!second_sent.load());
                    assert(channel.recv() == 1);
                    producer.join();
                    assert(second_sent.load());
                    assert(channel.recv() == 2);

                    // queue<T>: at-least-once until ACK; NACK keeps/requeues.
                    junexis::Queue<int> queue;
                    queue.enqueue(7);
                    auto first_delivery = queue.delivery();
                    auto repeated_delivery = queue.delivery();
                    assert(first_delivery.ok && first_delivery.value == 7);
                    assert(repeated_delivery.message_id == first_delivery.message_id);
                    queue.nack(first_delivery.message_id, "retry");
                    assert(queue.delivery().message_id == first_delivery.message_id);
                    queue.ack(first_delivery.message_id);
                    queue.enqueue(8);
                    assert(queue.recv() == 8);

                    // stream<T>: latest-wins, no history replay.
                    junexis::Stream<int> stream;
                    stream.publish(1);
                    stream.publish(2);
                    assert(stream.recv() == 2);
                    int observed = 0;
                    for (const auto value : stream) { assert(value == 2); ++observed; }
                    assert(observed == 1);
                }
                """);

            var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "runtime_conformance.exe" : "runtime_conformance");
            await RunCheckedAsync(
                "g++",
                ["-std=c++17", "-pthread", "-I", root, "-I", Path.Combine(root, "embedded"), Path.Combine(root, "runtime_conformance.cpp"), "-o", executable],
                root);
            await RunCheckedAsync(executable, [], root);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "RuntimeTrace")]
    public async Task GeneratedCppArtifact_EmitsCanonicalNarrativeTraceAsync()
    {
        const string source = """
            task Publish() @embedded -> stream<Text> {
                send "hello"
            }

            narrative Story {
                trace full
            }
            """;
        var document = await TransformToIrAsync(source);
        var root = Path.Combine(Path.GetTempPath(), $"junexis-trace-cpp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
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
                    template <typename T> String(const T&) {}
                    const char* c_str() const { return value_.c_str(); }
                private:
                    std::string value_;
                };
                inline unsigned long millis() {
                    static unsigned long value = 100;
                    return value++;
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "trace-conformance.cpp"), """
                #include <cassert>
                #include <string>
                #include <vector>
                #include "embedded/Publish.h"

                static std::vector<junexis::TraceEvent> captured;

                static uint64_t capture(const junexis::TraceEvent& event) {
                    captured.push_back(event);
                    return captured.size();
                }

                int main() {
                    junexis::set_trace_sink(capture);
                    (void)task_Publish();

                    assert(captured.size() == 2);
                    assert(captured[0].kind == "COMM_SEND");
                    assert(captured[0].task_id == 1);
                    assert(captured[0].attributes.narrative_id == "Story");
                    assert(captured[0].attributes.type == "task-start");
                    assert(captured[1].kind == "COMM_SEND");
                    assert(captured[1].attributes.narrative_id == "Story");
                    assert(captured[1].attributes.type == "Text");
                    assert(captured[1].attributes.payload_hash.find("Publish:2:") == 0);
                }
                """);

            var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "trace-conformance.exe" : "trace-conformance");
            await RunCheckedAsync(
                "g++",
                [
                    "-std=c++20",
                    "-I", root,
                    "-I", Path.Combine(root, "embedded"),
                    Path.Combine(root, "embedded", "Publish.cpp"),
                    Path.Combine(root, "trace-conformance.cpp"),
                    "-o", executable
                ],
                root);
            await RunCheckedAsync(executable, [], root);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "RuntimeConformance02")]
    public async Task GeneratedGoRuntime_ConformsToJunexis02CommunicationContractAsync()
    {
        AssertJunexis02CommunicationContract();

        const string source = """
            task QueueSource() @server -> queue<Int> { send 1 }
            task StreamSource() @server -> stream<Int> { send 1 }
            """;
        var document = await TransformToIrAsync(source);
        var root = Path.Combine(Path.GetTempPath(), $"junexis-runtime02-go-{Guid.NewGuid():N}");
        var serverDirectory = Path.Combine(root, "server");
        Directory.CreateDirectory(serverDirectory);

        try
        {
            await WriteGeneratedFilesAsync(root, new GoCodeGenerator().Generate(document, new CodeGenOptions(root)));

            await File.WriteAllTextAsync(Path.Combine(serverDirectory, "runtime_conformance_test.go"), """
                package main

                import "testing"

                func TestJunexis02RuntimeConformance(t *testing.T) {
                    // queue<T>: at-least-once until ACK; message id remains stable before ACK.
                    queue := NewQueue[int]()
                    queue.Enqueue(7)
                    first, firstID := queue.Recv()
                    if !first.Ok || firstID == nil || first.Value != 7 { t.Fatal("queue did not deliver head") }
                    repeated, repeatedID := queue.Recv()
                    if !repeated.Ok || repeatedID == nil || *repeatedID != *firstID { t.Fatal("queue lost unacked message") }
                    queue.Nack(*firstID, "retry")
                    afterNack, afterNackID := queue.Recv()
                    if !afterNack.Ok || afterNackID == nil || *afterNackID != *firstID { t.Fatal("nack removed message") }
                    queue.Ack(*firstID)
                    empty, emptyID := queue.Recv()
                    if empty.Ok || emptyID != nil { t.Fatal("ack did not remove message") }
                    queue.Enqueue(8)
                    queue.Enqueue(9)
                    queue.Enqueue(10)
                    batch := queue.Batch(2)
                    if len(batch) != 2 || batch[0] != 8 || batch[1] != 9 { t.Fatal("batch did not return head snapshot") }
                    stillQueued, _ := queue.Recv()
                    if !stillQueued.Ok || stillQueued.Value != 8 { t.Fatal("batch removed or reordered queue items") }

                    // stream<T>: latest-wins, no history replay.
                    stream := NewStream[int]()
                    stream.Publish(1)
                    stream.Publish(2)
                    if !stream.HasLatest() || stream.Latest() != 2 { t.Fatal("stream is not latest-wins") }

                    // chan<T>: native bounded Go channel demonstrates producer backpressure.
                    channel := make(chan int, 1)
                    channel <- 1
                    select {
                    case channel <- 2:
                        t.Fatal("bounded channel accepted value without backpressure")
                    default:
                    }
                    if <-channel != 1 { t.Fatal("channel is not FIFO") }
                }
                """);

            await RunCheckedAsync("go", ["test", "./..."], serverDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "RuntimeConformance02")]
    public async Task GeneratedTypeScriptRuntime_ConformsToJunexis02CommunicationContractAsync()
    {
        AssertJunexis02CommunicationContract();

        const string source = """
            task QueueSource() @report -> queue<Int> { send 1 }
            task StreamSource() @report -> stream<Int> { send 1 }
            """;
        var document = await TransformToIrAsync(source);
        var root = Path.Combine(Path.GetTempPath(), $"junexis-runtime02-ts-{Guid.NewGuid():N}");
        var reportDirectory = Path.Combine(root, "report");
        Directory.CreateDirectory(reportDirectory);

        try
        {
            await WriteGeneratedFilesAsync(root, new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions(root)));

            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "runtime-conformance.ts"), """
                import { Channel, Queue, Stream } from './runtime.js';

                // chan<T>: bounded FIFO; the second producer waits until consumption.
                const channel = new Channel<number>(1);
                await channel.send(1);
                let secondSent = false;
                const secondSend = channel.send(2).then(() => { secondSent = true; });
                await Promise.resolve();
                if (secondSent) throw new Error('bounded channel accepted value without backpressure');
                if (await channel.recv() !== 1) throw new Error('channel is not FIFO');
                await secondSend;
                if (await channel.recv() !== 2) throw new Error('channel lost retained value');
                channel.close();

                // queue<T>: at-least-once until ACK; message id remains stable before ACK.
                const queue = new Queue<number>();
                queue.enqueue(7);
                const first = await queue.recv();
                const repeated = await queue.recv();
                if (!first || !repeated || first.messageId !== repeated.messageId) throw new Error('queue lost unacked message');
                queue.nack(first.messageId, 'retry');
                const afterNack = await queue.recv();
                if (!afterNack || afterNack.messageId !== first.messageId) throw new Error('nack removed message');
                queue.ack(first.messageId);
                if (await queue.recv()) throw new Error('ack did not remove message');
                queue.enqueue(8);
                queue.enqueue(9);
                queue.enqueue(10);
                const batch = queue.batch(2);
                if (batch.length !== 2 || batch[0] !== 8 || batch[1] !== 9) throw new Error('batch did not return head snapshot');
                const stillQueued = await queue.recv();
                if (!stillQueued || stillQueued.value !== 8) throw new Error('batch removed or reordered queue items');

                // stream<T>: latest-wins, no history replay.
                const stream = new Stream<number>();
                stream.publish(1);
                stream.publish(2);
                if (stream.latest() !== 2) throw new Error('stream is not latest-wins');
                const observed: number[] = [];
                for await (const value of stream) observed.push(value);
                if (observed.length !== 1 || observed[0] !== 2) throw new Error('stream retained history');

                console.log('JUNEXIS_RUNTIME_02_TS_OK');
                """);

            await RunCheckedAsync(
                OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                OperatingSystem.IsWindows()
                    ? ["/d", "/s", "/c", "npm install --ignore-scripts"]
                    : ["install", "--ignore-scripts"],
                reportDirectory);
            await RunCheckedAsync(
                OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                OperatingSystem.IsWindows()
                    ? ["/d", "/s", "/c", "npm exec tsc -- --noEmit false --outDir dist"]
                    : ["exec", "tsc", "--", "--noEmit", "false", "--outDir", "dist"],
                reportDirectory);
            var output = await RunCheckedAsync("node", [Path.Combine("dist", "runtime-conformance.js")], reportDirectory);

            Assert.Contains("JUNEXIS_RUNTIME_02_TS_OK", output);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "RuntimeTrace")]
    public async Task GeneratedTypeScriptArtifact_EmitsCanonicalNarrativeTraceAsync()
    {
        const string source = """
            task Publish() @report -> stream<Text> {
                send "hello"
            }

            narrative Story {
                trace full
            }
            """;
        var document = await TransformToIrAsync(source);
        var root = Path.Combine(Path.GetTempPath(), $"junexis-trace-ts-{Guid.NewGuid():N}");
        var reportDirectory = Path.Combine(root, "report");
        Directory.CreateDirectory(reportDirectory);

        try
        {
            await WriteGeneratedFilesAsync(root, new TypeScriptCodeGenerator().Generate(document, new CodeGenOptions(root)));
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "trace-conformance.ts"), """
                const captured: Array<{ url: string; body?: string }> = [];
                const globals = globalThis as typeof globalThis & {
                  JUNEXIS_TRACE_URL?: string;
                  JUNEXIS_BUILD_ID?: string;
                  JUNEXIS_BUNDLE_ID?: string;
                };
                globals.JUNEXIS_TRACE_URL = 'http://trace.test';
                globals.JUNEXIS_BUILD_ID = 'build-test';
                globals.JUNEXIS_BUNDLE_ID = 'bundle-test';
                globals.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
                  const url = String(input);
                  captured.push({ url, body: typeof init?.body === 'string' ? init.body : undefined });
                  if (url.endsWith('/events')) {
                    return new Response(JSON.stringify({
                      sampled: true,
                      event: { eventId: captured.length }
                    }), { status: 201, headers: { 'Content-Type': 'application/json' } });
                  }
                  return new Response(JSON.stringify({ status: 'Running' }), {
                    status: 200,
                    headers: { 'Content-Type': 'application/json' }
                  });
                };

                const { Publish } = await import('./index.js');
                await Publish();

                const events = captured
                  .filter(request => request.url.endsWith('/events'))
                  .map(request => JSON.parse(request.body ?? '{}'));
                if (events.length !== 2) throw new Error(`expected 2 trace events, received ${events.length}`);
                if (events.some(event => event.kind !== 'COMM_SEND')) throw new Error('unexpected trace kind');
                if (events.some(event => event.attributes.narrative_id !== 'Story')) throw new Error('missing narrative context');
                if (events.some(event => event.attributes.build_id !== 'build-test')) throw new Error('missing build context');
                if (events.some(event => event.attributes.bundle_id !== 'bundle-test')) throw new Error('missing bundle context');
                if (events[1].attributes.type !== 'Text') throw new Error('missing payload type');
                if (!captured.some(request => request.url.endsWith('/debug/session'))) throw new Error('debug gate was not queried');

                console.log('JUNEXIS_TRACE_TS_OK');
                """);

            await RunCheckedAsync(
                OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                OperatingSystem.IsWindows()
                    ? ["/d", "/s", "/c", "npm install --ignore-scripts"]
                    : ["install", "--ignore-scripts"],
                reportDirectory);
            await RunCheckedAsync(
                OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                OperatingSystem.IsWindows()
                    ? ["/d", "/s", "/c", "npm exec tsc -- --noEmit false --outDir dist"]
                    : ["exec", "tsc", "--", "--noEmit", "false", "--outDir", "dist"],
                reportDirectory);
            var output = await RunCheckedAsync("node", [Path.Combine("dist", "trace-conformance.js")], reportDirectory);

            Assert.Contains("JUNEXIS_TRACE_TS_OK", output);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WriteGeneratedFilesAsync(string root, IReadOnlyCollection<GeneratedFile> files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(root, file.FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Content);
        }
    }

    private static void AssertJunexis02CommunicationContract()
    {
        Assert.Equal(CommunicationDeliverySemantics.Backpressured, CommunicationRuntimeContractCatalog.Chan.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerWaits, CommunicationRuntimeContractCatalog.Chan.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.UntilConsumed, CommunicationRuntimeContractCatalog.Chan.Retention);
        Assert.Equal(CommunicationOrderingSemantics.Fifo, CommunicationRuntimeContractCatalog.Chan.Ordering);

        Assert.Equal(CommunicationDeliverySemantics.AtLeastOnce, CommunicationRuntimeContractCatalog.Queue.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerContinues, CommunicationRuntimeContractCatalog.Queue.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.UntilAck, CommunicationRuntimeContractCatalog.Queue.Retention);
        Assert.Equal(CommunicationOrderingSemantics.Fifo, CommunicationRuntimeContractCatalog.Queue.Ordering);

        Assert.Equal(CommunicationDeliverySemantics.LatestWins, CommunicationRuntimeContractCatalog.Stream.Delivery);
        Assert.Equal(CommunicationBackpressureSemantics.ProducerContinues, CommunicationRuntimeContractCatalog.Stream.Backpressure);
        Assert.Equal(CommunicationRetentionSemantics.LatestOnly, CommunicationRuntimeContractCatalog.Stream.Retention);
        Assert.Equal(CommunicationOrderingSemantics.LatestOnly, CommunicationRuntimeContractCatalog.Stream.Ordering);
    }
}
