using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class PackageRestoreTests
{
    [Fact]
    public async Task RestoreAsync_UsesLocalIndexThenRebuildsOfflineWithIdenticalLockfile()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);

            var restored = await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var firstLock = await File.ReadAllBytesAsync(Path.Combine(root, PackagesLockFile.FileName));
            var resolvedInput = await CliProjectInputResolver.ResolveAsync(root);

            var package = Assert.Single(restored);
            Assert.Equal("junexis.observation", package.Id);
            Assert.Contains(resolvedInput.ModuleCatalog!.Modules, module =>
                module.Identity.Value == "junexis::observation::health");

            Directory.Delete(Path.Combine(root, "registry"), recursive: true);

            var offline = await PackageRestoreResolver.RestoreAsync(project, offline: true);
            var secondLock = await File.ReadAllBytesAsync(Path.Combine(root, PackagesLockFile.FileName));

            Assert.Single(offline);
            Assert.Equal(firstLock, secondLock);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_LocalAndNetworkSourcesProduceIdenticalLockfile()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var indexPath = Path.Combine(root, "registry", "index.json");
            var localSource = new LocalPackageSource(indexPath);

            await PackageRestoreResolver.RestoreAsync(project, offline: false, packageSource: localSource);
            var localLock = await File.ReadAllBytesAsync(Path.Combine(root, PackagesLockFile.FileName));

            Directory.Delete(Path.Combine(root, ".junexis"), recursive: true);
            File.Delete(Path.Combine(root, PackagesLockFile.FileName));
            var networkSource = new NetworkPackageSource(
                new Uri("https://registry.example.test/index.json"),
                new TestNetworkPackageSourceClient(indexPath));

            await PackageRestoreResolver.RestoreAsync(project, offline: false, packageSource: networkSource);
            var networkLock = await File.ReadAllBytesAsync(Path.Combine(root, PackagesLockFile.FileName));

            Assert.Equal(localLock, networkLock);
            Assert.False(Directory.Exists(Path.Combine(root, ".junexis", "cache", "packages", ".source")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_LocalIndexAndRegistrySnapshotProduceIdenticalLockfile()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var localLock = await File.ReadAllBytesAsync(Path.Combine(root, PackagesLockFile.FileName));

            Directory.Delete(Path.Combine(root, ".junexis"), recursive: true);
            File.Delete(Path.Combine(root, PackagesLockFile.FileName));
            var registryRoot = Path.Combine(root, "registry");
            var index = PackageJson.Deserialize<PackageIndexDocument>(
                await File.ReadAllTextAsync(Path.Combine(registryRoot, "index.json")),
                "test index");
            var entry = Assert.Single(index.Packages);
            var artifactPath = Path.Combine(registryRoot, entry.Artifact.Replace('/', Path.DirectorySeparatorChar));
            var artifacts = JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    artifactType = "package",
                    id = entry.Id,
                    version = entry.Version,
                    @namespace = entry.Namespace,
                    artifactPath = entry.Artifact,
                    mediaType = "application/zip",
                    sizeBytes = new FileInfo(artifactPath).Length,
                    sha256 = entry.ArtifactSha256,
                    manifestSha256 = entry.ManifestSha256,
                    dependencies = Array.Empty<PackageDependency>(),
                    compatibility = new
                    {
                        language = new { minimum = "0.2.0-experimental", maximumExclusive = "0.3.0" },
                        languageTargets = new[] { "server" }
                    },
                    source = $"registry:{entry.Id}@{entry.Version}"
                }
            }, PackageJson.Options);
            var snapshotJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "junexis.registry-snapshot/0.1",
                snapshotId = RegistrySnapshotValidator.ComputeSnapshotId(artifacts),
                artifacts
            }, PackageJson.Options);
            var client = new TestRegistrySnapshotClient(snapshotJson, artifactPath);
            var eventSink = new CollectingCompilerRegistryEventSink();
            var correlationId = Guid.NewGuid().ToString("D");
            var source = new RegistrySnapshotPackageSource(
                new Uri("https://registry.example.test/base/v1/snapshot"),
                client,
                new CompilerRegistryEventEmitter(eventSink),
                correlationId);

            await PackageRestoreResolver.RestoreAsync(project, offline: false, packageSource: source);
            var registryLock = await File.ReadAllBytesAsync(Path.Combine(root, PackagesLockFile.FileName));

            Assert.Equal(localLock, registryLock);
            Assert.Equal(
                "https://registry.example.test/base/v1/artifacts/package/junexis.observation/0.1.0-experimental/content",
                client.ArtifactUri?.ToString());
            Assert.Equal(4, eventSink.Values.Count);
            Assert.All(eventSink.Values, value => Assert.Equal(correlationId, value.CorrelationId));
            Assert.Equal(
                ["snapshot-read:started", "snapshot-read:completed", "artifact-download:started", "artifact-download:completed"],
                eventSink.Values.Select(value => $"{value.Operation}:{value.Phase}").ToArray());
            Assert.All(
                eventSink.Values.Where(value => value.Phase == "completed"),
                value => Assert.Equal("succeeded", value.Outcome));
            Assert.DoesNotContain(
                eventSink.Values.Select(value => JsonSerializer.Serialize(value)),
                serialized => serialized.Contains(root, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RegistrySnapshotPackageSource_RejectsMismatchedSnapshotId()
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.registry-snapshot/0.1",
            snapshotId = new string('0', 64),
            artifacts = Array.Empty<object>()
        }, PackageJson.Options);
        var source = new RegistrySnapshotPackageSource(
            new Uri("https://registry.example.test/v1/snapshot"),
            new TestRegistrySnapshotClient(snapshotJson, "unused"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => source.LoadIndexAsync());

        Assert.Contains("snapshot ID", error.Message);
    }

    [Fact]
    public async Task RegistrySnapshotPackageSource_IgnoresProviderArtifacts()
    {
        var artifacts = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                artifactType = "provider",
                id = "junexis.storage.postgresql",
                version = "1.0.0",
                artifactPath = "providers/postgresql.zip",
                mediaType = "application/zip",
                sizeBytes = 1,
                sha256 = new string('1', 64),
                manifestSha256 = new string('2', 64),
                compatibility = new
                {
                    platformRid = "win-x64",
                    supportedPlatformRids = new[] { "win-x64" },
                    sdkVersion = "1.0.0",
                    abiVersion = "storage/1.0",
                    targetFramework = "net10.0",
                    platformVersion = "1.0.0",
                    compatibilitySet = "postgresql-win-x64"
                },
                contract = "storage/1.0"
            }
        }, PackageJson.Options);
        var source = new RegistrySnapshotPackageSource(
            new Uri("https://registry.example.test/v1/snapshot"),
            new TestRegistrySnapshotClient(CreateSnapshotJson(artifacts), "unused"));

        var index = await source.LoadIndexAsync();

        Assert.Empty(index.Packages);
    }

    [Fact]
    public void RegistrySnapshotValidator_NormalizesIntegralDecimalForSnapshotId()
    {
        using var integer = JsonDocument.Parse("[{\"sizeBytes\":1}]");
        using var integralDecimal = JsonDocument.Parse("[{\"sizeBytes\":1.0}]");

        Assert.Equal(
            RegistrySnapshotValidator.ComputeSnapshotId(integer.RootElement),
            RegistrySnapshotValidator.ComputeSnapshotId(integralDecimal.RootElement));
    }

    [Fact]
    public async Task RegistrySnapshotPackageSource_RejectsInvalidProviderCompatibility()
    {
        var artifacts = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                artifactType = "provider",
                id = "junexis.storage.postgresql",
                version = "1.0.0",
                artifactPath = "providers/postgresql.zip",
                mediaType = "application/zip",
                sizeBytes = 1,
                sha256 = new string('1', 64),
                manifestSha256 = new string('2', 64),
                compatibility = new
                {
                    platformRid = "linux-x64",
                    supportedPlatformRids = new[] { "win-x64" },
                    sdkVersion = "1.0.0",
                    abiVersion = "storage/1.0",
                    targetFramework = "net10.0",
                    platformVersion = "1.0.0",
                    compatibilitySet = "postgresql-win-x64"
                },
                contract = "storage/1.0"
            }
        }, PackageJson.Options);
        var source = new RegistrySnapshotPackageSource(
            new Uri("https://registry.example.test/v1/snapshot"),
            new TestRegistrySnapshotClient(CreateSnapshotJson(artifacts), "unused"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => source.LoadIndexAsync());

        Assert.Contains("provider compatibility", error.Message);
    }

    [Fact]
    public async Task RegistrySnapshotPackageSource_RejectsNullNestedCompatibilityField()
    {
        var artifacts = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                artifactType = "package",
                id = "junexis.observation",
                version = "1.0.0",
                @namespace = "junexis::observation",
                artifactPath = "packages/observation.zip",
                mediaType = "application/zip",
                sizeBytes = 1,
                sha256 = new string('1', 64),
                manifestSha256 = new string('2', 64),
                dependencies = Array.Empty<PackageDependency>(),
                compatibility = new
                {
                    language = new { minimum = (string?)null, maximumExclusive = "2.0.0" },
                    languageTargets = new[] { "server" }
                },
                source = "registry:junexis.observation@1.0.0"
            }
        }, PackageJson.Options);
        var source = new RegistrySnapshotPackageSource(
            new Uri("https://registry.example.test/v1/snapshot"),
            new TestRegistrySnapshotClient(CreateSnapshotJson(artifacts), "unused"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => source.LoadIndexAsync());

        Assert.Contains("missing a required field", error.Message);
    }

    [Fact]
    public async Task RestoreAsync_RejectsArtifactSizeDifferentFromRegistrySnapshot()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var registryRoot = Path.Combine(root, "registry");
            var index = PackageJson.Deserialize<PackageIndexDocument>(
                await File.ReadAllTextAsync(Path.Combine(registryRoot, "index.json")),
                "test index");
            var entry = Assert.Single(index.Packages);
            var artifactPath = Path.Combine(registryRoot, entry.Artifact.Replace('/', Path.DirectorySeparatorChar));
            var actualLength = new FileInfo(artifactPath).Length;
            var artifacts = CreateSnapshotArtifacts(entry, artifactPath, [], actualLength - 1);
            var source = new RegistrySnapshotPackageSource(
                new Uri("https://registry.example.test/v1/snapshot"),
                new TestRegistrySnapshotClient(CreateSnapshotJson(artifacts), artifactPath));

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => PackageRestoreResolver.RestoreAsync(
                project,
                offline: false,
                packageSource: source));

            Assert.Contains("size differs", error.Message);
            Assert.False(File.Exists(Path.Combine(root, PackagesLockFile.FileName)));
            Assert.False(Directory.Exists(Path.Combine(root, ".junexis", "cache", "packages")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_RejectsRegistryDependenciesDifferentFromPackageManifest()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var registryRoot = Path.Combine(root, "registry");
            var index = PackageJson.Deserialize<PackageIndexDocument>(
                await File.ReadAllTextAsync(Path.Combine(registryRoot, "index.json")),
                "test index");
            var entry = Assert.Single(index.Packages);
            var artifactPath = Path.Combine(registryRoot, entry.Artifact.Replace('/', Path.DirectorySeparatorChar));
            var artifacts = CreateSnapshotArtifacts(entry, artifactPath,
                [new PackageDependency("junexis.other", "1.0.0")]);
            var source = new RegistrySnapshotPackageSource(
                new Uri("https://registry.example.test/v1/snapshot"),
                new TestRegistrySnapshotClient(CreateSnapshotJson(artifacts), artifactPath));

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => PackageRestoreResolver.RestoreAsync(
                project,
                offline: false,
                packageSource: source));

            Assert.Contains("dependencies differ", error.Message);
            Assert.False(File.Exists(Path.Combine(root, PackagesLockFile.FileName)));
            Assert.False(Directory.Exists(Path.Combine(root, ".junexis", "cache", "packages")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SizeLimitedWriteStream_RejectsBytesBeyondLimitBeforeWritingThem()
    {
        await using var destination = new MemoryStream();
        await using var limited = new SizeLimitedWriteStream(destination, maximumLength: 3);

        await limited.WriteAsync(new byte[] { 1, 2, 3 });
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await limited.WriteAsync(new byte[] { 4 }));

        Assert.Contains("size limit", error.Message);
        Assert.Equal(3, destination.Length);
    }

    [Fact]
    public async Task RestoreAsync_NetworkCancellationDoesNotMutateCacheOrLockfile()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var indexPath = Path.Combine(root, "registry", "index.json");
            using var cancellation = new CancellationTokenSource();
            var source = new NetworkPackageSource(
                new Uri("https://registry.example.test/index.json"),
                new TestNetworkPackageSourceClient(indexPath, cancellation));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageRestoreResolver.RestoreAsync(
                project,
                offline: false,
                packageSource: source,
                cancellationToken: cancellation.Token));

            Assert.False(File.Exists(Path.Combine(root, PackagesLockFile.FileName)));
            Assert.False(Directory.Exists(Path.Combine(root, ".junexis", "cache", "packages")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_PartialHttpTransferDoesNotMutateCacheOrLockfile()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var indexJson = await File.ReadAllTextAsync(Path.Combine(root, "registry", "index.json"));
            using var client = new HttpNetworkPackageSourceClient(new PartialArtifactHandler(indexJson));
            var source = new NetworkPackageSource(
                new Uri("https://registry.example.test/index.json"),
                client);

            await Assert.ThrowsAnyAsync<IOException>(() => PackageRestoreResolver.RestoreAsync(
                project,
                offline: false,
                packageSource: source));

            Assert.False(File.Exists(Path.Combine(root, PackagesLockFile.FileName)));
            Assert.False(Directory.Exists(Path.Combine(root, ".junexis", "cache", "packages")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(RemoteFailure.Timeout)]
    [InlineData(RemoteFailure.ConnectionLost)]
    [InlineData(RemoteFailure.Canceled)]
    public async Task RestoreAsync_RemoteFailurePreservesLastSuccessfulCacheAndLockfile(
        RemoteFailure failure)
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var before = await CapturePackageStateAsync(root);
            var indexPath = Path.Combine(root, "registry", "index.json");
            var indexJson = await File.ReadAllTextAsync(indexPath);
            using var cancellation = new CancellationTokenSource();
            using var client = new HttpNetworkPackageSourceClient(
                new RemoteFailureHandler(indexJson, failure, cancellation),
                failure == RemoteFailure.Timeout ? TimeSpan.FromMilliseconds(20) : null,
                (_, token) => token.IsCancellationRequested
                    ? Task.FromCanceled(token)
                    : Task.CompletedTask);
            var source = new NetworkPackageSource(
                new Uri("https://registry.example.test/index.json"),
                client);

            var error = await Record.ExceptionAsync(() => PackageRestoreResolver.RestoreAsync(
                project,
                offline: false,
                packageSource: source,
                cancellationToken: cancellation.Token));

            Assert.NotNull(error);
            if (failure == RemoteFailure.Timeout)
                Assert.IsType<TimeoutException>(error);
            else if (failure == RemoteFailure.ConnectionLost)
                Assert.IsAssignableFrom<IOException>(error);
            else
                Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.Equal(before, await CapturePackageStateAsync(root));
            AssertNoTransactionResidue(root);
            Assert.Single(await PackageRestoreResolver.RestoreAsync(project, offline: true));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RegistryRestore_PersistsSnapshotThenRestoreCheckAndBuildStayOffline()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var registryRoot = Path.Combine(root, "registry");
            var index = PackageJson.Deserialize<PackageIndexDocument>(
                await File.ReadAllTextAsync(Path.Combine(registryRoot, "index.json")),
                "test index");
            var entry = Assert.Single(index.Packages);
            var artifactPath = Path.Combine(registryRoot, entry.Artifact.Replace('/', Path.DirectorySeparatorChar));
            var source = new RegistrySnapshotPackageSource(
                new Uri("https://registry.example.test/v1/snapshot"),
                new TestRegistrySnapshotClient(
                    CreateSnapshotJson(CreateSnapshotArtifacts(entry, artifactPath, [])),
                    artifactPath));

            await PackageRestoreResolver.RestoreAsync(project, offline: false, packageSource: source);
            var packagesRoot = PackageRestoreResolver.GetPackagesRoot(project);
            Assert.True(File.Exists(Path.Combine(packagesRoot, RegistryOfflineSnapshot.RegistryFileName)));
            Assert.True(File.Exists(Path.Combine(packagesRoot, RegistryOfflineSnapshot.OfflineFileName)));
            var lockBefore = await File.ReadAllBytesAsync(Path.Combine(root, PackagesLockFile.FileName));
            Directory.Delete(registryRoot, recursive: true);

            var blockedNetwork = new ThrowingNetworkPackageSourceClient();
            var blockedSource = new RegistrySnapshotPackageSource(
                new Uri("https://unresolvable.invalid/v1/snapshot"),
                blockedNetwork);
            Assert.Single(await PackageRestoreResolver.RestoreAsync(
                project,
                offline: true,
                packageSource: blockedSource));
            Assert.Equal(0, blockedNetwork.CallCount);
            Assert.Equal(0, await CheckHandler.HandleAsync(root, json: false));
            Assert.Equal(0, await CompileHandler.HandleAsync(root, null, null, false, "1.0", null));
            Assert.Equal(lockBefore, await File.ReadAllBytesAsync(Path.Combine(root, PackagesLockFile.FileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OfflineRestore_RejectsCacheReceiptDifferentFromRegistrySnapshotWithoutUsingSource()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var registryRoot = Path.Combine(root, "registry");
            var index = PackageJson.Deserialize<PackageIndexDocument>(
                await File.ReadAllTextAsync(Path.Combine(registryRoot, "index.json")),
                "test index");
            var entry = Assert.Single(index.Packages);
            var artifactPath = Path.Combine(registryRoot, entry.Artifact.Replace('/', Path.DirectorySeparatorChar));
            var source = new RegistrySnapshotPackageSource(
                new Uri("https://registry.example.test/v1/snapshot"),
                new TestRegistrySnapshotClient(
                    CreateSnapshotJson(CreateSnapshotArtifacts(entry, artifactPath, [])),
                    artifactPath));
            await PackageRestoreResolver.RestoreAsync(project, offline: false, packageSource: source);

            var offlinePath = Path.Combine(
                PackageRestoreResolver.GetPackagesRoot(project),
                RegistryOfflineSnapshot.OfflineFileName);
            var offline = PackageJson.Deserialize<RegistryOfflineSnapshotDocument>(
                await File.ReadAllTextAsync(offlinePath),
                offlinePath);
            offline.CachedArtifacts[0] = offline.CachedArtifacts[0] with
            {
                SizeBytes = offline.CachedArtifacts[0].SizeBytes + 1
            };
            await File.WriteAllTextAsync(offlinePath, JsonSerializer.Serialize(offline, PackageJson.Options));
            var blockedNetwork = new ThrowingNetworkPackageSourceClient();
            var blockedSource = new RegistrySnapshotPackageSource(
                new Uri("https://unresolvable.invalid/v1/snapshot"),
                blockedNetwork);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                PackageRestoreResolver.RestoreAsync(
                    project,
                    offline: true,
                    packageSource: blockedSource));

            Assert.Contains("Lokalny cache jest niezgodny", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, blockedNetwork.CallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(RemoteHttpFailure.NotFound, "artifact-not-found")]
    [InlineData(RemoteHttpFailure.RateLimited, "rate-limited")]
    [InlineData(RemoteHttpFailure.ServerUnavailable, "registry-unavailable")]
    [InlineData(RemoteHttpFailure.Oversized, "size limit")]
    public async Task RestoreAsync_HttpFailurePreservesLastSuccessfulCacheAndLockfile(
        RemoteHttpFailure failure,
        string diagnostic)
    {
        var root = CreateTempDirectory();
        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var before = await CapturePackageStateAsync(root);
            var indexJson = await File.ReadAllTextAsync(Path.Combine(root, "registry", "index.json"));
            var handler = new RemoteHttpFailureHandler(indexJson, failure);
            using var client = new HttpNetworkPackageSourceClient(
                handler,
                delayAsync: (_, token) => token.IsCancellationRequested
                    ? Task.FromCanceled(token)
                    : Task.CompletedTask);
            var source = new NetworkPackageSource(
                new Uri("https://registry.example.test/index.json"), client);

            var error = await Record.ExceptionAsync(() => PackageRestoreResolver.RestoreAsync(
                project, offline: false, packageSource: source));

            Assert.NotNull(error);
            Assert.Contains(diagnostic, error.Message);
            Assert.Equal(failure is RemoteHttpFailure.RateLimited or RemoteHttpFailure.ServerUnavailable ? 5 : 2,
                handler.RequestCount);
            Assert.Equal(before, await CapturePackageStateAsync(root));
            AssertNoTransactionResidue(root);
            Assert.Single(await PackageRestoreResolver.RestoreAsync(project, offline: true));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreProcesses_PreserveLastSuccessfulCacheAndLockfile()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var before = await CapturePackageStateAsync(root);
            var acquiredPath = Path.Combine(root, "restore-acquired.signal");
            var releasePath = Path.Combine(root, "restore-release.signal");
            var first = RunGatedRestoreProcessAsync(root, acquiredPath, releasePath);
            (int ExitCode, string StandardOutput, string StandardError) firstResult;
            try
            {
                await WaitForFileAsync(acquiredPath);
                var second = await RunRestoreProcessAsync(root);
                Assert.Equal(1, second.ExitCode);
                Assert.Contains("already using this project cache", second.StandardError);
            }
            finally
            {
                await File.WriteAllTextAsync(releasePath, string.Empty);
                firstResult = await first;
            }
            Assert.Equal(0, firstResult.ExitCode);

            Assert.Equal(before, await CapturePackageStateAsync(root));
            AssertNoTransactionResidue(root);
            Assert.Single(await PackageRestoreResolver.RestoreAsync(project, offline: true));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_CrossProcessGateHelper()
    {
        var root = Environment.GetEnvironmentVariable("JUNEXIS_TEST_RESTORE_ROOT");
        var acquiredPath = Environment.GetEnvironmentVariable("JUNEXIS_TEST_RESTORE_ACQUIRED");
        var releasePath = Environment.GetEnvironmentVariable("JUNEXIS_TEST_RESTORE_RELEASE");
        if (root is null || acquiredPath is null || releasePath is null)
            return;

        var project = (await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(root)))!;
        var localSource = new LocalPackageSource(Path.Combine(root, "registry", "index.json"));
        var source = new GatedPackageSource(localSource, acquiredPath, releasePath);
        Assert.Single(await PackageRestoreResolver.RestoreAsync(
            project,
            offline: false,
            packageSource: source));
    }

    [Fact]
    public async Task RestoreAsync_RejectsArtifactWithMismatchedHash()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var artifact = Directory.GetFiles(Path.Combine(root, "registry", "artifacts"), "*.zip").Single();
            await File.AppendAllTextAsync(artifact, "tampered");

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => PackageRestoreResolver.RestoreAsync(project, offline: false));

            Assert.Contains("artifact SHA-256 mismatch", error.Message);
            Assert.False(File.Exists(Path.Combine(root, PackagesLockFile.FileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_OfflineRejectsMissingCache()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var restored = await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var package = Assert.Single(restored);
            Directory.Delete(PackageRestoreResolver.GetCacheRoot(project, package.Id, package.Version), recursive: true);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => PackageRestoreResolver.RestoreAsync(project, offline: true));

            Assert.Contains("Cached package artifact not found", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_RejectsArtifactPathTraversal()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var indexPath = Path.Combine(root, "registry", "index.json");
            var index = await File.ReadAllTextAsync(indexPath);
            await File.WriteAllTextAsync(
                indexPath,
                index.Replace(
                    "artifacts/junexis.observation-0.1.0-experimental.zip",
                    "../outside.zip",
                    StringComparison.Ordinal));

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => PackageRestoreResolver.RestoreAsync(project, offline: false));

            Assert.Contains("Invalid package artifact path", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectDiscovery_RejectsUnexpectedCachedModule()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var restored = await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var package = Assert.Single(restored);
            var unexpected = Path.Combine(
                PackageRestoreResolver.GetModulesRoot(project, package.Id, package.Version),
                "junexis", "observation", "unexpected.jnx");
            Directory.CreateDirectory(Path.GetDirectoryName(unexpected)!);
            await File.WriteAllTextAsync(unexpected, "struct Unexpected {}");

            var error = await Assert.ThrowsAsync<CliInputException>(
                () => CliProjectInputResolver.ResolveAsync(root));

            Assert.Contains("module view has unexpected files", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_OfflineRejectsLockEntryOutsideDependencyGraph()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var restored = await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var package = Assert.Single(restored);
            await PackagesLockFile.WriteAsync(
                project.RootDirectory,
                [package, package with { Id = "junexis.unused", Source = "registry:junexis.unused@0.1.0-experimental" }]);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => PackageRestoreResolver.RestoreAsync(project, offline: true));

            Assert.Contains("outside the requested dependency graph", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectDiscovery_RejectsExplicitTargetUnsupportedByPackage()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            await PackageRestoreResolver.RestoreAsync(project, offline: false);

            var error = await Assert.ThrowsAsync<CliInputException>(
                () => CliProjectInputResolver.ResolveAsync(root, explicitTargets: ["report"]));

            Assert.Contains("does not support project target(s): report", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_RejectsUnknownIndexProperty()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var indexPath = Path.Combine(root, "registry", "index.json");
            var index = await File.ReadAllTextAsync(indexPath);
            await File.WriteAllTextAsync(
                indexPath,
                index.Replace(
                    "\"schemaVersion\"",
                    "\"unexpected\": true, \"schemaVersion\"",
                    StringComparison.Ordinal));

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => PackageRestoreResolver.RestoreAsync(project, offline: false));

            Assert.Contains("could not be mapped", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageContract_RejectsCapabilityOutsidePublicSchemaPattern()
    {
        var manifest = new ApplicationPackageManifest(
            "junexis.package/0.1",
            "example.package",
            "Example",
            "0.1.0",
            new PackageLanguageRange("0.1.0", "1.0.0"),
            ["server"],
            ["Invalid Capability"],
            [new PackageExport("Example", "src/main.jnx")],
            [],
            [new PackageFile("src/main.jnx", 1, new string('a', 64))]);

        var error = Assert.Throws<InvalidDataException>(() => PackageContractValidator.Validate(manifest));

        Assert.Contains("invalid capability", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageContract_RejectsExportPathOutsidePublicSourcePathPattern()
    {
        var manifest = new ApplicationPackageManifest(
            "junexis.package/0.1",
            "example.package",
            "Example",
            "0.1.0",
            new PackageLanguageRange("0.1.0", "1.0.0"),
            ["server"],
            [],
            [new PackageExport("Example", "src/main.file.jnx")],
            [],
            [new PackageFile("src/main.file.jnx", 1, new string('a', 64))]);

        var error = Assert.Throws<InvalidDataException>(() => PackageContractValidator.Validate(manifest));

        Assert.Contains("not a declared .jnx file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreAsync_RejectsConcurrentProjectCacheUse()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var cache = Path.Combine(root, ".junexis", "cache");
            Directory.CreateDirectory(cache);
            await using var transactionLock = new FileStream(
                Path.Combine(cache, "packages.transaction.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => PackageRestoreResolver.RestoreAsync(project, offline: false));

            Assert.Contains("already using this project cache", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_RejectsNullRequiredIndexField()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            var indexPath = Path.Combine(root, "registry", "index.json");
            var index = await File.ReadAllTextAsync(indexPath);
            await File.WriteAllTextAsync(
                indexPath,
                index.Replace(
                    "\"schemaVersion\": \"junexis.package-index/0.1\"",
                    "\"schemaVersion\": null",
                    StringComparison.Ordinal));

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => PackageRestoreResolver.RestoreAsync(project, offline: false));

            Assert.Contains("missing 'schemaVersion'", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_RecoversPreparedTransactionBeforeCacheMove()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var paths = GetTransactionPaths(root);
            var originalLock = await File.ReadAllBytesAsync(paths.Lock);
            File.Copy(paths.Lock, paths.LockBackup);
            await WriteTransactionJournalAsync(paths.Journal, "prepared", hadCache: true, hadLock: true);

            var restored = await PackageRestoreResolver.RestoreAsync(project, offline: true);

            Assert.Single(restored);
            Assert.True(Directory.Exists(paths.Packages));
            Assert.Equal(originalLock, await File.ReadAllBytesAsync(paths.Lock));
            Assert.False(File.Exists(paths.Journal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_RollsBackPreparedTransactionAfterCachePromotion()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var paths = GetTransactionPaths(root);
            var originalLock = await File.ReadAllBytesAsync(paths.Lock);
            Directory.Move(paths.Packages, paths.PackagesBackup);
            Directory.CreateDirectory(paths.Packages);
            await File.WriteAllTextAsync(Path.Combine(paths.Packages, "uncommitted.txt"), "new cache");
            File.Copy(paths.Lock, paths.LockBackup);
            await File.WriteAllTextAsync(paths.Lock, "invalid lockfile");
            await WriteTransactionJournalAsync(paths.Journal, "prepared", hadCache: true, hadLock: true);

            var restored = await PackageRestoreResolver.RestoreAsync(project, offline: true);

            Assert.Single(restored);
            Assert.False(File.Exists(Path.Combine(paths.Packages, "uncommitted.txt")));
            Assert.Equal(originalLock, await File.ReadAllBytesAsync(paths.Lock));
            Assert.False(Directory.Exists(paths.PackagesBackup));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_CompletesCleanupForCommittedTransaction()
    {
        var root = CreateTempDirectory();

        try
        {
            var project = await CreateProjectAndIndexAsync(root);
            await PackageRestoreResolver.RestoreAsync(project, offline: false);
            var paths = GetTransactionPaths(root);
            Directory.CreateDirectory(paths.PackagesBackup);
            await File.WriteAllTextAsync(Path.Combine(paths.PackagesBackup, "old.txt"), "old cache");
            File.Copy(paths.Lock, paths.LockBackup);
            await WriteTransactionJournalAsync(paths.Journal, "committed", hadCache: true, hadLock: true);

            var restored = await PackageRestoreResolver.RestoreAsync(project, offline: true);

            Assert.Single(restored);
            Assert.False(Directory.Exists(paths.PackagesBackup));
            Assert.False(File.Exists(paths.LockBackup));
            Assert.False(File.Exists(paths.Journal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WriteTransactionJournalAsync(
        string path,
        string phase,
        bool hadCache,
        bool hadLock) =>
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new { phase, hadCache, hadLock }, PackageJson.Options));

    private static (string Packages, string PackagesBackup, string Lock, string LockBackup, string Journal)
        GetTransactionPaths(string root)
    {
        var cache = Path.Combine(root, ".junexis", "cache");
        return (
            Path.Combine(cache, "packages"),
            Path.Combine(cache, "packages.backup"),
            Path.Combine(root, PackagesLockFile.FileName),
            Path.Combine(cache, "packages.lock.backup"),
            Path.Combine(cache, "packages.transaction.json"));
    }

    private static async Task<JunexisProject> CreateProjectAndIndexAsync(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            """
            import junexis::observation::health { HealthObservation }

            task HealthMonitor() @server {
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, JunexisProjectManifest.FileName),
            """
            name = "package-restore-test"
            entry = "src/main.jnx"
            packages = ["junexis.observation@0.1.0-experimental"]
            packageIndex = "registry/index.json"
            targets = ["server"]
            outDir = "out"
            """);

        var packageSource = Encoding.UTF8.GetBytes(
            """
            struct HealthObservation {
                message: Text
            }
            """);
        var sourceHash = ComputeSha256(packageSource);
        var manifest = new ApplicationPackageManifest(
            "junexis.package/0.1",
            "junexis.observation",
            "junexis::observation",
            "0.1.0-experimental",
            new PackageLanguageRange("0.2.0-experimental", "0.3.0"),
            ["server"],
            ["observation.health"],
            [new PackageExport("junexis::observation::health", "src/observation/health.jnx")],
            [],
            [new PackageFile("src/observation/health.jnx", packageSource.Length, sourceHash)]);
        var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, PackageJson.Options) + "\n");

        var registryRoot = Path.Combine(root, "registry");
        var artifactDirectory = Path.Combine(registryRoot, "artifacts");
        Directory.CreateDirectory(artifactDirectory);
        var artifactPath = Path.Combine(artifactDirectory, "junexis.observation-0.1.0-experimental.zip");
        using (var archive = ZipFile.Open(artifactPath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "junexis.package.json", manifestBytes);
            await WriteEntryAsync(archive, "src/observation/health.jnx", packageSource);
        }

        var index = new PackageIndexDocument(
            "junexis.package-index/0.1",
            [new PackageIndexEntry(
                "junexis.observation",
                "0.1.0-experimental",
                "junexis::observation",
                "junexis.package.json",
                ComputeSha256(manifestBytes),
                "artifacts/junexis.observation-0.1.0-experimental.zip",
                ComputeSha256(await File.ReadAllBytesAsync(artifactPath)))]);
        await File.WriteAllTextAsync(
            Path.Combine(registryRoot, "index.json"),
            JsonSerializer.Serialize(index, PackageJson.Options));

        return (await JunexisProjectManifest.DiscoverAsync(new DirectoryInfo(root)))!;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        await using var stream = entry.Open();
        await stream.WriteAsync(content);
    }

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static async Task<string> CapturePackageStateAsync(string root)
    {
        var lockPath = Path.Combine(root, PackagesLockFile.FileName);
        var packagesPath = Path.Combine(root, ".junexis", "cache", "packages");
        Assert.True(File.Exists(lockPath));
        Assert.True(Directory.Exists(packagesPath));
        var entries = new[] { packagesPath }
            .Concat(Directory.GetDirectories(packagesPath, "*", SearchOption.AllDirectories))
            .Select(path => (Type: "D", Path: path))
            .Concat(new[] { lockPath }
                .Concat(Directory.GetFiles(packagesPath, "*", SearchOption.AllDirectories))
                .Select(path => (Type: "F", Path: path)))
            .OrderBy(entry => Path.GetRelativePath(root, entry.Path), StringComparer.Ordinal)
            .ThenBy(entry => entry.Type, StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries)
        {
            AppendHashField(hash, Encoding.UTF8.GetBytes(entry.Type));
            AppendHashField(hash, Encoding.UTF8.GetBytes(
                Path.GetRelativePath(root, entry.Path).Replace('\\', '/')));
            AppendHashField(hash, entry.Type == "F"
                ? await File.ReadAllBytesAsync(entry.Path)
                : []);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendHashField(IncrementalHash hash, byte[] value)
    {
        hash.AppendData(BitConverter.GetBytes(value.LongLength));
        hash.AppendData(value);
    }

    private static void AssertNoTransactionResidue(string root)
    {
        var cache = Path.Combine(root, ".junexis", "cache");
        Assert.False(Directory.Exists(Path.Combine(cache, "packages.backup")));
        Assert.False(File.Exists(Path.Combine(cache, "packages.lock.backup")));
        Assert.False(File.Exists(Path.Combine(cache, "packages.transaction.json")));
        Assert.Empty(Directory.GetDirectories(cache, "packages.*.tmp", SearchOption.TopDirectoryOnly));
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunRestoreProcessAsync(
        string root)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(typeof(RestoreHandler).Assembly.Location);
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(root);
        return await RunProcessAsync(startInfo);
    }

    private static Task<(int ExitCode, string StandardOutput, string StandardError)> RunGatedRestoreProcessAsync(
        string root,
        string acquiredPath,
        string releasePath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(PackageRestoreTests).Assembly.Location);
        startInfo.ArgumentList.Add($"--Tests:{typeof(PackageRestoreTests).FullName}.{nameof(RestoreAsync_CrossProcessGateHelper)}");
        startInfo.Environment["JUNEXIS_TEST_RESTORE_ROOT"] = root;
        startInfo.Environment["JUNEXIS_TEST_RESTORE_ACQUIRED"] = acquiredPath;
        startInfo.Environment["JUNEXIS_TEST_RESTORE_RELEASE"] = releasePath;
        return RunProcessAsync(startInfo);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start restore test process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Restore test process did not exit within 30 seconds.");
        }
        return (process.ExitCode, await standardOutput, await standardError);
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(path))
            await Task.Delay(10, timeout.Token);
    }

    private static JsonElement CreateSnapshotArtifacts(
        PackageIndexEntry entry,
        string artifactPath,
        PackageDependency[] dependencies,
        long? sizeBytes = null)
    {
        return JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                artifactType = "package",
                id = entry.Id,
                version = entry.Version,
                @namespace = entry.Namespace,
                artifactPath = entry.Artifact,
                mediaType = "application/zip",
                sizeBytes = sizeBytes ?? new FileInfo(artifactPath).Length,
                sha256 = entry.ArtifactSha256,
                manifestSha256 = entry.ManifestSha256,
                dependencies,
                compatibility = new
                {
                    language = new { minimum = "0.2.0-experimental", maximumExclusive = "0.3.0" },
                    languageTargets = new[] { "server" }
                },
                source = $"registry:{entry.Id}@{entry.Version}"
            }
        }, PackageJson.Options);
    }

    private static string CreateSnapshotJson(JsonElement artifacts) => JsonSerializer.Serialize(new
    {
        schemaVersion = "junexis.registry-snapshot/0.1",
        snapshotId = RegistrySnapshotValidator.ComputeSnapshotId(artifacts),
        artifacts
    }, PackageJson.Options);

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-package-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class TestNetworkPackageSourceClient : INetworkPackageSourceClient
    {
        private readonly CancellationTokenSource? cancelArtifactCopy;
        private readonly string indexPath;

        internal TestNetworkPackageSourceClient(
            string indexPath,
            CancellationTokenSource? cancelArtifactCopy = null)
        {
            this.indexPath = indexPath;
            this.cancelArtifactCopy = cancelArtifactCopy;
        }

        public Task<string> GetIndexAsync(Uri indexUri, CancellationToken cancellationToken)
        {
            return File.ReadAllTextAsync(indexPath, cancellationToken);
        }

        public async Task CopyArtifactAsync(
            Uri artifactUri,
            Stream destination,
            CancellationToken cancellationToken)
        {
            if (cancelArtifactCopy is not null)
                await cancelArtifactCopy.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var artifactPath = Path.Combine(
                Path.GetDirectoryName(indexPath)!,
                Uri.UnescapeDataString(artifactUri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar));
            await CopyAsync(artifactPath, destination, cancellationToken);
        }

        private static async Task CopyAsync(
            string artifactPath,
            Stream destination,
            CancellationToken cancellationToken)
        {
            await using var source = File.OpenRead(artifactPath);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private sealed class TestRegistrySnapshotClient(string snapshotJson, string artifactPath)
        : INetworkPackageSourceClient
    {
        internal Uri? ArtifactUri { get; private set; }

        public Task<string> GetIndexAsync(Uri indexUri, CancellationToken cancellationToken) =>
            Task.FromResult(snapshotJson);

        public async Task CopyArtifactAsync(
            Uri artifactUri,
            Stream destination,
            CancellationToken cancellationToken)
        {
            ArtifactUri = artifactUri;
            await using var source = File.OpenRead(artifactPath);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    public enum RemoteFailure
    {
        Timeout,
        ConnectionLost,
        Canceled
    }

    private sealed class CollectingCompilerRegistryEventSink : ICompilerRegistryEventSink
    {
        internal List<CompilerRegistryEvent> Values { get; } = [];

        public ValueTask WriteAsync(CompilerRegistryEvent value, CancellationToken cancellationToken)
        {
            Values.Add(value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingNetworkPackageSourceClient : INetworkPackageSourceClient
    {
        internal int CallCount { get; private set; }

        public Task<string> GetIndexAsync(Uri indexUri, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Tryb offline nie może wykonać żądania HTTP ani rozwiązać DNS.");
        }

        public Task CopyArtifactAsync(
            Uri artifactUri,
            Stream destination,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Tryb offline nie może wykonać żądania HTTP ani rozwiązać DNS.");
        }
    }

    public enum RemoteHttpFailure
    {
        NotFound,
        RateLimited,
        ServerUnavailable,
        Oversized
    }

    private sealed class RemoteFailureHandler(
        string indexJson,
        RemoteFailure failure,
        CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("index.json", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(indexJson, Encoding.UTF8, "application/json")
                };
            }

            switch (failure)
            {
                case RemoteFailure.Timeout:
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    break;
                case RemoteFailure.ConnectionLost:
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new FaultingReadStream(new byte[] { 1, 2, 3 }))
                    };
                case RemoteFailure.Canceled:
                    await cancellation.CancelAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    break;
            }

            throw new InvalidOperationException("Unreachable remote failure handler state.");
        }
    }

    private sealed class RemoteHttpFailureHandler(
        string indexJson,
        RemoteHttpFailure failure) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            if (request.RequestUri!.AbsolutePath.EndsWith("index.json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(indexJson, Encoding.UTF8, "application/json")
                });
            }

            var response = failure switch
            {
                RemoteHttpFailure.NotFound => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
                RemoteHttpFailure.RateLimited => new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests),
                RemoteHttpFailure.ServerUnavailable => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable),
                RemoteHttpFailure.Oversized => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([0])
                },
                _ => throw new InvalidOperationException("Unknown simulated HTTP failure.")
            };
            if (failure == RemoteHttpFailure.RateLimited)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            if (failure == RemoteHttpFailure.Oversized)
                response.Content.Headers.ContentLength = PackageContractValidator.MaximumPackageSizeBytes + 1L;
            return Task.FromResult(response);
        }
    }

    private sealed class GatedPackageSource(
        IPackageSource inner,
        string acquiredPath,
        string releasePath) : IPackageSource
    {
        public async Task<PackageIndexDocument> LoadIndexAsync(CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(acquiredPath, string.Empty, cancellationToken);
            await WaitForFileAsync(releasePath);
            return await inner.LoadIndexAsync(cancellationToken);
        }

        public Task CopyArtifactAsync(
            PackageIndexEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default) =>
            inner.CopyArtifactAsync(entry, destination, cancellationToken);
    }

    private sealed class PartialArtifactHandler(string indexJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpContent content = request.RequestUri!.AbsolutePath.EndsWith("index.json", StringComparison.Ordinal)
                ? new StringContent(indexJson, Encoding.UTF8, "application/json")
                : new StreamContent(new FaultingReadStream(new byte[] { 1, 2, 3 }));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }

    private sealed class FaultingReadStream(byte[] prefix) : Stream
    {
        private bool prefixRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (prefixRead)
                throw new IOException("Simulated interrupted transfer.");
            prefixRead = true;
            var length = Math.Min(prefix.Length, buffer.Length);
            prefix.AsSpan(0, length).CopyTo(buffer);
            return length;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
