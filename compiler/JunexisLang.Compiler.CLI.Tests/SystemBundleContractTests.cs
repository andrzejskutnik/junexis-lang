using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;
using JunexisLang.Compiler.CLI.Output;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class SystemBundleContractTests
{
    [Fact]
    [Trait("Issue", "37")]
    public async Task CreateAndVerify_ProjectLatestBuild_ProducesDeterministicPortableInventory()
    {
        var root = await CreateProjectAsync();
        var firstPath = Path.Combine(root, "first.system-bundle.zip");
        var secondPath = Path.Combine(root, "second.system-bundle.zip");
        try
        {
            var build = await RunCliAsync(["build"], root);
            Assert.Equal(0, build.ExitCode);

            var first = await SystemBundleContract.CreateAsync(
                root,
                new FileInfo(firstPath));
            var second = await SystemBundleContract.CreateAsync(
                root,
                new FileInfo(secondPath));
            var verified = await SystemBundleContract.VerifyAsync(firstPath);

            Assert.Equal(SystemBundleDescriptor.CurrentVersion, verified.Descriptor.SchemaVersion);
            Assert.Equal("m7-system", verified.Descriptor.ProjectName);
            Assert.Equal(first.BundleId, second.BundleId);
            Assert.Equal(first.BundleId, verified.BundleId);
            Assert.Equal(
                await File.ReadAllBytesAsync(firstPath),
                await File.ReadAllBytesAsync(secondPath));
            Assert.All(verified.Descriptor.Artifacts, artifact =>
            {
                Assert.StartsWith("artifacts/", artifact.Path, StringComparison.Ordinal);
                Assert.Equal(64, artifact.Sha256.Length);
            });
            Assert.DoesNotContain(
                root,
                JsonSerializer.Serialize(verified.Descriptor),
                StringComparison.OrdinalIgnoreCase);

            using var archive = ZipFile.OpenRead(firstPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "system-bundle.json");
            Assert.Contains(
                archive.Entries,
                entry => entry.FullName == "contracts/junexis.artifacts.json");
            Assert.Contains(
                archive.Entries,
                entry => entry.FullName == "artifacts/server/main.go");
            Assert.All(archive.Entries, entry =>
                Assert.Equal(
                    new DateTime(1980, 1, 1, 0, 0, 0),
                    entry.LastWriteTime.DateTime));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "37")]
    public async Task Verify_WhenArtifactIsAltered_RejectsBundleIntegrity()
    {
        var root = await CreateProjectAsync();
        var bundlePath = Path.Combine(root, "tampered.system-bundle.zip");
        try
        {
            Assert.Equal(0, (await RunCliAsync(["build"], root)).ExitCode);
            await SystemBundleContract.CreateAsync(root, new FileInfo(bundlePath));
            using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("artifacts/server/main.go")!;
                entry.Delete();
                var replacement = archive.CreateEntry("artifacts/server/main.go");
                await using var stream = replacement.Open();
                await stream.WriteAsync("tampered"u8.ToArray());
            }

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => SystemBundleContract.VerifyAsync(bundlePath));

            Assert.Contains("integrity check failed", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "37")]
    public async Task Create_WhenLatestArtifactChanged_RejectsStaleBuild()
    {
        var root = await CreateProjectAsync();
        try
        {
            Assert.Equal(0, (await RunCliAsync(["build"], root)).ExitCode);
            await File.AppendAllTextAsync(
                Path.Combine(root, "out", "server", "main.go"),
                "\n// changed after latest build\n");

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => SystemBundleContract.CreateAsync(root, null));

            Assert.Contains("no longer matches", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "out", "system")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "37")]
    public async Task Create_RequiresProjectModeAndLatestBuild()
    {
        var root = await CreateProjectAsync();
        try
        {
            var noBuild = await Assert.ThrowsAsync<InvalidDataException>(
                () => SystemBundleContract.CreateAsync(root, null));
            Assert.Contains("run 'junexis build' first", noBuild.Message, StringComparison.Ordinal);

            var fileMode = await Assert.ThrowsAsync<InvalidDataException>(
                () => SystemBundleContract.CreateAsync(
                    Path.Combine(root, "src", "main.jnx"),
                    null));
            Assert.Contains("project-first", fileMode.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "37")]
    public async Task BundleCommands_CreateAndVerifyThroughProcessBoundary()
    {
        var root = await CreateProjectAsync();
        var bundlePath = Path.Combine(root, "cli.system-bundle.zip");
        try
        {
            Assert.Equal(0, (await RunCliAsync(["build"], root)).ExitCode);

            var create = await RunCliAsync(
                ["bundle", "create", root, "--out", bundlePath],
                root);
            var verify = await RunCliAsync(
                ["bundle", "verify", bundlePath],
                root);

            Assert.Equal(0, create.ExitCode);
            Assert.Contains("System Bundle 0.1", create.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Bundle ID:", create.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(0, verify.ExitCode);
            Assert.Contains(
                "junexis.system-bundle/0.1",
                verify.StandardOutput,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "M7.9")]
    public async Task SignAndInspect_ExactManifest_SeparatesVerificationFromTrust()
    {
        var root = await CreateProjectAsync();
        var bundlePath = Path.Combine(root, "signed.system-bundle.zip");
        var keyPath = Path.Combine(root, "signing-key.pem");
        try
        {
            Assert.Equal(0, (await RunCliAsync(["build"], root)).ExitCode);
            await SystemBundleContract.CreateAsync(root, new FileInfo(bundlePath));
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            await File.WriteAllTextAsync(keyPath, key.ExportECPrivateKeyPem());

            var signed = await SystemBundleAuthenticityContract.SignAsync(
                bundlePath,
                keyPath,
                "Junexis release test");
            var verified = await SystemBundleAuthenticityContract.InspectAsync(
                bundlePath,
                policy: "signed");
            var trusted = await SystemBundleAuthenticityContract.InspectAsync(
                bundlePath,
                policy: "trusted",
                trustedSignerFingerprints: [signed.Signature.PublicKeyFingerprintSha256]);

            Assert.Equal("verified", verified.Status);
            Assert.True(verified.PolicySatisfied);
            Assert.Equal("trusted", trusted.Status);
            Assert.True(trusted.PolicySatisfied);
            Assert.Equal(
                (await SystemBundleContract.VerifyAsync(bundlePath)).BundleId,
                signed.Signature.BundleId);
            Assert.Equal(
                signed.Signature.PublicKeyFingerprintSha256,
                trusted.PublicKeyFingerprintSha256);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "M7.9")]
    public async Task Inspect_UnsignedOrInvalidSignature_IsExplicitPolicyEvidence()
    {
        var root = await CreateProjectAsync();
        var bundlePath = Path.Combine(root, "policy.system-bundle.zip");
        var keyPath = Path.Combine(root, "signing-key.pem");
        try
        {
            Assert.Equal(0, (await RunCliAsync(["build"], root)).ExitCode);
            await SystemBundleContract.CreateAsync(root, new FileInfo(bundlePath));
            var unsigned = await SystemBundleAuthenticityContract.InspectAsync(
                bundlePath,
                policy: "signed");
            Assert.Equal("unsigned", unsigned.Status);
            Assert.False(unsigned.PolicySatisfied);

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            await File.WriteAllTextAsync(keyPath, key.ExportECPrivateKeyPem());
            var signed = await SystemBundleAuthenticityContract.SignAsync(
                bundlePath,
                keyPath,
                "Tamper test");
            var signature = JsonSerializer.Deserialize<SystemBundleSignature>(
                await File.ReadAllTextAsync(signed.Path))!;
            await File.WriteAllTextAsync(
                signed.Path,
                JsonSerializer.Serialize(signature with { Signature = Convert.ToBase64String(new byte[64]) }));

            var invalid = await SystemBundleAuthenticityContract.InspectAsync(bundlePath);
            Assert.Equal("invalid", invalid.Status);
            Assert.False(invalid.PolicySatisfied);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateProjectAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"junexis-system-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "m7-system"
            entry = "src/main.jnx"
            targets = ["server"]
            outDir = "out"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            """
            task Main() @server {
                log("M7 system bundle");
            }

            pipeline MainPipeline {
                use Main;
            }
            """);
        return root;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string[] arguments,
        string workingDirectory)
    {
        var assembly = typeof(CompileHandler).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Junexis CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await standardOutput, await standardError);
    }
}
