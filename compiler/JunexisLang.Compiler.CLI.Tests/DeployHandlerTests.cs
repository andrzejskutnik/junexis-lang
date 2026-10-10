using System.Reflection;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

/// <summary>
/// Testy regresyjne dla komendy <c>deploy</c>.
/// </summary>
public sealed class DeployHandlerTests
{
    private static string ExamplesDir => Path.GetFullPath(
        Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..", "..", "examples"));

    [Fact]
    [Trait("Category", "Deploy")]
    [Trait("Issue", "M-Platform-01")]
    public async Task Deploy_03_SmartHome_WritesMinimalGoBuildArtifacts()
    {
        var jnx = Path.Combine(ExamplesDir, "03-smart-home.jnx");
        Assert.True(File.Exists(jnx), $"Example not found: {jnx}");

        var outDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"junexis-deploy-test-{Guid.NewGuid():N}"));

        try
        {
            var exitCode = await DeployHandler.HandleAsync(
                jnx,
                outDir,
                "ghcr.io/junexis/narrative:latest");

            Assert.Equal(0, exitCode);

            var composePath = Path.Combine(outDir.FullName, "docker-compose.yml");
            var mainGoPath = Path.Combine(outDir.FullName, "server", "main.go");
            var goModPath = Path.Combine(outDir.FullName, "server", "go.mod");
            var dockerfilePath = Path.Combine(outDir.FullName, "server", "Dockerfile");
            var deploymentPlanPath = Path.Combine(outDir.FullName, "deployment-plan.json");
            var wireContractPath = Path.Combine(outDir.FullName, "wire-contract.json");

            Assert.True(File.Exists(composePath), "docker-compose.yml was not generated");
            Assert.True(File.Exists(mainGoPath), "server/main.go was not generated");
            Assert.True(File.Exists(goModPath), "server/go.mod was not generated");
            Assert.True(File.Exists(dockerfilePath), "server/Dockerfile was not generated");
            Assert.True(File.Exists(deploymentPlanPath), "deployment-plan.json was not generated");
            Assert.True(File.Exists(wireContractPath), "wire-contract.json was not generated");

            var composeContent = await File.ReadAllTextAsync(composePath);
            var goModContent = await File.ReadAllTextAsync(goModPath);
            var dockerfileContent = await File.ReadAllTextAsync(dockerfilePath);
            var deploymentPlanContent = await File.ReadAllTextAsync(deploymentPlanPath);
            var wireContractContent = await File.ReadAllTextAsync(wireContractPath);

            Assert.Contains("dockerfile: Dockerfile", composeContent);
            Assert.Contains("RUN go mod tidy && go build", dockerfileContent);
            Assert.Contains("module junexis/server", goModContent);
            Assert.Contains("JUNEXIS_WIRE_VERSION: junexis-wire/0.2", composeContent);
            Assert.Contains("\"task\": \"SensorAggregator\"", deploymentPlanContent);
            Assert.Contains("\"wireVersion\": \"junexis-wire/0.2\"", wireContractContent);
            Assert.Contains("\"protocol\": \"http\"", wireContractContent);
        }
        finally
        {
            if (outDir.Exists)
                outDir.Delete(recursive: true);
        }
    }
}
