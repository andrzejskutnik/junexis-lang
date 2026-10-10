using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class RestoreHandler
{
    internal static async Task<int> HandleAsync(string? inputPath, bool offline, string? registry = null)
    {
        try
        {
            if (offline && !string.IsNullOrWhiteSpace(registry))
                throw new InvalidDataException("Options '--offline' and '--registry' cannot be used together.");
            var startDirectory = string.IsNullOrWhiteSpace(inputPath)
                ? new DirectoryInfo(Environment.CurrentDirectory)
                : new DirectoryInfo(Path.GetFullPath(inputPath));
            var project = await JunexisProjectManifest.DiscoverAsync(startDirectory)
                ?? throw new InvalidDataException(
                    $"Project manifest '{JunexisProjectManifest.FileName}' not found from directory: {startDirectory.FullName}");
            var snapshotUri = ResolveRegistrySnapshotUri(registry);
            var correlationId = Guid.NewGuid().ToString("D");
            using var networkClient = snapshotUri is null
                ? null
                : new HttpNetworkPackageSourceClient(correlationId: correlationId);
            var signatureVerifier = snapshotUri is null
                ? null
                : RegistrySnapshotSignatureVerifier.FromEnvironment(
                    PackageRestoreResolver.GetPackagesRoot(project));
            var packageSource = snapshotUri is null
                ? null
                : new RegistrySnapshotPackageSource(
                    snapshotUri,
                    networkClient!,
                    new CompilerRegistryEventEmitter(new TraceCompilerRegistryEventSink()),
                    correlationId,
                    signatureVerifier);
            var packages = await PackageRestoreResolver.RestoreAsync(
                project,
                offline,
                packageSource: packageSource);
            await Console.Out.WriteLineAsync(
                $"Restored {packages.Count} package(s){(offline ? " from cache" : string.Empty)}.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
    }

    internal static Uri? ResolveRegistrySnapshotUri(string? registry)
    {
        if (string.IsNullOrWhiteSpace(registry))
            return null;
        if (!Uri.TryCreate(registry, UriKind.Absolute, out var baseAddress) ||
            baseAddress.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseAddress.UserInfo) ||
            !string.IsNullOrEmpty(baseAddress.Query) ||
            !string.IsNullOrEmpty(baseAddress.Fragment))
            throw new InvalidDataException("Registry must be an absolute HTTPS base URL without user information, query or fragment.");
        return new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + "/v1/snapshot", UriKind.Absolute);
    }
}
