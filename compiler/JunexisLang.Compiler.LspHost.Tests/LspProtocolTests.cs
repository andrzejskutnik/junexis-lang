using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace JunexisLang.Compiler.LspHost.Tests;

public sealed class LspProtocolTests
{
    [Fact]
    public async Task StdioSession_AdvertisesAndServesM5LanguageFeatures()
    {
        await using var client = LspProcessClient.Start();
        const string uri = "file:///m5-protocol.jnx";
        const string source = "struct Payload { value: Int }\ntask Worker(input: Payload) @server { }\npipeline Broken { use Worker -> Missing }";

        await client.SendRequestAsync(1, "initialize", new
        {
            processId = (int?)null,
            rootUri = (string?)null,
            capabilities = ClientCapabilities()
        });
        using var initialize = await client.ReadResponseAsync(1);
        var capabilities = initialize.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(IsEnabled(capabilities.GetProperty("hoverProvider")));
        Assert.True(IsEnabled(capabilities.GetProperty("definitionProvider")));
        Assert.True(IsEnabled(capabilities.GetProperty("documentSymbolProvider")));
        Assert.True(capabilities.TryGetProperty("completionProvider", out _));
        Assert.True(capabilities.TryGetProperty("semanticTokensProvider", out _));

        await client.SendNotificationAsync("initialized", new { });
        await client.SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new { uri, languageId = "junexis", version = 1, text = source }
        });

        using var diagnostics = await client.ReadNotificationAsync("textDocument/publishDiagnostics");
        var published = diagnostics.RootElement.GetProperty("params").GetProperty("diagnostics");
        Assert.Contains(published.EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("code").ToString() == "T0002");

        await client.SendRequestAsync(2, "textDocument/hover", Position(uri, 1, 22));
        using var hover = await client.ReadResponseAsync(2);
        Assert.NotEqual(JsonValueKind.Null, hover.RootElement.GetProperty("result").ValueKind);

        await client.SendRequestAsync(3, "textDocument/completion", Position(uri, 1, 0));
        using var completion = await client.ReadResponseAsync(3);
        var completionResult = completion.RootElement.GetProperty("result");
        var completionItems = completionResult.ValueKind == JsonValueKind.Array
            ? completionResult
            : completionResult.GetProperty("items");
        Assert.True(completionItems.GetArrayLength() > 0);

        await client.SendRequestAsync(4, "textDocument/definition", Position(uri, 1, 22));
        using var definition = await client.ReadResponseAsync(4);
        Assert.NotEqual(JsonValueKind.Null, definition.RootElement.GetProperty("result").ValueKind);

        await client.SendRequestAsync(5, "textDocument/documentSymbol", Document(uri));
        using var symbols = await client.ReadResponseAsync(5);
        Assert.Equal(3, symbols.RootElement.GetProperty("result").GetArrayLength());

        await client.SendRequestAsync(6, "textDocument/semanticTokens/full", Document(uri));
        using var semanticTokens = await client.ReadResponseAsync(6);
        var tokenData = semanticTokens.RootElement.GetProperty("result").GetProperty("data");
        Assert.True(tokenData.GetArrayLength() > 0);
        Assert.Equal(0, tokenData.GetArrayLength() % 5);

        await client.SendRequestAsync(7, "shutdown", new { });
        using var shutdown = await client.ReadResponseAsync(7);
        await client.SendNotificationAsync("exit", new { });
        Assert.True(await client.WaitForExitAsync());
    }

    [Fact]
    public async Task StdioSession_RePublishesDiagnosticsOnChangeAndClearsThemOnClose()
    {
        await using var client = LspProcessClient.Start();
        const string uri = "file:///m5-sync.jnx";
        const string broken = "task A() { } pipeline Broken { use A -> Missing }";
        const string fixedSource = "task A() { } pipeline Fixed { use A }";

        await client.SendRequestAsync(1, "initialize", new
        {
            processId = (int?)null,
            rootUri = (string?)null,
            capabilities = ClientCapabilities()
        });
        using var initialize = await client.ReadResponseAsync(1);
        Assert.True(initialize.RootElement.TryGetProperty("result", out _));

        await client.SendNotificationAsync("initialized", new { });
        await client.SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new { uri, languageId = "junexis", version = 1, text = broken }
        });

        using var initialDiagnostics = await client.ReadNotificationAsync("textDocument/publishDiagnostics");
        AssertDiagnosticsContain(initialDiagnostics, "T0002");

        await client.SendNotificationAsync("textDocument/didChange", new
        {
            textDocument = new { uri, version = 2 },
            contentChanges = new[] { new { text = fixedSource } }
        });

        using var changedDiagnostics = await client.ReadNotificationAsync("textDocument/publishDiagnostics");
        AssertDiagnosticsDoNotContain(changedDiagnostics, "T0002");

        await client.SendNotificationAsync("textDocument/didClose", new
        {
            textDocument = new { uri }
        });

        using var closedDiagnostics = await client.ReadNotificationAsync("textDocument/publishDiagnostics");
        Assert.Empty(closedDiagnostics.RootElement.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        await client.SendRequestAsync(2, "shutdown", new { });
        using var shutdown = await client.ReadResponseAsync(2);
        await client.SendNotificationAsync("exit", new { });
        Assert.True(await client.WaitForExitAsync());
    }

    private static object Document(string uri) => new
    {
        textDocument = new { uri }
    };

    private static object Position(string uri, int line, int character) => new
    {
        textDocument = new { uri },
        position = new { line, character }
    };

    private static object ClientCapabilities() => new
    {
        textDocument = new
        {
            synchronization = new { dynamicRegistration = false },
            hover = new { dynamicRegistration = false },
            completion = new { dynamicRegistration = false },
            definition = new { dynamicRegistration = false },
            documentSymbol = new { dynamicRegistration = false },
            semanticTokens = new
            {
                dynamicRegistration = false,
                requests = new { range = true, full = true },
                tokenTypes = new[] { "comment", "keyword", "string", "number", "operator", "type", "struct", "enum", "function", "event", "namespace", "interface", "decorator", "variable" },
                tokenModifiers = Array.Empty<string>(),
                formats = new[] { "relative" }
            }
        }
    };

    private static bool IsEnabled(JsonElement capability) =>
        capability.ValueKind == JsonValueKind.Object ||
        capability.ValueKind == JsonValueKind.True;

    private static void AssertDiagnosticsContain(JsonDocument diagnostics, string code)
    {
        var published = diagnostics.RootElement.GetProperty("params").GetProperty("diagnostics");
        Assert.Contains(published.EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("code").ToString() == code);
    }

    private static void AssertDiagnosticsDoNotContain(JsonDocument diagnostics, string code)
    {
        var published = diagnostics.RootElement.GetProperty("params").GetProperty("diagnostics");
        Assert.DoesNotContain(published.EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("code").ToString() == code);
    }

    private sealed class LspProcessClient : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Stream _input;
        private readonly Stream _output;

        private LspProcessClient(Process process)
        {
            _process = process;
            _input = process.StandardInput.BaseStream;
            _output = process.StandardOutput.BaseStream;
        }

        public static LspProcessClient Start()
        {
            var assembly = typeof(LspHostRunner).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(assembly);

            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start LSP host.");
            return new LspProcessClient(process);
        }

        public Task SendRequestAsync(int id, string method, object parameters) =>
            SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters });

        public Task SendNotificationAsync(string method, object parameters) =>
            SendAsync(new { jsonrpc = "2.0", method, @params = parameters });

        public Task<JsonDocument> ReadResponseAsync(int id) =>
            ReadUntilAsync(root => root.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id);

        public Task<JsonDocument> ReadNotificationAsync(string method) =>
            ReadUntilAsync(root => root.TryGetProperty("method", out var value) && value.GetString() == method);

        public async Task<bool> WaitForExitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _process.WaitForExitAsync(timeout.Token);
                return _process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _input.DisposeAsync();
            await _output.DisposeAsync();
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }

        private async Task SendAsync(object message)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await _input.WriteAsync(header);
            await _input.WriteAsync(body);
            await _input.FlushAsync();
        }

        private async Task<JsonDocument> ReadUntilAsync(Func<JsonElement, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var document = await ReadMessageAsync(timeout.Token);
                if (predicate(document.RootElement))
                    return document;
                document.Dispose();
            }
        }

        private async Task<JsonDocument> ReadMessageAsync(CancellationToken cancellationToken)
        {
            var header = new List<byte>();
            while (!EndsWithHeaderTerminator(header))
            {
                var value = await ReadByteAsync(cancellationToken);
                header.Add(value);
            }

            var headerText = Encoding.ASCII.GetString(header.ToArray());
            var contentLengthLine = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            var contentLength = int.Parse(contentLengthLine[(contentLengthLine.IndexOf(':') + 1)..].Trim());
            var body = new byte[contentLength];
            var offset = 0;
            while (offset < body.Length)
            {
                var read = await _output.ReadAsync(body.AsMemory(offset), cancellationToken);
                if (read == 0)
                    throw new EndOfStreamException("LSP host closed stdout before completing a message.");
                offset += read;
            }

            return JsonDocument.Parse(body);
        }

        private async Task<byte> ReadByteAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[1];
            var read = await _output.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("LSP host closed stdout.");
            return buffer[0];
        }

        private static bool EndsWithHeaderTerminator(IReadOnlyList<byte> value) =>
            value.Count >= 4 &&
            value[^4] == '\r' && value[^3] == '\n' &&
            value[^2] == '\r' && value[^1] == '\n';
    }
}
