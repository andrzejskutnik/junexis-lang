using System.Diagnostics;
using System.Text.Json;
using JunexisLang.Compiler.CLI.Handlers;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class RuntimeCommandTests
{
    [Fact]
    public async Task RuntimePrimitives_PrintsHumanReadableInventory()
    {
        var result = await RunCliAsync("runtime", "primitives");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Junexis runtime primitives", result.StandardOutput);
        Assert.Contains("recv_any", result.StandardOutput);
        Assert.Contains("pobierz", result.StandardOutput);
        Assert.Contains("proposed", result.StandardOutput);
    }

    [Fact]
    public async Task RuntimePrimitives_JsonIncludesImplementationCoverage()
    {
        var result = await RunCliAsync("runtime", "primitives", "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("0.2", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("runtime primitives", root.GetProperty("command").GetString());

        var primitives = root.GetProperty("primitives").EnumerateArray().ToList();
        var recvAny = primitives.Single(primitive => primitive.GetProperty("name").GetString() == "recv_any");
        Assert.Equal("supported", recvAny.GetProperty("status").GetString());
        Assert.Contains(recvAny.GetProperty("implementations").EnumerateArray(), item => item.GetString() == "GoCodegen");
        Assert.Contains(recvAny.GetProperty("implementations").EnumerateArray(), item => item.GetString() == "TypeScriptCodegen");
        Assert.DoesNotContain(recvAny.GetProperty("implementations").EnumerateArray(), item => item.GetString() == "CppCodegen");

        var pobierz = primitives.Single(primitive => primitive.GetProperty("name").GetString() == "pobierz");
        Assert.Equal("proposed", pobierz.GetProperty("status").GetString());
        Assert.Contains(pobierz.GetProperty("implementations").EnumerateArray(), item => item.GetString() == "none");
    }

    [Fact]
    public async Task DeployEmbeddedProbe_JsonReportsDetectedDevices()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-embedded-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fakePlatformIo = await CreateFakePlatformIoAsync(root);

            var result = await RunCliAsync("deploy", "embedded", "probe", "--json", "--platformio-exe", fakePlatformIo);

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var response = document.RootElement;
            Assert.Equal("junexis.embedded-probe/0.1", response.GetProperty("schemaVersion").GetString());
            Assert.True(response.GetProperty("toolAvailable").GetBoolean());
            Assert.True(response.GetProperty("probeSucceeded").GetBoolean());
            var devices = response.GetProperty("devices").EnumerateArray().ToList();
            Assert.Single(devices);
            Assert.Equal("COM7", devices[0].GetProperty("port").GetString());
            Assert.Equal("USB Serial Device", devices[0].GetProperty("description").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task DeployEmbeddedProbe_ReturnsFailureWhenToolIsMissing()
    {
        var result = await RunCliAsync("deploy", "embedded", "probe", "--platformio-exe", "missing-platformio-executable");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("safe embedded device selection remains blocked", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        params string[] arguments)
    {
        var assembly = typeof(CompileHandler).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Junexis CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await standardOutput, await standardError);
    }

    private static async Task<string> CreateFakePlatformIoAsync(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "fake-pio.cmd");
            await File.WriteAllTextAsync(
                path,
                "@echo off\r\n" +
                "echo [{\"port\":\"COM7\",\"description\":\"USB Serial Device\",\"hwid\":\"USB VID:PID=303A:1001\",\"serial_number\":\"ABC123\",\"manufacturer\":\"Espressif\"}]\r\n" +
                "exit /b 0\r\n");
            return path;
        }

        var pathUnix = Path.Combine(root, "fake-pio.sh");
        await File.WriteAllTextAsync(
            pathUnix,
            "#!/usr/bin/env sh\n" +
            "printf '[{\"port\":\"COM7\",\"description\":\"USB Serial Device\",\"hwid\":\"USB VID:PID=303A:1001\",\"serial_number\":\"ABC123\",\"manufacturer\":\"Espressif\"}]\\n'\n");
        File.SetUnixFileMode(pathUnix, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return pathUnix;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }
}
