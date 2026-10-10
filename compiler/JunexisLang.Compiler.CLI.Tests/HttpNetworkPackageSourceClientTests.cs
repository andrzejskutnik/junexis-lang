using System.Net;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class HttpNetworkPackageSourceClientTests
{
    [Fact]
    public async Task RequestsAndRedirectsCarryTheRestoreCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var handler = new SequenceHandler(
            Redirect("/snapshot.json"),
            Content(System.Text.Encoding.UTF8.GetBytes("{}")));
        using var client = new HttpNetworkPackageSourceClient(handler, correlationId: correlationId);

        await client.GetIndexAsync(
            new Uri("https://registry.example.test/v1/snapshot"),
            CancellationToken.None);

        Assert.Equal([correlationId, correlationId], handler.CorrelationIds);
    }

    [Fact]
    public async Task CopyArtifactAsync_FollowsSameAuthorityHttpsRedirectAndStreamsContent()
    {
        var handler = new SequenceHandler(
            Redirect("/artifacts/package.zip"),
            Content(new byte[] { 1, 2, 3 }));
        using var client = new HttpNetworkPackageSourceClient(handler);
        await using var destination = new MemoryStream();

        await client.CopyArtifactAsync(
            new Uri("https://registry.example.test/download"),
            destination,
            CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, destination.ToArray());
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal("https://registry.example.test/artifacts/package.zip", handler.RequestUris[1].ToString());
    }

    [Fact]
    public async Task CopyArtifactAsync_RejectsCrossAuthorityRedirect()
    {
        using var client = new HttpNetworkPackageSourceClient(new SequenceHandler(
            Redirect("https://other.example.test/package.zip")));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.CopyArtifactAsync(
            new Uri("https://registry.example.test/download"),
            new MemoryStream(),
            CancellationToken.None));

        Assert.Contains("authority", error.Message);
    }

    [Fact]
    public async Task CopyArtifactAsync_RejectsHttpsDowngradeRedirect()
    {
        using var client = new HttpNetworkPackageSourceClient(new SequenceHandler(
            Redirect("http://registry.example.test/package.zip")));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.CopyArtifactAsync(
            new Uri("https://registry.example.test/download"),
            new MemoryStream(),
            CancellationToken.None));

        Assert.Contains("HTTPS", error.Message);
    }

    [Fact]
    public async Task CopyArtifactAsync_RejectsSixthRedirect()
    {
        using var client = new HttpNetworkPackageSourceClient(new SequenceHandler(
            Redirect("/one"),
            Redirect("/two"),
            Redirect("/three"),
            Redirect("/four"),
            Redirect("/five"),
            Redirect("/six")));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.CopyArtifactAsync(
            new Uri("https://registry.example.test/download"),
            new MemoryStream(),
            CancellationToken.None));

        Assert.Contains("5 redirects", error.Message);
    }

    [Fact]
    public async Task GetIndexAsync_DoesNotRetryNotFound()
    {
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: NoDelayAsync);

        var error = await Assert.ThrowsAsync<RegistryHttpResponseException>(() => client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("Registry returned HTTP 404 (artifact-not-found).", error.Message);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task CopyArtifactAsync_RejectsPartialContentWithoutRetry()
    {
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent([1, 2, 3])
        });
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: NoDelayAsync);

        var error = await Assert.ThrowsAsync<RegistryHttpResponseException>(() => client.CopyArtifactAsync(
            new Uri("https://registry.example.test/package.zip"),
            new MemoryStream(),
            CancellationToken.None));

        Assert.Equal("Registry returned HTTP 206 (unexpected-http-status).", error.Message);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetIndexAsync_RetriesRateLimitAndHonorsRetryAfter()
    {
        var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        var handler = new SequenceHandler(limited, Content(System.Text.Encoding.UTF8.GetBytes("{}")));
        var delays = new List<TimeSpan>();
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        Assert.Equal("{}", await client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"), CancellationToken.None));

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(delays));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData((HttpStatusCode)599)]
    public async Task GetIndexAsync_RetriesServerFailures(HttpStatusCode statusCode)
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(statusCode),
            Content(System.Text.Encoding.UTF8.GetBytes("{}")));
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: NoDelayAsync);

        Assert.Equal("{}", await client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"), CancellationToken.None));
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetIndexAsync_RetriesConnectionFailure()
    {
        var handler = new ScriptedHandler(
            (_, _) => throw new HttpRequestException("unstable transport"),
            (_, _) => Task.FromResult(Content(System.Text.Encoding.UTF8.GetBytes("{}"))));
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: NoDelayAsync);

        Assert.Equal("{}", await client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"), CancellationToken.None));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CopyArtifactAsync_RetriesInterruptedTransferFromCleanDestinationPosition()
    {
        var interrupted = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FaultAfterPrefixStream(new byte[] { 9, 9 }))
        };
        var handler = new SequenceHandler(interrupted, Content(new byte[] { 1, 2, 3 }));
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: NoDelayAsync);
        await using var destination = new MemoryStream();

        await client.CopyArtifactAsync(
            new Uri("https://registry.example.test/package.zip"), destination, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, destination.ToArray());
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetIndexAsync_ExhaustedServerFailureHasStableDiagnostic()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.ServiceUnavailable),
            new(HttpStatusCode.ServiceUnavailable),
            new(HttpStatusCode.ServiceUnavailable),
            new(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: NoDelayAsync);

        var error = await Assert.ThrowsAsync<RegistryHttpResponseException>(() => client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"), CancellationToken.None));

        Assert.Equal("Registry returned HTTP 503 (registry-unavailable).", error.Message);
        Assert.Equal(4, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetIndexAsync_CancellationDuringRetryDelayStopsFurtherRequests()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"), cancellation.Token));
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetIndexAsync_RejectsDeclaredLengthAboveLimit()
    {
        var response = Content(new byte[] { 1 });
        response.Content.Headers.ContentLength = PackageJson.MaximumDocumentSizeBytes + 1L;
        using var client = new HttpNetworkPackageSourceClient(new SequenceHandler(response));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"),
            CancellationToken.None));

        Assert.Contains("size limit", error.Message);
    }

    [Fact]
    public async Task GetIndexAsync_RejectsActualLengthAboveLimitWithoutContentLength()
    {
        var content = new StreamContent(new MemoryStream(new byte[PackageJson.MaximumDocumentSizeBytes + 1]));
        content.Headers.ContentLength = null;
        using var client = new HttpNetworkPackageSourceClient(new SequenceHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"),
            CancellationToken.None));

        Assert.Contains("size limit", error.Message);
    }

    [Fact]
    public async Task CopyArtifactAsync_RejectsTransferShorterThanDeclaredLength()
    {
        static HttpResponseMessage ShortResponse()
        {
            var response = Content(new byte[] { 1, 2, 3 });
            response.Content.Headers.ContentLength = 4;
            return response;
        }
        var handler = new SequenceHandler(ShortResponse(), ShortResponse(), ShortResponse(), ShortResponse());
        using var client = new HttpNetworkPackageSourceClient(handler, delayAsync: NoDelayAsync);

        var error = await Assert.ThrowsAsync<RegistryTransportException>(() => client.CopyArtifactAsync(
            new Uri("https://registry.example.test/package.zip"),
            new MemoryStream(),
            CancellationToken.None));

        Assert.Contains("declared length", error.Message);
        Assert.Equal(4, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetIndexAsync_ConvertsInternalCancellationToTimeout()
    {
        using var client = new HttpNetworkPackageSourceClient(
            new DelayingHandler(),
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TimeoutException>(() => client.GetIndexAsync(
            new Uri("https://registry.example.test/index.json"),
            CancellationToken.None));
    }

    [Fact]
    public async Task CopyArtifactAsync_PreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var client = new HttpNetworkPackageSourceClient(new DelayingHandler());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CopyArtifactAsync(
            new Uri("https://registry.example.test/package.zip"),
            new MemoryStream(),
            cancellation.Token));
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Content(byte[] content) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(content)
    };

    private static Task NoDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        internal List<Uri> RequestUris { get; } = [];
        internal List<string?> CorrelationIds { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            CorrelationIds.Add(request.Headers.GetValues("X-Correlation-Id").SingleOrDefault());
            return Task.FromResult(responses.Dequeue());
        }
    }

    private sealed class DelayingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class ScriptedHandler(
        params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> steps = new(steps);

        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return steps.Dequeue()(request, cancellationToken);
        }
    }

    private sealed class FaultAfterPrefixStream(byte[] prefix) : MemoryStream(prefix)
    {
        private bool prefixRead;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (prefixRead)
                throw new IOException("simulated connection loss");
            prefixRead = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
