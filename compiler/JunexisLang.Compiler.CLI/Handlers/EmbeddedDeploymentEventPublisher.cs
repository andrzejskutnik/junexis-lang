using System.Net.Http.Json;
using System.Text.Json;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class EmbeddedDeploymentEventPublisher
{
    private const string IngressUrlEnvironmentVariable = "JUNEXIS_OBSERVABILITY_INGRESS_URL";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static async Task TryPublishSelectionAsync(
        EmbeddedDeploymentHandler.EmbeddedDeviceSelection selection,
        string selectionPath,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveEndpoint();
        if (endpoint is null)
            return;

        var observedAt = ParseTimestamp(selection.SelectedAtUtc);
        var correlationId = $"embedded-select:{selection.Port}:{Guid.NewGuid():N}";

        var auditPayload = new
        {
            actor = selection.Tool,
            action = "deploy.embedded.select",
            target = selection.Port,
            outcome = "Allowed",
            reason = $"Selected embedded device {selection.Port} for verified upload.",
            evidenceRefs = new[] { selectionPath },
        };
        var healthPayload = new
        {
            scope = "deploy.embedded.device",
            subject = selection.Port,
            status = "Healthy",
            details = $"Device {selection.Port} selected by {selection.Tool}.",
            observedAt,
            freshFor = TimeSpan.FromMinutes(5),
        };

        await TryPostAsync(CreateEnvelope(
            kind: "Audit",
            producer: "junexis.cli.embedded",
            component: "embedded-deploy",
            observedAt,
            correlationId,
            buildId: null,
            bundleId: null,
            sessionId: null,
            deviceId: selection.Port,
            payloadType: "AuditRecord",
            payloadJson: JsonSerializer.Serialize(auditPayload, JsonOptions)), endpoint, cancellationToken).ConfigureAwait(false);

        await TryPostAsync(CreateEnvelope(
            kind: "Health",
            producer: "junexis.cli.embedded",
            component: "embedded-deploy",
            observedAt,
            correlationId,
            buildId: null,
            bundleId: null,
            sessionId: null,
            deviceId: selection.Port,
            payloadType: "HealthSignal",
            payloadJson: JsonSerializer.Serialize(healthPayload, JsonOptions)), endpoint, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task TryPublishFlashReceiptAsync(
        EmbeddedDeploymentHandler.EmbeddedFlashReceipt receipt,
        string receiptPath,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveEndpoint();
        if (endpoint is null)
            return;

        var observedAt = ParseTimestamp(receipt.FlashedAtUtc);
        var correlationId = $"embedded-flash:{receipt.Port}:{Guid.NewGuid():N}";
        var auditPayload = new
        {
            actor = receipt.Tool,
            action = "deploy.embedded.flash",
            target = receipt.Port,
            outcome = string.Equals(receipt.LifecycleStatus, "succeeded", StringComparison.OrdinalIgnoreCase) ? "Succeeded" : "Failed",
            reason = receipt.Summary,
            evidenceRefs = new[] { receiptPath, receipt.PackageReceiptPath, receipt.SelectionPath },
        };
        var healthPayload = new
        {
            scope = "deploy.embedded.device",
            subject = receipt.Port,
            status = string.Equals(receipt.LifecycleStatus, "succeeded", StringComparison.OrdinalIgnoreCase) ? "Healthy" : "Unavailable",
            details = receipt.Summary,
            observedAt,
            freshFor = TimeSpan.FromMinutes(10),
        };

        await TryPostAsync(CreateEnvelope(
            kind: "Audit",
            producer: "junexis.cli.embedded",
            component: "embedded-deploy",
            observedAt,
            correlationId,
            buildId: receipt.BuildId,
            bundleId: receipt.BundleId,
            sessionId: null,
            deviceId: receipt.Port,
            payloadType: "AuditRecord",
            payloadJson: JsonSerializer.Serialize(auditPayload, JsonOptions)), endpoint, cancellationToken).ConfigureAwait(false);

        await TryPostAsync(CreateEnvelope(
            kind: "Health",
            producer: "junexis.cli.embedded",
            component: "embedded-deploy",
            observedAt,
            correlationId,
            buildId: receipt.BuildId,
            bundleId: receipt.BundleId,
            sessionId: null,
            deviceId: receipt.Port,
            payloadType: "HealthSignal",
            payloadJson: JsonSerializer.Serialize(healthPayload, JsonOptions)), endpoint, cancellationToken).ConfigureAwait(false);
    }

    private static ObservabilityEnvelope CreateEnvelope(
        string kind,
        string producer,
        string component,
        DateTimeOffset observedAt,
        string correlationId,
        string? buildId,
        string? bundleId,
        string? sessionId,
        string? deviceId,
        string payloadType,
        string payloadJson)
        => new(
            Guid.NewGuid().ToString("N"),
            kind,
            producer,
            component,
            "Embedded",
            observedAt,
            default,
            correlationId,
            buildId,
            bundleId,
            sessionId,
            deviceId,
            "1.0",
            payloadType,
            payloadJson,
            new Dictionary<string, string>());

    private static async Task TryPostAsync(ObservabilityEnvelope envelope, Uri endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.PostAsJsonAsync(endpoint, envelope, JsonOptions, cancellationToken).ConfigureAwait(false);
            _ = response.IsSuccessStatusCode;
        }
        catch
        {
        }
    }

    private static Uri? ResolveEndpoint()
    {
        var raw = Environment.GetEnvironmentVariable(IngressUrlEnvironmentVariable);
        return Uri.TryCreate(raw, UriKind.Absolute, out var endpoint) ? endpoint : null;
    }

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private sealed record ObservabilityEnvelope(
        string EventId,
        string Kind,
        string Producer,
        string Component,
        string PlatformRole,
        DateTimeOffset OccurredAt,
        DateTimeOffset ReceivedAt,
        string CorrelationId,
        string? BuildId,
        string? BundleId,
        string? SessionId,
        string? DeviceId,
        string SchemaVersion,
        string PayloadType,
        string PayloadJson,
        IReadOnlyDictionary<string, string> Tags);
}