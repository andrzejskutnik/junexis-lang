using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunexisLang.Compiler.CLI.Output;

internal sealed record PackageReceipt(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("targets")] IReadOnlyList<string> Targets,
    [property: JsonPropertyName("gates")] IReadOnlyList<PackageGateReceipt> Gates)
{
    internal const string CurrentVersion = "junexis.package-receipt/0.1";
}

internal sealed record PackageGateReceipt(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("toolchain")] string Toolchain,
    [property: JsonPropertyName("status")] string Status);

internal static class PackageReceiptContract
{
    private const int MaximumReceiptBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<(string Path, PackageReceipt Receipt)> WriteAsync(
        SystemBundleInspection bundle,
        CancellationToken cancellationToken = default)
    {
        var targets = bundle.Descriptor.Targets
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();
        var receipt = new PackageReceipt(
            PackageReceipt.CurrentVersion,
            bundle.BundleId,
            bundle.Descriptor.BuildId,
            targets,
            targets.Select(target => new PackageGateReceipt(
                target,
                "native-build",
                Toolchain(target),
                "passed")).ToArray());
        Validate(receipt);
        var path = DefaultPath(bundle.Path);
        var json = JsonSerializer.Serialize(receipt, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumReceiptBytes)
            throw new InvalidDataException("Package receipt exceeds its 1 MiB safety limit.");
        await WriteAtomicAsync(path, json, cancellationToken).ConfigureAwait(false);
        return (path, receipt);
    }

    internal static async Task<PackageReceipt> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists)
            throw new FileNotFoundException("Package receipt was not found.", file.FullName);
        if (file.Length > MaximumReceiptBytes)
            throw new InvalidDataException("Package receipt exceeds its 1 MiB safety limit.");
        PackageReceipt? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<PackageReceipt>(
                await File.ReadAllTextAsync(file.FullName, cancellationToken).ConfigureAwait(false),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Package receipt contains invalid JSON.", ex);
        }
        if (receipt is null)
            throw new InvalidDataException("Package receipt is empty.");
        Validate(receipt);
        return receipt;
    }

    internal static string DefaultPath(string bundlePath) =>
        Path.GetFullPath(bundlePath) + ".package-receipt.json";

    internal static void ValidateForBundle(
        PackageReceipt receipt,
        SystemBundleInspection bundle)
    {
        Validate(receipt);
        if (!string.Equals(receipt.BundleId, bundle.BundleId, StringComparison.Ordinal)
            || !string.Equals(receipt.BuildId, bundle.Descriptor.BuildId, StringComparison.Ordinal)
            || !receipt.Targets.SequenceEqual(
                bundle.Descriptor.Targets.OrderBy(target => target, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Package receipt does not belong to the selected System Bundle.");
        }
    }

    private static void Validate(PackageReceipt receipt)
    {
        if (!string.Equals(
                receipt.SchemaVersion,
                PackageReceipt.CurrentVersion,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unsupported package receipt schema '{receipt.SchemaVersion}'.");
        ValidateHash(receipt.BundleId, "bundleId");
        ValidateHash(receipt.BuildId, "buildId");
        if (receipt.Targets is null
            || receipt.Targets.Count == 0
            || receipt.Targets.Count > 32
            || receipt.Targets.Any(target =>
                string.IsNullOrWhiteSpace(target) || target.Length > 64)
            || receipt.Targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != receipt.Targets.Count
            || !receipt.Targets.SequenceEqual(
                receipt.Targets.OrderBy(target => target, StringComparer.Ordinal),
                StringComparer.Ordinal))
            throw new InvalidDataException(
                "Package receipt targets must be non-empty, unique, and sorted.");
        if (receipt.Gates is null
            || receipt.Gates.Count != receipt.Targets.Count
            || receipt.Gates.Select(gate => gate.Target)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.Gates.Count)
            throw new InvalidDataException(
                "Package receipt must contain exactly one native gate per target.");
        foreach (var target in receipt.Targets)
        {
            var gate = receipt.Gates.SingleOrDefault(item =>
                item.Target.Equals(target, StringComparison.Ordinal));
            if (gate is null
                || gate.Kind != "native-build"
                || gate.Status != "passed"
                || gate.Toolchain != Toolchain(target))
                throw new InvalidDataException(
                    $"Package receipt has no valid passed native gate for '{target}'.");
        }
    }

    private static string Toolchain(string target) => target switch
    {
        "embedded" => "platformio",
        "server" => "go",
        "report" => "npm-typescript",
        _ => throw new InvalidDataException(
            $"Package receipt contains unsupported target '{target}'.")
    };

    private static void ValidateHash(string? value, string property)
    {
        if (value is null
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException(
                $"Package receipt {property} must be a 64-character SHA-256.");
    }

    private static async Task WriteAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("Package receipt path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
