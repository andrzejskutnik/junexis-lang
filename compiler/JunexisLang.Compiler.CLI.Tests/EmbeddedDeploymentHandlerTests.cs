using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class EmbeddedDeploymentHandlerTests
{
    [Fact]
    public async Task SelectAsync_WritesSelectionForDetectedPort()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-embedded-select-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fakePio = await CreateFakePlatformIoAsync(root);
            var selectionPath = Path.Combine(root, "embedded-device-selection.json");

            var exitCode = await EmbeddedDeploymentHandler.SelectAsync(
                fakePio,
                "COM7",
                new FileInfo(selectionPath),
                json: false);

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(selectionPath));
            var rootElement = document.RootElement;
            Assert.Equal("junexis.embedded-device-selection/0.1", rootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("COM7", rootElement.GetProperty("port").GetString());
            Assert.Equal("USB Serial Device", rootElement.GetProperty("description").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task FlashAsync_UsesExplicitSelectionAndWritesReceipt()
    {
        var root = await CreateEmbeddedProjectAsync();
        var bundlePath = Path.Combine(root, "out", "system", "embedded-smoke.system-bundle.zip");
        var uploadLogPath = Path.Combine(root, "upload.log");

        try
        {
            var fakePio = await CreateFakePlatformIoAsync(root, uploadLogPath);
            var packageExitCode = await PackageHandler.HandleAsync(
                root,
                new FileInfo(bundlePath),
                ["embedded"],
                "missing-go",
                "missing-npm",
                fakePio);
            Assert.Equal(0, packageExitCode);

            var selectionPath = Path.Combine(root, "embedded-device-selection.json");
            var selectExitCode = await EmbeddedDeploymentHandler.SelectAsync(
                fakePio,
                "COM7",
                new FileInfo(selectionPath),
                json: false);
            Assert.Equal(0, selectExitCode);

            var flashReceiptPath = Path.Combine(root, "embedded-flash-receipt.json");
            var flashExitCode = await EmbeddedDeploymentHandler.FlashAsync(
                new FileInfo(bundlePath),
                null,
                new FileInfo(selectionPath),
                fakePio,
                null,
                new FileInfo(flashReceiptPath),
                json: false);

            Assert.Equal(0, flashExitCode);
            var uploadLog = await File.ReadAllTextAsync(uploadLogPath);
            Assert.Contains("--target upload", uploadLog, StringComparison.Ordinal);
            Assert.Contains("--upload-port COM7", uploadLog, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(flashReceiptPath));
            var rootElement = document.RootElement;
            Assert.Equal("junexis.embedded-flash-receipt/0.1", rootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("succeeded", rootElement.GetProperty("lifecycleStatus").GetString());
            Assert.Equal("COM7", rootElement.GetProperty("port").GetString());
            Assert.Contains("JUNEXIS_BUILD_ID", uploadLog, StringComparison.Ordinal);
            Assert.Contains(rootElement.GetProperty("buildId").GetString()!, uploadLog, StringComparison.Ordinal);
            Assert.Contains("JUNEXIS_BUNDLE_ID", uploadLog, StringComparison.Ordinal);
            Assert.Contains(rootElement.GetProperty("bundleId").GetString()!, uploadLog, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    [Trait("Issue", "M7.9")]
    public async Task FlashAsync_SignedPolicyRejectsUnsignedBundleBeforeUpload()
    {
        var root = await CreateEmbeddedProjectAsync();
        var bundlePath = Path.Combine(root, "out", "system", "unsigned.system-bundle.zip");
        var uploadLogPath = Path.Combine(root, "upload.log");
        try
        {
            var fakePio = await CreateFakePlatformIoAsync(root, uploadLogPath);
            Assert.Equal(0, await PackageHandler.HandleAsync(
                root,
                new FileInfo(bundlePath),
                ["embedded"],
                "missing-go",
                "missing-npm",
                fakePio));
            var selectionPath = Path.Combine(root, "embedded-device-selection.json");
            Assert.Equal(0, await EmbeddedDeploymentHandler.SelectAsync(
                fakePio,
                "COM7",
                new FileInfo(selectionPath),
                json: false));

            var exitCode = await EmbeddedDeploymentHandler.FlashAsync(
                new FileInfo(bundlePath),
                null,
                new FileInfo(selectionPath),
                fakePio,
                null,
                null,
                json: false,
                authenticityPolicy: "signed");

            Assert.Equal(1, exitCode);
            Assert.DoesNotContain(
                "--target upload",
                await File.ReadAllTextAsync(uploadLogPath),
                StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task SelectAsync_PublishesCanonicalEvents_WhenIngressIsConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-embedded-select-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("JUNEXIS_OBSERVABILITY_INGRESS_URL");
        try
        {
            var port = GetFreeTcpPort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/ingress/events/");
            listener.Start();
            Environment.SetEnvironmentVariable("JUNEXIS_OBSERVABILITY_INGRESS_URL", $"http://127.0.0.1:{port}/ingress/events");

            var captureTask = CaptureRequestsAsync(listener, expectedCount: 2);
            var fakePio = await CreateFakePlatformIoAsync(root);

            var exitCode = await EmbeddedDeploymentHandler.SelectAsync(
                fakePio,
                "COM7",
                new FileInfo(Path.Combine(root, "embedded-device-selection.json")),
                json: false);

            Assert.Equal(0, exitCode);
            var payloads = await captureTask;
            Assert.Equal(2, payloads.Count);

            using var audit = JsonDocument.Parse(payloads[0]);
            using var health = JsonDocument.Parse(payloads[1]);
            Assert.Equal("Audit", audit.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Health", health.RootElement.GetProperty("kind").GetString());
            Assert.Equal("deploy.embedded.select", JsonDocument.Parse(audit.RootElement.GetProperty("payloadJson").GetString()!).RootElement.GetProperty("action").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JUNEXIS_OBSERVABILITY_INGRESS_URL", previous);
            TryDelete(root);
        }
    }

    private static async Task<string> CreateEmbeddedProjectAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-embedded-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            """
            name = "embedded-smoke"
            entry = "src/main.jnx"
            targets = ["embedded"]
            outDir = "out"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "main.jnx"),
            """
            task Blink() @embedded(esp32c3) {
                log("blink");
            }

            pipeline BlinkPipeline {
                use Blink;
            }
            """);
        return root;
    }

    private static async Task<string> CreateFakePlatformIoAsync(string root, string? uploadLogPath = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-pio.cmd");
            var uploadLog = uploadLogPath is null ? string.Empty : uploadLogPath.Replace("\\", "\\\\", StringComparison.Ordinal);
            await File.WriteAllTextAsync(
                path,
                "@echo off\r\n" +
                "if \"%1\"==\"device\" if \"%2\"==\"list\" if \"%3\"==\"--json-output\" (\r\n" +
                "  echo [{\"port\":\"COM7\",\"description\":\"USB Serial Device\",\"hwid\":\"USB VID:PID=303A:1001\",\"serial_number\":\"ABC123\",\"manufacturer\":\"Espressif\"}]\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "if \"%1\"==\"run\" (\r\n" +
                (uploadLogPath is null ? string.Empty : $"  >> \"{uploadLog}\" echo %*\r\n") +
                (uploadLogPath is null ? string.Empty : $"  >> \"{uploadLog}\" echo %PLATFORMIO_BUILD_FLAGS%\r\n") +
                "  echo %* | findstr /c:\"--target upload\" >nul\r\n" +
                "  if not errorlevel 1 (\r\n" +
                "    echo %* | findstr /c:\"--upload-port COM7\" >nul\r\n" +
                "    if errorlevel 1 exit /b 27\r\n" +
                "  )\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "echo unexpected PlatformIO invocation %* 1>&2\r\n" +
                "exit /b 99\r\n");
            return path;
        }

        var shellPath = Path.Combine(root, "fake-pio.sh");
        var uploadLogUnix = uploadLogPath ?? Path.Combine(root, "upload.log");
        await File.WriteAllTextAsync(
            shellPath,
            "#!/usr/bin/env sh\n" +
            "if [ \"$1\" = \"device\" ] && [ \"$2\" = \"list\" ] && [ \"$3\" = \"--json-output\" ]; then\n" +
            "  printf '[{\"port\":\"COM7\",\"description\":\"USB Serial Device\",\"hwid\":\"USB VID:PID=303A:1001\",\"serial_number\":\"ABC123\",\"manufacturer\":\"Espressif\"}]\\n'\n" +
            "  exit 0\n" +
            "fi\n" +
            "if [ \"$1\" = \"run\" ]; then\n" +
            $"  printf '%s\n' \"$*\" >> '{uploadLogUnix.Replace("'", "'\\''")}'\n" +
            $"  printf '%s\n' \"$PLATFORMIO_BUILD_FLAGS\" >> '{uploadLogUnix.Replace("'", "'\\''")}'\n" +
            "  case \"$*\" in\n" +
            "    *\"--target upload\"*)\n" +
            "      case \"$*\" in\n" +
            "        *\"--upload-port COM7\"*) exit 0 ;;\n" +
            "        *) exit 27 ;;\n" +
            "      esac\n" +
            "      ;;\n" +
            "    *) exit 0 ;;\n" +
            "  esac\n" +
            "fi\n" +
            "echo unexpected PlatformIO invocation $* 1>&2\n" +
            "exit 99\n");
        File.SetUnixFileMode(shellPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }

    private static async Task<List<string>> CaptureRequestsAsync(HttpListener listener, int expectedCount)
    {
        var payloads = new List<string>(expectedCount);
        for (var index = 0; index < expectedCount; index++)
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            payloads.Add(await reader.ReadToEndAsync());
            context.Response.StatusCode = 202;
            context.Response.Close();
        }

        return payloads;
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
