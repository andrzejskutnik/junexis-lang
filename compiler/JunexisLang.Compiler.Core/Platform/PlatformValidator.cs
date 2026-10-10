using System.Globalization;
using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Core.Platform;

/// <summary>
/// Validates task platform requirements against resolved platform profiles.
/// </summary>
public static class PlatformValidator
{
    private static readonly HashSet<string> ValidOverrideProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "threading",
        "fileIO",
        "networking",
        "async",
        "dynamicAllocation",
        "realTimeClock",
        "hardwareInterrupts",
        "maxMemoryBytes",
        "maxConcurrentTasks"
    };

    /// <summary>
    /// Validates all task declarations in a program.
    /// </summary>
    /// <param name="program">Parsed AST program.</param>
    /// <returns>Platform-related diagnostics for the current file.</returns>
    public static IReadOnlyList<Diagnostic> ValidateProgram(ProgramNode program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var diagnostics = new List<Diagnostic>();
        var validatedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emittedProfileConstraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Build map: device name → device's actual platform variant (e.g. "kitchen_sensor" → "esp32c3").
        // Task annotations like @embedded(kitchen_sensor) bind to a device, not a hardware profile.
        var deviceVariants = program.Declarations
            .OfType<DeviceDeclarationNode>()
            .ToDictionary(
                d => d.Name,
                d => GetVariant(d.Platform),
                StringComparer.OrdinalIgnoreCase);

        foreach (var task in program.Declarations.OfType<TaskDeclarationNode>())
        {
            foreach (var platform in task.Platforms)
            {
                var variant = GetVariant(platform);

                // Resolve device binding → actual hardware profile variant.
                if (variant is not null && deviceVariants.TryGetValue(variant, out var deviceVariant))
                {
                    variant = deviceVariant;
                }

                if (!string.IsNullOrWhiteSpace(variant))
                {
                    var profileKey = $"@{platform.Target}/{variant}";
                    if (validatedProfiles.Add(profileKey))
                    {
                        diagnostics.AddRange(ValidateReferencedProfile("@" + platform.Target, variant));
                    }
                }

                ValidateTask(task, platform, diagnostics, deviceVariants, emittedProfileConstraints);
            }
        }

        return diagnostics;
    }

    private static void ValidateTask(
        TaskDeclarationNode task,
        PlatformAnnotation platform,
        List<Diagnostic> diagnostics,
        Dictionary<string, string?> deviceVariants,
        HashSet<string> emittedProfileConstraints)
    {
        var variant = GetVariant(platform);

        // Resolve device binding → actual hardware profile variant.
        if (variant is not null && deviceVariants.TryGetValue(variant, out var deviceVariant))
        {
            variant = deviceVariant;
        }

        PlatformCapabilities profile;
        try
        {
            profile = PlatformProfiles.GetProfile("@" + platform.Target, variant);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (ArgumentException)
        {
            diagnostics.Add(new Diagnostic(
                "W5000",
                DiagnosticLevel.Warning,
                "Platform",
                $"Platform '@{platform.Target}' has no capability profile; platform validation was skipped.",
                task.File,
                platform.Line,
                platform.Column));
            return;
        }

        ValidateMemoryRequirements(task, platform, profile, diagnostics);
        ValidateFeatureRequirements(task, platform, profile, diagnostics);
        var profileKey = $"{profile.Platform}/{profile.Variant}";
        if (emittedProfileConstraints.Add(profileKey))
        {
            EmitPlatformConstraints(task, profile, diagnostics);
        }
    }

    /// <summary>
    /// Validates a referenced YAML profile and its inheritance chain.
    /// </summary>
    /// <param name="platform">Platform annotation, e.g. "@embedded".</param>
    /// <param name="variant">Profile variant.</param>
    /// <param name="maxDepth">Maximum allowed inheritance depth.</param>
    /// <returns>Profile-related diagnostics.</returns>
    internal static IReadOnlyList<Diagnostic> ValidateReferencedProfile(string platform, string variant, int maxDepth = 5)
    {
        var diagnostics = new List<Diagnostic>();
        ValidateProfileChain(platform, variant, diagnostics, new Stack<string>(), 0, maxDepth);
        return diagnostics;
    }

    private static void ValidateMemoryRequirements(TaskDeclarationNode task, PlatformAnnotation platform, PlatformCapabilities profile, List<Diagnostic> diagnostics)
    {
        var memoryArg = platform.Args.FirstOrDefault(arg => string.Equals(arg.Key, "memory", StringComparison.OrdinalIgnoreCase));
        if (memoryArg is null)
        {
            return;
        }

        var requiredBytes = ParseMemoryValue(memoryArg.Value);
        if (requiredBytes <= 0)
        {
            return;
        }

        var maxMemory = profile.MaxMemoryBytes;
        var usagePercentage = (double)requiredBytes / maxMemory * 100;

        if (requiredBytes > maxMemory)
        {
            diagnostics.Add(new Diagnostic(
                "E5001",
                DiagnosticLevel.Error,
                "Platform",
                $"Task '{task.Name}' requires {FormatBytes(requiredBytes)} but platform '{profile.Platform}/{profile.Variant}' only has {FormatBytes(maxMemory)} available.",
                task.File,
                memoryArg.Line,
                memoryArg.Column));
        }
        else if (usagePercentage > 80.0)
        {
            diagnostics.Add(new Diagnostic(
                "W5002",
                DiagnosticLevel.Warning,
                "Platform",
                $"Task '{task.Name}' requires {FormatBytes(requiredBytes)} which is {usagePercentage.ToString("F1", CultureInfo.InvariantCulture)}% of platform '{profile.Platform}/{profile.Variant}' memory limit {FormatBytes(maxMemory)}.",
                task.File,
                memoryArg.Line,
                memoryArg.Column));
        }
    }

    private static void ValidateFeatureRequirements(TaskDeclarationNode task, PlatformAnnotation platform, PlatformCapabilities profile, List<Diagnostic> diagnostics)
    {
        ValidateFeature(task, platform, profile, diagnostics, "wifi", profile.SupportsNetworking, "networking");
        ValidateFeature(task, platform, profile, diagnostics, "networking", profile.SupportsNetworking, "networking");
        ValidateFeature(task, platform, profile, diagnostics, "fileIO", profile.SupportsFileIO, "file I/O");
        ValidateFeature(task, platform, profile, diagnostics, "threading", profile.SupportsThreading, "threading");
    }

    private static void ValidateFeature(TaskDeclarationNode task, PlatformAnnotation platform, PlatformCapabilities profile, List<Diagnostic> diagnostics, string key, bool supported, string label)
    {
        var arg = platform.Args.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (arg is null || !RequiresFeature(arg.Value) || supported)
        {
            return;
        }

        diagnostics.Add(new Diagnostic(
            "E5003",
            DiagnosticLevel.Error,
            "Platform",
            $"Task '{task.Name}' requires {label} but platform '{profile.Platform}/{profile.Variant}' does not support it.",
            task.File,
            arg.Line,
            arg.Column));
    }

    private static void EmitPlatformConstraints(TaskDeclarationNode task, PlatformCapabilities profile, List<Diagnostic> diagnostics)
    {
        foreach (var constraint in profile.Constraints)
        {
            var level = constraint.Severity.ToLowerInvariant() switch
            {
                "error" => DiagnosticLevel.Error,
                "warning" => DiagnosticLevel.Warning,
                _ => DiagnosticLevel.Info
            };

            diagnostics.Add(new Diagnostic(
                string.IsNullOrWhiteSpace(constraint.Code) ? "P0000" : constraint.Code,
                level,
                "Platform",
                constraint.Message,
                task.File,
                task.Line,
                task.Column));
        }
    }

    private static void ValidateProfileChain(string platform, string variant, List<Diagnostic> diagnostics, Stack<string> stack, int depth, int maxDepth)
    {
        var key = $"{platform}/{variant}";
        var path = PlatformProfiles.ResolveProfilePath(platform, variant);

        if (path is null)
        {
            diagnostics.Add(new Diagnostic(
                "W5014",
                DiagnosticLevel.Warning,
                "Platform",
                $"Could not load profile '{variant}' for platform '{platform}'. Falling back to default profile.",
                null,
                0,
                0));
            return;
        }

        if (stack.Contains(key))
        {
            var chain = string.Join(" -> ", stack.Reverse().Append(key));
            diagnostics.Add(new Diagnostic(
                "E5011",
                DiagnosticLevel.Error,
                "Platform",
                $"Circular dependency detected in platform profile inheritance: {chain}.",
                path,
                1,
                1));
            return;
        }

        if (depth > maxDepth)
        {
            diagnostics.Add(new Diagnostic(
                "E5012",
                DiagnosticLevel.Error,
                "Platform",
                $"Inheritance depth {depth} exceeds limit {maxDepth} for profile '{variant}'.",
                path,
                1,
                1));
            return;
        }

        if (depth >= (int)Math.Ceiling(maxDepth * 0.8))
        {
            diagnostics.Add(new Diagnostic(
                "W5013",
                DiagnosticLevel.Warning,
                "Platform",
                $"Inheritance chain for profile '{variant}' is deep ({depth}/{maxDepth}).",
                path,
                1,
                1));
        }

        var profile = PlatformProfiles.LoadRawProfile(platform, variant);
        if (profile is null)
        {
            diagnostics.Add(new Diagnostic(
                "W5014",
                DiagnosticLevel.Warning,
                "Platform",
                $"Could not deserialize profile '{variant}' for platform '{platform}'.",
                path,
                1,
                1));
            return;
        }

        ValidateProfileIdentity(profile, path, diagnostics);
        ValidateProfileConstraints(profile, path, diagnostics);

        if (string.IsNullOrWhiteSpace(profile.Extends))
        {
            return;
        }

        PlatformCapabilities? parentCapabilities = null;
        try
        {
            parentCapabilities = PlatformProfiles.GetProfile(platform, profile.Extends);
        }
        catch (InvalidOperationException)
        {
        }

        ValidateProfileOverrides(profile, parentCapabilities, path, diagnostics);

        stack.Push(key);
        ValidateProfileChain(platform, profile.Extends, diagnostics, stack, depth + 1, maxDepth);
        stack.Pop();
    }

    private static void ValidateProfileIdentity(YamlPlatformProfile profile, string path, List<Diagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(profile.Platform) || !string.IsNullOrWhiteSpace(profile.Variant) || !string.IsNullOrWhiteSpace(profile.Extends))
        {
            return;
        }

        diagnostics.Add(new Diagnostic(
            "E5017",
            DiagnosticLevel.Error,
            "Platform",
            "Profile must define platform/variant or extend another profile.",
            path,
            1,
            1));
    }

    private static void ValidateProfileConstraints(YamlPlatformProfile profile, string path, List<Diagnostic> diagnostics)
    {
        var overrides = new HashSet<string>(profile.Overrides ?? [], StringComparer.OrdinalIgnoreCase);

        var invalidMemory = profile.Limits.MaxMemoryBytes < 0 ||
            (string.IsNullOrWhiteSpace(profile.Extends) && profile.Limits.MaxMemoryBytes <= 0) ||
            (overrides.Contains("maxMemoryBytes") && profile.Limits.MaxMemoryBytes <= 0);

        var invalidTasks = profile.Limits.MaxConcurrentTasks < 0 ||
            (string.IsNullOrWhiteSpace(profile.Extends) && profile.Limits.MaxConcurrentTasks <= 0) ||
            (overrides.Contains("maxConcurrentTasks") && profile.Limits.MaxConcurrentTasks <= 0);

        if (invalidMemory || invalidTasks)
        {
            diagnostics.Add(new Diagnostic(
                "E5015",
                DiagnosticLevel.Error,
                "Platform",
                "Profile limits must define positive values unless inherited from a parent profile.",
                path,
                1,
                1));
        }

        if (profile.Capabilities.Async == true && profile.Capabilities.Threading == false)
        {
            diagnostics.Add(new Diagnostic(
                "W5016",
                DiagnosticLevel.Warning,
                "Platform",
                "Profile enables async support while disabling threading. Confirm this combination is intentional.",
                path,
                1,
                1));
        }
    }

    private static void ValidateProfileOverrides(YamlPlatformProfile profile, PlatformCapabilities? parent, string path, List<Diagnostic> diagnostics)
    {
        if (profile.Overrides is null || profile.Overrides.Count == 0)
        {
            return;
        }

        foreach (var property in profile.Overrides)
        {
            if (!ValidOverrideProperties.Contains(property))
            {
                diagnostics.Add(new Diagnostic(
                    "E5010",
                    DiagnosticLevel.Error,
                    "Platform",
                    $"Invalid override property '{property}'.",
                    path,
                    1,
                    1));
                continue;
            }

            if (parent is not null && IsOverrideUnnecessary(property, profile, parent))
            {
                diagnostics.Add(new Diagnostic(
                    "W5009",
                    DiagnosticLevel.Warning,
                    "Platform",
                    $"Override '{property}' is unnecessary because the child value matches the parent profile.",
                    path,
                    1,
                    1));
            }
        }
    }

    private static bool IsOverrideUnnecessary(string property, YamlPlatformProfile child, PlatformCapabilities parent)
    {
        return property.ToLowerInvariant() switch
        {
            "threading" => (child.Capabilities.Threading ?? false) == parent.SupportsThreading,
            "fileio" => (child.Capabilities.FileIO ?? false) == parent.SupportsFileIO,
            "networking" => (child.Capabilities.Networking ?? false) == parent.SupportsNetworking,
            "async" => (child.Capabilities.Async ?? false) == parent.SupportsAsync,
            "dynamicallocation" => (child.Capabilities.DynamicAllocation ?? false) == parent.SupportsDynamicAllocation,
            "realtimeclock" => (child.Capabilities.RealTimeClock ?? false) == parent.SupportsRealTimeClock,
            "hardwareinterrupts" => (child.Capabilities.HardwareInterrupts ?? false) == parent.SupportsHardwareInterrupts,
            "maxmemorybytes" => child.Limits.MaxMemoryBytes == parent.MaxMemoryBytes,
            "maxconcurrenttasks" => child.Limits.MaxConcurrentTasks == parent.MaxConcurrentTasks,
            _ => false
        };
    }

    private static string? GetVariant(PlatformAnnotation platform)
    {
        var positional = platform.Args.FirstOrDefault(arg => arg.Key is null);
        if (positional is null)
        {
            return null;
        }

        return positional.Value switch
        {
            IdentifierExprNode identifier => identifier.Name,
            LiteralNode literal when literal.Value is string text => text,
            LiteralNode literal => literal.RawText,
            _ => null
        };
    }

    private static bool RequiresFeature(ExprNode expr)
    {
        return expr switch
        {
            LiteralNode { Value: bool flag } => flag,
            LiteralNode { Value: string text } => text.Equals("true", StringComparison.OrdinalIgnoreCase),
            IdentifierExprNode identifier => identifier.Name.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static long ParseMemoryValue(ExprNode expr)
    {
        return expr switch
        {
            LiteralNode literal => ParseMemoryValue(literal.Value, literal.RawText),
            IdentifierExprNode identifier => ParseMemoryValue(identifier.Name, identifier.Name),
            _ => -1
        };
    }

    private static long ParseMemoryValue(object? value, string rawText)
    {
        if (value is long longValue)
        {
            return longValue;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        var text = rawText.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        var index = 0;
        while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
        {
            index++;
        }

        if (index == 0)
        {
            return -1;
        }

        var numericPart = text[..index];
        var unitPart = text[index..].Trim();

        if (!double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return -1;
        }

        var multiplier = unitPart switch
        {
            "B" or "" => 1L,
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            _ => -1L
        };

        return multiplier < 0 ? -1 : (long)(numeric * multiplier);
    }

    private static string FormatBytes(long bytes)
    {
        const long kb = 1024;
        const long mb = 1024 * kb;
        const long gb = 1024 * mb;

        return bytes switch
        {
            >= gb => $"{(bytes / (double)gb).ToString("F1", CultureInfo.InvariantCulture)} GB",
            >= mb => $"{(bytes / (double)mb).ToString("F1", CultureInfo.InvariantCulture)} MB",
            >= kb => $"{(bytes / (double)kb).ToString("F1", CultureInfo.InvariantCulture)} KB",
            _ => $"{bytes} B"
        };
    }
}
