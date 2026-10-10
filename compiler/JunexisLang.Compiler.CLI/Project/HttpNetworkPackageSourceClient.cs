using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace JunexisLang.Compiler.CLI.Project;

internal sealed class HttpNetworkPackageSourceClient : INetworkPackageSourceClient, IDisposable
{
    private const int MaximumRedirects = 5;
    private const int MaximumAttempts = 4;
    private static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(120);
    private readonly HttpClient httpClient;
    private readonly TimeSpan attemptTimeout;
    private readonly TimeSpan operationTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly string correlationId;

    internal HttpNetworkPackageSourceClient(TimeSpan? requestTimeout = null, string? correlationId = null)
        : this(CreateHandler(), requestTimeout, correlationId: correlationId)
    {
    }

    internal HttpNetworkPackageSourceClient(
        HttpMessageHandler handler,
        TimeSpan? requestTimeout = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        attemptTimeout = requestTimeout ?? DefaultAttemptTimeout;
        operationTimeout = requestTimeout ?? DefaultOperationTimeout;
        ValidateTimeout(attemptTimeout, nameof(requestTimeout));
        ValidateTimeout(operationTimeout, nameof(requestTimeout));
        this.delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
        this.correlationId = NormalizeCorrelationId(correlationId);
        httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Task<string> GetIndexAsync(Uri indexUri, CancellationToken cancellationToken) =>
        ExecuteWithPolicyAsync(async operationToken =>
        {
            using var response = await SendOnceAsync(indexUri, operationToken);
            ValidateDeclaredLength(response, PackageJson.MaximumDocumentSizeBytes, "Registry package index");
            await using var source = await response.Content.ReadAsStreamAsync(operationToken);
            using var destination = new MemoryStream();
            await CopyBoundedAsync(source, destination, PackageJson.MaximumDocumentSizeBytes,
                response.Content.Headers.ContentLength, "Registry package index", operationToken);
            return System.Text.Encoding.UTF8.GetString(destination.ToArray());
        }, resetBeforeRetry: _ => Task.CompletedTask, cancellationToken);

    public Task CopyArtifactAsync(Uri artifactUri, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var initialPosition = destination.CanSeek ? destination.Position : 0;
        var initialLength = destination.CanSeek ? destination.Length : 0;
        return ExecuteWithPolicyAsync(async operationToken =>
        {
            using var response = await SendOnceAsync(artifactUri, operationToken);
            ValidateDeclaredLength(response, PackageContractValidator.MaximumPackageSizeBytes, "Package artifact");
            await using var source = await response.Content.ReadAsStreamAsync(operationToken);
            await CopyBoundedAsync(source, destination, PackageContractValidator.MaximumPackageSizeBytes,
                response.Content.Headers.ContentLength, "Package artifact", operationToken);
            return true;
        }, destination.CanSeek
            ? _ =>
            {
                destination.SetLength(initialLength);
                destination.Position = initialPosition;
                return Task.CompletedTask;
            }
            : null,
            cancellationToken);
    }

    public void Dispose() => httpClient.Dispose();

    private static SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxResponseHeadersLength = 64
        };
        var customRootPath = Environment.GetEnvironmentVariable("JUNEXIS_REGISTRY_CA_CERTIFICATE");
        if (string.IsNullOrWhiteSpace(customRootPath))
            return handler;

        var fullPath = Path.GetFullPath(customRootPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Nie istnieje certyfikat CA dla Junexis Registry.", fullPath);
        var customRoot = X509CertificateLoader.LoadCertificateFromFile(fullPath);
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
        {
            if (certificate is null ||
                (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
                return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(customRoot);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(new X509Certificate2(certificate));
        };
        return handler;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        ValidateInitialUri(initialUri);
        var currentUri = initialUri;
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.Add("X-Correlation-Id", correlationId);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                if (response.StatusCode == HttpStatusCode.OK)
                    return response;
                var failure = CreateResponseFailure(response);
                response.Dispose();
                throw failure;
            }

            if (redirectCount >= MaximumRedirects)
            {
                response.Dispose();
                throw new InvalidDataException($"Registry response exceeds the limit of {MaximumRedirects} redirects.");
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                throw new InvalidDataException("Registry redirect does not contain a Location header.");
            var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            ValidateRedirectUri(initialUri, nextUri);
            currentUri = nextUri;
        }
    }

    private async Task<T> ExecuteWithPolicyAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task>? resetBeforeRetry,
        CancellationToken cancellationToken)
    {
        using var operationBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationBudget.CancelAfter(operationTimeout);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                using var attemptBudget = CancellationTokenSource.CreateLinkedTokenSource(operationBudget.Token);
                attemptBudget.CancelAfter(attemptTimeout);
                try
                {
                    return await operation(attemptBudget.Token);
                }
                catch (OperationCanceledException ex) when (
                    !operationBudget.IsCancellationRequested && attemptBudget.IsCancellationRequested)
                {
                    if (attempt >= MaximumAttempts)
                        throw new TimeoutException($"Registry request timed out after {MaximumAttempts} attempts.", ex);
                    await PrepareRetryAsync(attempt, retryAfter: null, resetBeforeRetry, operationBudget.Token);
                }
                catch (RegistryHttpResponseException ex) when (ex.Retryable && attempt < MaximumAttempts)
                {
                    await PrepareRetryAsync(attempt, ex.RetryAfter, resetBeforeRetry, operationBudget.Token);
                }
                catch (Exception ex) when (IsRetryableTransportFailure(ex) && attempt < MaximumAttempts)
                {
                    await PrepareRetryAsync(attempt, retryAfter: null, resetBeforeRetry, operationBudget.Token);
                }
            }
        }
        catch (OperationCanceledException ex) when (
            !cancellationToken.IsCancellationRequested && operationBudget.IsCancellationRequested)
        {
            throw new TimeoutException("Registry operation timed out.", ex);
        }
    }

    private async Task PrepareRetryAsync(
        int attempt,
        TimeSpan? retryAfter,
        Func<CancellationToken, Task>? resetBeforeRetry,
        CancellationToken cancellationToken)
    {
        if (resetBeforeRetry is null)
            throw new RegistryTransportException("Registry transfer failed and its destination cannot be safely retried.");
        await resetBeforeRetry(cancellationToken);
        var exponentialMilliseconds = BaseRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var localDelay = TimeSpan.FromMilliseconds(Math.Min(exponentialMilliseconds, MaximumRetryDelay.TotalMilliseconds));
        var boundedRetryAfter = retryAfter is null
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(Math.Min(Math.Max(0, retryAfter.Value.TotalMilliseconds), MaximumRetryAfter.TotalMilliseconds));
        await delayAsync(localDelay >= boundedRetryAfter ? localDelay : boundedRetryAfter, cancellationToken);
    }

    private static RegistryHttpResponseException CreateResponseFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var retryable = status == 429 || status is >= 500 and <= 599;
        var code = status switch
        {
            404 => "artifact-not-found",
            409 => "artifact-conflict",
            413 => "payload-too-large",
            429 => "rate-limited",
            >= 500 and <= 599 => "registry-unavailable",
            _ => "unexpected-http-status"
        };
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
            retryAfter = date - DateTimeOffset.UtcNow;
        return new RegistryHttpResponseException(
            $"Registry returned HTTP {status} ({code}).", response.StatusCode, code, retryable, retryAfter);
    }

    private static bool IsRetryableTransportFailure(Exception exception) =>
        exception is RegistryTransportException ||
        exception is HttpRequestException and not RegistryHttpResponseException;

    private static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maximumLength,
        long? declaredLength,
        string label,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            int read;
            try
            {
                read = await source.ReadAsync(buffer, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new RegistryTransportException($"{label} transfer was interrupted.", ex);
            }
            if (read == 0)
                break;
            if (copied > maximumLength - read)
                throw new InvalidDataException($"{label} exceeds the size limit.");
            copied += read;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (declaredLength is not null && copied != declaredLength)
            throw new RegistryTransportException($"{label} transfer ended before the declared length was received.");
    }

    private static void ValidateDeclaredLength(HttpResponseMessage response, long maximumLength, string label)
    {
        if (response.Content.Headers.ContentLength > maximumLength)
            throw new InvalidDataException($"{label} exceeds the size limit.");
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(parameterName, "Request timeout must be positive and finite.");
    }

    private static string NormalizeCorrelationId(string? value)
    {
        if (value is null)
            return Guid.NewGuid().ToString("D");
        if (!Guid.TryParseExact(value, "D", out var parsed))
            throw new ArgumentException("Correlation ID must be a UUID.", nameof(value));
        return parsed.ToString("D");
    }

    private static void ValidateInitialUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Registry URI must be an absolute HTTPS URI without user information.");
    }

    private static void ValidateRedirectUri(Uri initialUri, Uri redirectUri)
    {
        if (redirectUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Registry redirect must use HTTPS.");
        if (!string.Equals(initialUri.Authority, redirectUri.Authority, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Registry redirect cannot change authority.");
        if (!string.IsNullOrEmpty(redirectUri.UserInfo))
            throw new InvalidDataException("Registry redirect cannot contain user information.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}

internal sealed class RegistryHttpResponseException(
    string message,
    HttpStatusCode statusCode,
    string errorCode,
    bool retryable,
    TimeSpan? retryAfter) : HttpRequestException(message, inner: null, statusCode)
{
    internal string ErrorCode { get; } = errorCode;
    internal bool Retryable { get; } = retryable;
    internal TimeSpan? RetryAfter { get; } = retryAfter;
}

internal sealed class RegistryTransportException : IOException
{
    internal RegistryTransportException(string message) : base(message) { }
    internal RegistryTransportException(string message, Exception innerException) : base(message, innerException) { }
}
