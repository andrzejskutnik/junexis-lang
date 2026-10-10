using System.Collections.Concurrent;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JunexisLang.Compiler.Core.Platform;

/// <summary>
/// Loads platform capability profiles from YAML with fallback to hardcoded defaults.
/// </summary>
public static class PlatformProfiles
{
    private static readonly ConcurrentDictionary<string, PlatformCapabilities> Cache = new();
    private static IDeserializer? _yamlParser;

    private static readonly List<string> SearchPaths = new()
    {
        Path.Combine(AppContext.BaseDirectory, "profiles"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".JunexisLang", "profiles"),
        Path.Combine(Directory.GetCurrentDirectory(), "profiles")
    };

    /// <summary>
    /// Gets a capability profile for a platform and optional variant.
    /// </summary>
    /// <param name="platform">Platform annotation, e.g. "@embedded".</param>
    /// <param name="variant">Optional variant, e.g. "esp32c3".</param>
    /// <returns>The resolved platform capabilities.</returns>
    public static PlatformCapabilities GetProfile(string platform, string? variant = null)
    {
        variant ??= GetDefaultVariant(platform);
        var cacheKey = $"{platform}/{variant}";

        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (ResolveProfilePath(platform, variant) is not null)
        {
            var inheritedProfile = LoadProfileWithInheritance(platform, variant);
            Cache[cacheKey] = inheritedProfile;
            return inheritedProfile;
        }

        var profile = GetHardcodedProfile(platform, variant);
        Cache[cacheKey] = profile;
        return profile;
    }

    /// <summary>
    /// Clears the in-memory cache used by profile resolution.
    /// </summary>
    internal static void ClearCache() => Cache.Clear();

    /// <summary>
    /// Loads a profile with inheritance processing enabled.
    /// </summary>
    /// <param name="platform">Platform annotation, e.g. "@embedded".</param>
    /// <param name="variant">Profile variant.</param>
    /// <param name="visited">Visited chain for circular dependency detection.</param>
    /// <returns>The merged capability profile.</returns>
    public static PlatformCapabilities LoadProfileWithInheritance(string platform, string variant, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cacheKey = $"{platform}/{variant}";

        if (visited.Contains(cacheKey))
        {
            var chain = string.Join(" -> ", visited) + $" -> {cacheKey}";
            throw new InvalidOperationException($"Circular dependency detected in profile inheritance: {chain}");
        }

        visited.Add(cacheKey);

        var rawProfile = LoadRawProfile(platform, variant);
        if (rawProfile == null)
        {
            return GetProfile(platform, variant);
        }

        if (string.IsNullOrWhiteSpace(rawProfile.Extends))
        {
            return ParseYamlProfile(rawProfile);
        }

        var parent = LoadProfileWithInheritance(platform, rawProfile.Extends, visited);
        var overrideList = new HashSet<string>(rawProfile.Overrides ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        return new PlatformCapabilities
        {
            Platform = rawProfile.Platform,
            Variant = rawProfile.Variant,
            Extends = rawProfile.Extends,
            SupportsThreading = overrideList.Contains("threading")
                ? (rawProfile.Capabilities.Threading ?? false)
                : (rawProfile.Capabilities.Threading.HasValue ? rawProfile.Capabilities.Threading.Value : parent.SupportsThreading),
            SupportsFileIO = overrideList.Contains("fileIO")
                ? (rawProfile.Capabilities.FileIO ?? false)
                : (rawProfile.Capabilities.FileIO.HasValue ? rawProfile.Capabilities.FileIO.Value : parent.SupportsFileIO),
            SupportsNetworking = overrideList.Contains("networking")
                ? (rawProfile.Capabilities.Networking ?? false)
                : (rawProfile.Capabilities.Networking.HasValue ? rawProfile.Capabilities.Networking.Value : parent.SupportsNetworking),
            SupportsAsync = overrideList.Contains("async")
                ? (rawProfile.Capabilities.Async ?? false)
                : (rawProfile.Capabilities.Async.HasValue ? rawProfile.Capabilities.Async.Value : parent.SupportsAsync),
            SupportsDynamicAllocation = overrideList.Contains("dynamicAllocation")
                ? (rawProfile.Capabilities.DynamicAllocation ?? false)
                : (rawProfile.Capabilities.DynamicAllocation.HasValue ? rawProfile.Capabilities.DynamicAllocation.Value : parent.SupportsDynamicAllocation),
            SupportsRealTimeClock = overrideList.Contains("realTimeClock")
                ? (rawProfile.Capabilities.RealTimeClock ?? false)
                : (rawProfile.Capabilities.RealTimeClock.HasValue ? rawProfile.Capabilities.RealTimeClock.Value : parent.SupportsRealTimeClock),
            SupportsHardwareInterrupts = overrideList.Contains("hardwareInterrupts")
                ? (rawProfile.Capabilities.HardwareInterrupts ?? false)
                : (rawProfile.Capabilities.HardwareInterrupts.HasValue ? rawProfile.Capabilities.HardwareInterrupts.Value : parent.SupportsHardwareInterrupts),
            MaxMemoryBytes = overrideList.Contains("maxMemoryBytes")
                ? rawProfile.Limits.MaxMemoryBytes
                : (rawProfile.Limits.MaxMemoryBytes > 0 ? rawProfile.Limits.MaxMemoryBytes : parent.MaxMemoryBytes),
            MaxConcurrentTasks = overrideList.Contains("maxConcurrentTasks")
                ? rawProfile.Limits.MaxConcurrentTasks
                : (rawProfile.Limits.MaxConcurrentTasks > 0 ? rawProfile.Limits.MaxConcurrentTasks : parent.MaxConcurrentTasks),
            Constraints = parent.Constraints
                .Concat(rawProfile.Constraints.Select(ToConstraint))
                .ToList()
        };
    }

    /// <summary>
    /// Loads a raw YAML profile without resolving inheritance.
    /// </summary>
    /// <param name="platform">Platform annotation, e.g. "@embedded".</param>
    /// <param name="variant">Profile variant.</param>
    /// <returns>The deserialized profile or null if not found.</returns>
    internal static YamlPlatformProfile? LoadRawProfile(string platform, string variant)
    {
        var filePath = ResolveProfilePath(platform, variant);
        if (filePath is null)
        {
            return null;
        }

        var yaml = File.ReadAllText(filePath);
        return GetDeserializer().Deserialize<YamlPlatformProfile>(yaml);

    }

    /// <summary>
    /// Resolves a YAML profile path from the configured search paths.
    /// </summary>
    /// <param name="platform">Platform annotation, e.g. "@embedded".</param>
    /// <param name="variant">Profile variant.</param>
    /// <returns>Absolute path when found; otherwise null.</returns>
    internal static string? ResolveProfilePath(string platform, string variant)
    {
        var fileName = $"{platform.TrimStart('@')}/{variant}.yaml";

        foreach (var searchPath in SearchPaths)
        {
            var filePath = Path.Combine(searchPath, fileName);
            if (File.Exists(filePath))
            {
                return filePath;
            }
        }

        return null;
    }

    private static string GetDefaultVariant(string platform)
    {
        return platform switch
        {
            "@embedded" => "generic",
            "@server" => "linux",
            "@report" => "browser",
            _ => throw new ArgumentException($"Unknown platform: {platform}", nameof(platform))
        };
    }

    private static PlatformCapabilities ParseYamlProfile(YamlPlatformProfile profile)
    {
        return new PlatformCapabilities
        {
            Platform = profile.Platform,
            Variant = profile.Variant,
            Extends = profile.Extends,
            SupportsThreading = profile.Capabilities.Threading ?? false,
            SupportsFileIO = profile.Capabilities.FileIO ?? false,
            SupportsNetworking = profile.Capabilities.Networking ?? false,
            SupportsAsync = profile.Capabilities.Async ?? false,
            MaxMemoryBytes = profile.Limits.MaxMemoryBytes,
            MaxConcurrentTasks = profile.Limits.MaxConcurrentTasks,
            SupportsDynamicAllocation = profile.Capabilities.DynamicAllocation ?? false,
            SupportsRealTimeClock = profile.Capabilities.RealTimeClock ?? false,
            SupportsHardwareInterrupts = profile.Capabilities.HardwareInterrupts ?? false,
            Constraints = profile.Constraints.Select(ToConstraint).ToList()
        };
    }

    private static PlatformConstraint ToConstraint(YamlConstraint constraint)
    {
        return new PlatformConstraint
        {
            Type = constraint.Type,
            Severity = constraint.Severity,
            Message = constraint.Message,
            Code = constraint.Code
        };
    }

    private static IDeserializer GetDeserializer()
    {
        _yamlParser ??= new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return _yamlParser;
    }

    private static PlatformCapabilities GetHardcodedProfile(string platform, string variant)
    {
        return platform switch
        {
            "@embedded" => GetEmbeddedProfile(variant),
            "@server" => GetServerProfile(variant),
            "@report" => GetReportProfile(variant),
            _ => throw new ArgumentException($"Unknown platform: {platform}", nameof(platform))
        };
    }

    private static PlatformCapabilities GetEmbeddedProfile(string variant)
    {
        return variant switch
        {
            "esp32c3" => new PlatformCapabilities
            {
                Platform = "@embedded",
                Variant = "esp32c3",
                SupportsThreading = false,
                SupportsFileIO = false,
                SupportsNetworking = true,
                SupportsAsync = false,
                MaxMemoryBytes = 400 * 1024,
                MaxConcurrentTasks = 16,
                SupportsDynamicAllocation = true,
                SupportsRealTimeClock = true,
                SupportsHardwareInterrupts = true
            },
            "esp32s3" => new PlatformCapabilities
            {
                Platform = "@embedded",
                Variant = "esp32s3",
                SupportsThreading = false,
                SupportsFileIO = false,
                SupportsNetworking = true,
                SupportsAsync = false,
                MaxMemoryBytes = 512 * 1024,
                MaxConcurrentTasks = 32,
                SupportsDynamicAllocation = true,
                SupportsRealTimeClock = true,
                SupportsHardwareInterrupts = true
            },
            "stm32f4" or "stm32f103" => new PlatformCapabilities
            {
                Platform = "@embedded",
                Variant = variant,
                SupportsThreading = false,
                SupportsFileIO = false,
                SupportsNetworking = false,
                SupportsAsync = false,
                MaxMemoryBytes = 192 * 1024,
                MaxConcurrentTasks = 12,
                SupportsDynamicAllocation = true,
                SupportsRealTimeClock = true,
                SupportsHardwareInterrupts = true
            },
            "rp2040" => new PlatformCapabilities
            {
                Platform = "@embedded",
                Variant = "rp2040",
                SupportsThreading = false,
                SupportsFileIO = false,
                SupportsNetworking = false,
                SupportsAsync = false,
                MaxMemoryBytes = 264 * 1024,
                MaxConcurrentTasks = 16,
                SupportsDynamicAllocation = true,
                SupportsRealTimeClock = false,
                SupportsHardwareInterrupts = true
            },
            "arduino" or "avr" => new PlatformCapabilities
            {
                Platform = "@embedded",
                Variant = variant,
                SupportsThreading = false,
                SupportsFileIO = false,
                SupportsNetworking = false,
                SupportsAsync = false,
                MaxMemoryBytes = 8 * 1024,
                MaxConcurrentTasks = 4,
                SupportsDynamicAllocation = true,
                SupportsRealTimeClock = false,
                SupportsHardwareInterrupts = true
            },
            _ => new PlatformCapabilities
            {
                Platform = "@embedded",
                Variant = "generic",
                SupportsThreading = false,
                SupportsFileIO = false,
                SupportsNetworking = false,
                SupportsAsync = false,
                MaxMemoryBytes = 64 * 1024,
                MaxConcurrentTasks = 8,
                SupportsDynamicAllocation = true,
                SupportsRealTimeClock = false,
                SupportsHardwareInterrupts = true
            }
        };
    }

    private static PlatformCapabilities GetServerProfile(string variant)
    {
        return new PlatformCapabilities
        {
            Platform = "@server",
            Variant = variant,
            SupportsThreading = true,
            SupportsFileIO = true,
            SupportsNetworking = true,
            SupportsAsync = true,
            MaxMemoryBytes = 32L * 1024 * 1024 * 1024,
            MaxConcurrentTasks = 100_000,
            SupportsDynamicAllocation = true,
            SupportsRealTimeClock = true,
            SupportsHardwareInterrupts = false
        };
    }

    private static PlatformCapabilities GetReportProfile(string variant)
    {
        return new PlatformCapabilities
        {
            Platform = "@report",
            Variant = variant,
            SupportsThreading = true,
            SupportsFileIO = false,
            SupportsNetworking = true,
            SupportsAsync = true,
            MaxMemoryBytes = 2L * 1024 * 1024 * 1024,
            MaxConcurrentTasks = 100,
            SupportsDynamicAllocation = true,
            SupportsRealTimeClock = true,
            SupportsHardwareInterrupts = false
        };
    }
}