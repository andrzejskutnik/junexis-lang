using System.Diagnostics;
using System.Text.Json;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class EmbeddedProbeHandler
{
    internal static async Task<int> HandleAsync(string platformIoExe, bool json)
    {
        var result = await ProbeAsync(platformIoExe).ConfigureAwait(false);

        if (json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            };
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, options)).ConfigureAwait(false);
        }
        else
        {
            await WriteHumanReadableAsync(result).ConfigureAwait(false);
        }

        return result.ProbeSucceeded ? 0 : 1;
    }

    internal static async Task<EmbeddedProbeResult> ProbeAsync(string platformIoExe)
    {
        var resolved = ResolveExecutable(platformIoExe);
        if (resolved is null)
        {
            return new EmbeddedProbeResult(
                "junexis.embedded-probe/0.1",
                platformIoExe,
                null,
                false,
                false,
                "PlatformIO executable was not found; safe embedded device selection remains blocked.",
                []);
        }

        var invocation = await RunCaptureAsync(resolved, ["device", "list", "--json-output"], Environment.CurrentDirectory)
            .ConfigureAwait(false);
        if (invocation.ExitCode != 0)
        {
            var failure = string.IsNullOrWhiteSpace(invocation.StandardError)
                ? "PlatformIO device discovery failed."
                : invocation.StandardError.Trim();
            return new EmbeddedProbeResult(
                "junexis.embedded-probe/0.1",
                platformIoExe,
                resolved,
                true,
                false,
                failure,
                []);
        }

        try
        {
            var devices = ParseDevices(invocation.StandardOutput);
            var message = devices.Count == 0
                ? "PlatformIO is available, but no candidate embedded devices were detected."
                : $"PlatformIO detected {devices.Count} candidate embedded device(s).";
            return new EmbeddedProbeResult(
                "junexis.embedded-probe/0.1",
                platformIoExe,
                resolved,
                true,
                true,
                message,
                devices);
        }
        catch (JsonException ex)
        {
            return new EmbeddedProbeResult(
                "junexis.embedded-probe/0.1",
                platformIoExe,
                resolved,
                true,
                false,
                $"PlatformIO returned invalid device JSON: {ex.Message}",
                []);
        }
    }

    private static async Task WriteHumanReadableAsync(EmbeddedProbeResult result)
    {
        if (!result.ProbeSucceeded)
        {
            await Console.Error.WriteLineAsync($"Embedded deploy probe blocked: {result.Message}").ConfigureAwait(false);
            return;
        }

        await Console.Out.WriteLineAsync("Embedded deploy probe").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"PlatformIO: {result.ToolPath}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(result.Message).ConfigureAwait(false);
        if (result.Devices.Count == 0)
        {
            await Console.Out.WriteLineAsync("No candidate devices detected.").ConfigureAwait(false);
            return;
        }

        foreach (var device in result.Devices)
        {
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(device.Description))
                details.Add(device.Description);
            if (!string.IsNullOrWhiteSpace(device.Manufacturer))
                details.Add(device.Manufacturer);
            if (!string.IsNullOrWhiteSpace(device.SerialNumber))
                details.Add($"serial {device.SerialNumber}");
            if (!string.IsNullOrWhiteSpace(device.HardwareId))
                details.Add(device.HardwareId);

            await Console.Out.WriteLineAsync(
                details.Count == 0
                    ? $"- {device.Port}"
                    : $"- {device.Port} · {string.Join(" · ", details)}").ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<EmbeddedProbeDevice> ParseDevices(string stdout)
    {
        using var document = JsonDocument.Parse(stdout);
        var devicesElement = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement,
            JsonValueKind.Object when document.RootElement.TryGetProperty("devices", out var deviceArray) && deviceArray.ValueKind == JsonValueKind.Array => deviceArray,
            _ => throw new JsonException("expected an array or an object with a 'devices' array")
        };

        var devices = new List<EmbeddedProbeDevice>();
        foreach (var item in devicesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var port = GetString(item, "port") ?? GetString(item, "device") ?? GetString(item, "name") ?? "unknown";
            devices.Add(new EmbeddedProbeDevice(
                port,
                GetString(item, "description") ?? GetString(item, "desc"),
                GetString(item, "hwid"),
                GetString(item, "serial_number") ?? GetString(item, "serialNumber"),
                GetString(item, "manufacturer")));
        }

        return devices;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return null;

        return property.GetString();
    }

    private static async Task<ProcessResult> RunCaptureAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var requiresCommandInterpreter = OperatingSystem.IsWindows() &&
            Path.GetExtension(executable) is ".cmd" or ".bat";
        var processExecutable = requiresCommandInterpreter
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : executable;
        var info = new ProcessStartInfo(processExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (requiresCommandInterpreter)
        {
            var command = $"\"{executable}\" {string.Join(" ", arguments.Select(QuoteCommandArgument))}";
            info.Arguments = $"/d /c \"{command}\"";
        }
        else
        {
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start PlatformIO device probe.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static string QuoteCommandArgument(string argument) =>
        argument.Any(character => char.IsWhiteSpace(character) || character is '&' or '|' or '<' or '>')
            ? $"\"{argument.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : argument;

    private static string? ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(executable) ? executable : null;

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(
                    directory,
                    executable.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                        ? executable
                        : executable + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    internal sealed record EmbeddedProbeResult(
        string SchemaVersion,
        string Tool,
        string? ToolPath,
        bool ToolAvailable,
        bool ProbeSucceeded,
        string Message,
        IReadOnlyList<EmbeddedProbeDevice> Devices);

    internal sealed record EmbeddedProbeDevice(
        string Port,
        string? Description,
        string? HardwareId,
        string? SerialNumber,
        string? Manufacturer);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}