namespace JunexisLang.Compiler.CLI.Project;

internal interface IPackageSource
{
    Task<PackageIndexDocument> LoadIndexAsync(CancellationToken cancellationToken = default);

    Task CopyArtifactAsync(
        PackageIndexEntry entry,
        Stream destination,
        CancellationToken cancellationToken = default);
}

internal sealed class LocalPackageSource : IPackageSource
{
    private readonly string indexPath;
    private readonly string indexRoot;

    internal LocalPackageSource(string indexPath)
    {
        this.indexPath = Path.GetFullPath(indexPath);
        indexRoot = Path.GetDirectoryName(this.indexPath)!;
    }

    public async Task<PackageIndexDocument> LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(indexPath))
            throw new InvalidDataException($"Package index not found: {indexPath}");

        return PackageJson.Deserialize<PackageIndexDocument>(
            await PackageJson.ReadTextAsync(indexPath, cancellationToken),
            indexPath);
    }

    public async Task CopyArtifactAsync(
        PackageIndexEntry entry,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var artifactPath = PackageRestoreResolver.ResolveContainedFile(
            indexRoot,
            entry.Artifact,
            "Package artifact");
        if (!File.Exists(artifactPath))
            throw new InvalidDataException($"Package artifact not found: {entry.Artifact}");
        if (new FileInfo(artifactPath).Length > PackageContractValidator.MaximumPackageSizeBytes)
            throw new InvalidDataException($"Package artifact exceeds the size limit: {entry.Id}@{entry.Version}");

        await using var source = File.OpenRead(artifactPath);
        await source.CopyToAsync(destination, cancellationToken);
    }
}

internal interface INetworkPackageSourceClient
{
    Task<string> GetIndexAsync(Uri indexUri, CancellationToken cancellationToken);

    Task CopyArtifactAsync(Uri artifactUri, Stream destination, CancellationToken cancellationToken);
}

internal sealed class NetworkPackageSource : IPackageSource
{
    private readonly Uri indexUri;
    private readonly INetworkPackageSourceClient client;

    internal NetworkPackageSource(Uri indexUri, INetworkPackageSourceClient client)
    {
        if (!indexUri.IsAbsoluteUri)
            throw new ArgumentException("Network package index URI must be absolute.", nameof(indexUri));

        this.indexUri = indexUri;
        this.client = client;
    }

    public async Task<PackageIndexDocument> LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        var json = await client.GetIndexAsync(indexUri, cancellationToken);
        return PackageJson.Deserialize<PackageIndexDocument>(json, indexUri.ToString());
    }

    public Task CopyArtifactAsync(
        PackageIndexEntry entry,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        return client.CopyArtifactAsync(new Uri(indexUri, entry.Artifact), destination, cancellationToken);
    }
}
