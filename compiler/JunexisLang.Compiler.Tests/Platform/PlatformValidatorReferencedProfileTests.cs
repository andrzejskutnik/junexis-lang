using JunexisLang.Compiler.Core.Platform;

namespace JunexisLang.Compiler.Tests.Platform;

/// <summary>Testy walidacji referencjonowanych profili platformy.</summary>
public sealed partial class PlatformValidatorTests
{
    /// <summary>
    /// Verifies invalid override property emits E5010.
    /// </summary>
    [Fact]
    public void ValidateReferencedProfile_InvalidOverride_EmitsE5010()
    {
        var variant = $"test-invalid-override-{Guid.NewGuid():N}";
        WriteProfile(variant, """
version: "1.0"
platform: "@embedded"
variant: "VARIANT"
extends: "esp32c3"
overrides: [bogus]
limits:
  maxMemoryBytes: 0
  maxConcurrentTasks: 0
""".Replace("VARIANT", variant));

        try
        {
            PlatformProfiles.ClearCache();
            var diagnostics = PlatformValidator.ValidateReferencedProfile("@embedded", variant);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "E5010");
        }
        finally
        {
            DeleteProfile(variant);
        }
    }

    /// <summary>
    /// Verifies unnecessary override emits W5009.
    /// </summary>
    [Fact]
    public void ValidateReferencedProfile_UnnecessaryOverride_EmitsW5009()
    {
        var variant = $"test-unnecessary-override-{Guid.NewGuid():N}";
        WriteProfile(variant, """
version: "1.0"
platform: "@embedded"
variant: "VARIANT"
extends: "esp32c3"
overrides: [networking]
capabilities:
  networking: true
limits:
  maxMemoryBytes: 0
  maxConcurrentTasks: 0
""".Replace("VARIANT", variant));

        try
        {
            PlatformProfiles.ClearCache();
            var diagnostics = PlatformValidator.ValidateReferencedProfile("@embedded", variant);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "W5009");
        }
        finally
        {
            DeleteProfile(variant);
        }
    }

    /// <summary>
    /// Verifies circular inheritance emits E5011.
    /// </summary>
    [Fact]
    public void ValidateReferencedProfile_CircularInheritance_EmitsE5011()
    {
        var first = $"test-cycle-a-{Guid.NewGuid():N}";
        var second = $"test-cycle-b-{Guid.NewGuid():N}";
        WriteProfile(first, $$"""
version: "1.0"
platform: "@embedded"
variant: "{{first}}"
extends: "{{second}}"
limits:
  maxMemoryBytes: 1
  maxConcurrentTasks: 1
""");
        WriteProfile(second, $$"""
version: "1.0"
platform: "@embedded"
variant: "{{second}}"
extends: "{{first}}"
limits:
  maxMemoryBytes: 1
  maxConcurrentTasks: 1
""");

        try
        {
            PlatformProfiles.ClearCache();
            var diagnostics = PlatformValidator.ValidateReferencedProfile("@embedded", first);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "E5011");
        }
        finally
        {
            DeleteProfile(first);
            DeleteProfile(second);
        }
    }

    /// <summary>
    /// Verifies deep inheritance emits W5013 and E5012.
    /// </summary>
    [Fact]
    public void ValidateReferencedProfile_DeepInheritance_EmitsDepthDiagnostics()
    {
        var names = Enumerable.Range(0, 7)
            .Select(index => $"test-depth-{index}-{Guid.NewGuid():N}")
            .ToArray();

        for (var index = 0; index < names.Length; index++)
        {
            var extends = index < names.Length - 1 ? $"extends: \"{names[index + 1]}\"" : string.Empty;
            WriteProfile(names[index], $$"""
version: "1.0"
platform: "@embedded"
variant: "{{names[index]}}"
{{extends}}
limits:
  maxMemoryBytes: 1
  maxConcurrentTasks: 1
""");
        }

        try
        {
            PlatformProfiles.ClearCache();
            var diagnostics = PlatformValidator.ValidateReferencedProfile("@embedded", names[0]);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "W5013");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "E5012");
        }
        finally
        {
            foreach (var name in names)
            {
                DeleteProfile(name);
            }
        }
    }

    /// <summary>
    /// Verifies invalid limits and async/threading mismatch emit E5015 and W5016.
    /// </summary>
    [Fact]
    public void ValidateReferencedProfile_InvalidConstraints_EmitE5015AndW5016()
    {
        var variant = $"test-invalid-constraints-{Guid.NewGuid():N}";
        WriteProfile(variant, $$"""
version: "1.0"
platform: "@embedded"
variant: "{{variant}}"
capabilities:
  async: true
  threading: false
limits:
  maxMemoryBytes: -1
  maxConcurrentTasks: -1
""");

        try
        {
            PlatformProfiles.ClearCache();
            var diagnostics = PlatformValidator.ValidateReferencedProfile("@embedded", variant);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "E5015");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "W5016");
        }
        finally
        {
            DeleteProfile(variant);
        }
    }

    /// <summary>
    /// Verifies missing identity emits E5017.
    /// </summary>
    [Fact]
    public void ValidateReferencedProfile_MissingIdentity_EmitsE5017()
    {
        var variant = $"test-missing-identity-{Guid.NewGuid():N}";
        WriteProfile(variant, """
version: "1.0"
platform: ""
variant: ""
limits:
  maxMemoryBytes: 1
  maxConcurrentTasks: 1
""");

        try
        {
            PlatformProfiles.ClearCache();
            var diagnostics = PlatformValidator.ValidateReferencedProfile("@embedded", variant);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "E5017");
        }
        finally
        {
            DeleteProfile(variant);
        }
    }

    /// <summary>
    /// Verifies missing profile emits W5014.
    /// </summary>
    [Fact]
    public void ValidateReferencedProfile_MissingProfile_EmitsW5014()
    {
        PlatformProfiles.ClearCache();
        var diagnostics = PlatformValidator.ValidateReferencedProfile("@embedded", $"missing-{Guid.NewGuid():N}");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "W5014");
    }
}