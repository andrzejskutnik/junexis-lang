using System.ComponentModel;
using System.Diagnostics;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class SdkToolHandler
{
    internal static async Task<int> NewProviderAsync(
        string providerId,
        DirectoryInfo? output,
        DirectoryInfo? sdkSource,
        bool json)
    {
        var arguments = new List<string> { "sdk", "new", "provider", providerId };
        if (output is not null)
        {
            arguments.AddRange(["--output", output.FullName]);
        }
        if (sdkSource is not null)
        {
            arguments.AddRange(["--sdk-source", sdkSource.FullName]);
        }
        if (json)
        {
            arguments.Add("--json");
        }
        return await InvokeAsync(arguments);
    }

    internal static async Task<int> TestProviderAsync(
        DirectoryInfo provider,
        string configuration,
        bool noBuild,
        bool offline,
        IReadOnlyList<string> profiles,
        bool json)
    {
        var arguments = new List<string>
        {
            "sdk", "test", "provider", provider.FullName,
            "--configuration", configuration
        };
        if (noBuild) { arguments.Add("--no-build"); }
        if (offline) { arguments.Add("--offline"); }
        foreach (var profile in profiles)
        {
            arguments.AddRange(["--profile", profile]);
        }
        if (json) { arguments.Add("--json"); }
        return await InvokeAsync(arguments);
    }

    internal static async Task<int> ValidateProviderAsync(DirectoryInfo provider, bool json)
    {
        var arguments = new List<string> { "sdk", "validate", "provider", provider.FullName };
        if (json) { arguments.Add("--json"); }
        return await InvokeAsync(arguments);
    }

    internal static async Task<int> CompatibilityProviderAsync(
        DirectoryInfo provider,
        string runtimeVersion,
        string? runtimeIdentifier,
        IReadOnlyList<string> requiredCapabilities,
        bool json)
    {
        var arguments = new List<string>
        {
            "sdk", "compatibility", "provider", provider.FullName,
            "--runtime-version", runtimeVersion
        };
        if (!string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            arguments.AddRange(["--rid", runtimeIdentifier]);
        }
        foreach (var capability in requiredCapabilities)
        {
            arguments.AddRange(["--require", capability]);
        }
        if (json) { arguments.Add("--json"); }
        return await InvokeAsync(arguments);
    }

    private static async Task<int> InvokeAsync(IReadOnlyList<string> arguments)
    {
        var executable = Environment.GetEnvironmentVariable("JUNEXIS_SDK_TOOL");
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = "junexis-sdk";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                await Console.Error.WriteLineAsync(
                    "error SDKL0001: Nie udało się uruchomić komponentu Junexis SDK.");
                return 1;
            }
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception)
        {
            await Console.Error.WriteLineAsync(
                "error SDKL0001: Brak komponentu Junexis SDK. Zainstaluj Junexis.SDK.Tool " +
                "albo ustaw JUNEXIS_SDK_TOOL.");
            return 1;
        }
    }
}
