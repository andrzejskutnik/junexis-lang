using System.IO.Compression;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class LaboratoryPackageContractTests
{
    [Theory]
    [InlineData("win-x64", "bin/junexis-laboratory.exe")]
    [InlineData("linux-x64", "bin/junexis-laboratory")]
    [InlineData("osx-arm64", "bin/junexis-laboratory")]
    public async Task CreateAndVerify_SupportedRuntime_UsesPlatformEntrypoint(
        string runtimeIdentifier,
        string expectedEntrypoint)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(root, $"laboratory-{runtimeIdentifier}.junexis-lab.zip");
            await LaboratoryPackageContract.CreateAsync(
                packagePath,
                Write(root, "laboratory.json", "{}"),
                Write(root, "README.md", "# Laboratory"),
                Write(root, "server", "native-server"),
                Write(root, "system-bundle.zip", "verified-evidence"),
                "smart-home",
                "Dom, który zauważa i reaguje",
                "preview-1",
                runtimeIdentifier,
                new string('a', 64),
                new string('b', 64));

            var verified = await LaboratoryPackageContract.VerifyAsync(packagePath);

            Assert.Equal(runtimeIdentifier, verified.Manifest.RuntimeIdentifier);
            Assert.Equal(expectedEntrypoint, verified.Manifest.Entrypoint);
            Assert.Contains(expectedEntrypoint, verified.Manifest.Files.Keys);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Create_UnsupportedRuntime_RejectsPackage()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(root, "unsupported.junexis-lab.zip");

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                LaboratoryPackageContract.CreateAsync(
                    packagePath,
                    Write(root, "laboratory.json", "{}"),
                    Write(root, "README.md", "# Laboratory"),
                    Write(root, "server", "native-server"),
                    Write(root, "system-bundle.zip", "verified-evidence"),
                    "smart-home",
                    "Dom, który zauważa i reaguje",
                    "preview-1",
                    "freebsd-x64",
                    new string('a', 64),
                    new string('b', 64)));

            Assert.Contains("Nieobsługiwany RID paczki Laboratorium", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(packagePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAndVerify_CompletePayload_PreservesIdentityAndIntegrity()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(root, "smart-home.junexis-lab.zip");
            var inspection = await LaboratoryPackageContract.CreateAsync(
                packagePath,
                Write(root, "laboratory.json", "{\"id\":\"smart-home\"}"),
                Write(root, "README.md", "# Smart home"),
                Write(root, "server.exe", "native-server"),
                Write(root, "system-bundle.zip", "verified-evidence"),
                "smart-home",
                "Dom, który zauważa i reaguje",
                "preview-1",
                "win-x64",
                new string('a', 64),
                new string('b', 64));

            var verified = await LaboratoryPackageContract.VerifyAsync(packagePath);

            Assert.Equal("smart-home", inspection.Manifest.LaboratoryId);
            Assert.Equal(inspection.Manifest.BuildId, verified.Manifest.BuildId);
            Assert.Equal(inspection.Manifest.BundleId, verified.Manifest.BundleId);
            Assert.Equal(inspection.Manifest.Files, verified.Manifest.Files);
            Assert.Equal(4, verified.VerifiedFileCount);
            Assert.Equal(new string('a', 64), verified.Manifest.BuildId);
            Assert.Equal(new string('b', 64), verified.Manifest.BundleId);
            using var archive = ZipFile.OpenRead(packagePath);
            Assert.All(archive.Entries, entry => Assert.Equal(
                new DateTime(1980, 1, 1),
                entry.LastWriteTime.DateTime));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_WhenReadmeIsAltered_RejectsPackageIntegrity()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(root, "tampered.junexis-lab.zip");
            await LaboratoryPackageContract.CreateAsync(
                packagePath,
                Write(root, "laboratory.json", "{}"),
                Write(root, "README.md", "original"),
                Write(root, "server.exe", "native-server"),
                Write(root, "system-bundle.zip", "verified-evidence"),
                "smart-home",
                "Smart home",
                "preview-1",
                "win-x64",
                new string('a', 64),
                new string('b', 64));
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.GetEntry("README.md")!.Delete();
                var replacement = archive.CreateEntry("README.md");
                await using var output = replacement.Open();
                await output.WriteAsync("tampered"u8.ToArray());
            }

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => LaboratoryPackageContract.VerifyAsync(packagePath));

            Assert.Contains("integrity failed", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "junexis-lab-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Write(string root, string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }
}
