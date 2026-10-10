using System.Text.Json;
using JunexisLang.Compiler.CLI.Project;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class PlatformVariantSelectorTests
{
    [Fact]
    public void SelectLockedPackages_KeepsLogicalLockAndSelectsArtifactByRid()
    {
        var package = LockedPackage();
        var lockDocument = new PackagesLockDocument("junexis.packages.lock/0.1", [package]);
        var lockBefore = JsonSerializer.Serialize(lockDocument, PackageJson.Options);
        var catalog = PlatformVariantSelector.ParseCatalog(CatalogJson(
            Entry("win-x64", "windows", "x64", "authenticode", "zip", '1'),
            Entry("linux-x64", "linux", "x64", "linux-package", "tar.gz", '2'),
            Entry("osx-arm64", "macos", "arm64", "apple-developer-id", "pkg", '3')),
            "test catalog");

        var windows = Assert.Single(PlatformVariantSelector.SelectLockedPackages(lockDocument, catalog, "win-x64"));
        var linux = Assert.Single(PlatformVariantSelector.SelectLockedPackages(lockDocument, catalog, "linux-x64"));

        Assert.Equal((package.Id, package.Version), (windows.Component, windows.Version));
        Assert.Equal((package.Id, package.Version), (linux.Component, linux.Version));
        Assert.NotEqual(windows.Artifact.Sha256, linux.Artifact.Sha256);
        Assert.Equal("win-x64", windows.Rid);
        Assert.Equal("linux-x64", linux.Rid);
        Assert.Equal(lockBefore, JsonSerializer.Serialize(lockDocument, PackageJson.Options));
        Assert.DoesNotContain("rid", lockBefore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("architecture", lockBefore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", lockBefore, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageLock_RejectsPhysicalRidField()
    {
        var package = LockedPackage();
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.packages.lock/0.1",
            packages = new[]
            {
                new
                {
                    id = package.Id,
                    version = package.Version,
                    @namespace = package.Namespace,
                    source = package.Source,
                    manifestSha256 = package.ManifestSha256,
                    artifactSha256 = package.ArtifactSha256,
                    dependencies = package.Dependencies,
                    rid = "win-x64"
                }
            }
        }, PackageJson.Options);

        var error = Assert.Throws<InvalidDataException>(() =>
            PackageJson.Deserialize<PackagesLockDocument>(json, "test lock"));

        Assert.Contains("rid", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectLockedPackages_DoesNotFallbackToForeignRid()
    {
        var catalog = PlatformVariantSelector.ParseCatalog(CatalogJson(
            Entry("win-x64", "windows", "x64", "authenticode", "zip", '1')),
            "test catalog");

        var error = Assert.Throws<InvalidDataException>(() => PlatformVariantSelector.SelectLockedPackages(
            new PackagesLockDocument("junexis.packages.lock/0.1", [LockedPackage()]),
            catalog,
            "linux-x64"));

        Assert.Contains("No cross-RID fallback", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseCatalog_RejectsDuplicateLogicalVariant()
    {
        var entry = Entry("win-x64", "windows", "x64", "authenticode", "zip", '1');

        var error = Assert.Throws<InvalidDataException>(() =>
            PlatformVariantSelector.ParseCatalog(CatalogJson(entry, entry), "test catalog"));

        Assert.Contains("Duplicate platform artifact", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectLockedPackages_RequiresExactLogicalVersion()
    {
        var catalog = PlatformVariantSelector.ParseCatalog(CatalogJson(
            Entry("win-x64", "windows", "x64", "authenticode", "zip", '1', version: "1.2.4")),
            "test catalog");

        var error = Assert.Throws<InvalidDataException>(() => PlatformVariantSelector.SelectLockedPackages(
            new PackagesLockDocument("junexis.packages.lock/0.1", [LockedPackage()]),
            catalog,
            "win-x64"));

        Assert.Contains("junexis.observation@1.2.3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseCatalog_RejectsRidContradictionAndUnverifiedSignature()
    {
        var contradiction = Entry("linux-x64", "windows", "x64", "authenticode", "tar.gz", '1');
        Assert.Throws<InvalidDataException>(() =>
            PlatformVariantSelector.ParseCatalog(CatalogJson(contradiction), "test catalog"));

        var unverifiedCatalog = PlatformVariantSelector.ParseCatalog(CatalogJson(
            Entry("win-x64", "windows", "x64", "authenticode", "zip", '1', verified: false)),
            "test catalog");
        var error = Assert.Throws<InvalidDataException>(() => PlatformVariantSelector.SelectLockedPackages(
            new PackagesLockDocument("junexis.packages.lock/0.1", [LockedPackage()]),
            unverifiedCatalog,
            "win-x64"));
        Assert.Contains("not system-verified", error.Message, StringComparison.Ordinal);
    }

    private static PackageLockEntry LockedPackage() => new(
        "junexis.observation",
        "1.2.3",
        "junexis::observation",
        "registry:junexis.observation@1.2.3",
        new string('a', 64),
        new string('b', 64),
        []);

    private static string CatalogJson(params object[] entries) => JsonSerializer.Serialize(new
    {
        schemaVersion = "junexis.platform-release-catalog/0.2",
        channel = "preview",
        issuedAt = "2026-09-10T10:00:00Z",
        expiresAt = "2026-09-11T10:00:00Z",
        entries,
        algorithm = "ecdsa-p256-sha256-der",
        keyId = $"sha256:{new string('c', 64)}",
        signature = "dGVzdA=="
    }, PackageJson.Options);

    private static object Entry(
        string rid,
        string system,
        string architecture,
        string signatureKind,
        string format,
        char artifactHash,
        string version = "1.2.3",
        bool verified = true) => new
        {
            component = "junexis.observation",
            version,
            rid,
            system,
            architecture,
            artifact = new
            {
                format,
                url = $"https://releases.example.test/{rid}/package",
                sizeBytes = 1024,
                sha256 = new string(artifactHash, 64)
            },
            systemRequirements = new
            {
                minimumOsVersion = "1.0",
                nativeDependencies = Array.Empty<object>()
            },
            systemSignature = new
            {
                kind = signatureKind,
                state = verified ? "verified" : "required",
                publisher = verified ? "Junexis test" : null,
                signatureSha256 = verified ? new string('d', 64) : null
            },
            provenanceSha256 = new string('e', 64),
            sbomSha256 = new string('f', 64),
            minimumSupportedVersion = "1.0.0",
            revoked = false
        };
}
