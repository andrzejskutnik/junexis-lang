using JunexisLang.Compiler.Core.Platform;

namespace JunexisLang.Compiler.Tests.Platform;

/// <summary>Testy ladowania dziedziczonych profili YAML i wynikowych wariantow po merge.</summary>
public sealed partial class PlatformProfilesYamlTests
{
    /// <summary>Sprawdza ladowanie profilu dziedziczacego z YAML.</summary>
    [Fact]
    public void LoadProfileWithInheritance_ChildOverridesAndParentFallback_Work()
    {
        var profile = PlatformProfiles.LoadProfileWithInheritance("@embedded", "my-esp32");

        Assert.Equal("my-esp32", profile.Variant);
        Assert.Equal("esp32c3", profile.Extends);
        Assert.Equal(524288L, profile.MaxMemoryBytes);
        Assert.Equal(16, profile.MaxConcurrentTasks);
        Assert.True(profile.SupportsNetworking);
        Assert.Contains(profile.Constraints, constraint => constraint.Code == "CUSTOM_PROFILE");
    }

    /// <summary>Sprawdza, ze standardowy odczyt profilu uwzglednia wartosci odziedziczone.</summary>
    [Fact]
    public void GetProfile_InheritedVariant_UsesMergedValues()
    {
        PlatformProfiles.ClearCache();
        var profile = PlatformProfiles.GetProfile("@embedded", "my-esp32");

        Assert.Equal("my-esp32", profile.Variant);
        Assert.Equal(524288L, profile.MaxMemoryBytes);
        Assert.Equal(16, profile.MaxConcurrentTasks);
        Assert.True(profile.SupportsNetworking);
    }

    /// <summary>Sprawdza, ze dziedziczony profil serwerowy moze zawezac limity bez utraty mozliwosci Linuxa.</summary>
    [Fact]
    public void GetProfile_ServerInheritedVariant_UsesMergedValues()
    {
        PlatformProfiles.ClearCache();
        var profile = PlatformProfiles.GetProfile("@server", "linux-container");

        Assert.Equal("linux-container", profile.Variant);
        Assert.Equal("linux", profile.Extends);
        Assert.Equal(2147483648L, profile.MaxMemoryBytes);
        Assert.Equal(20000, profile.MaxConcurrentTasks);
        Assert.True(profile.SupportsThreading);
        Assert.True(profile.SupportsFileIO);
        Assert.True(profile.SupportsNetworking);
        Assert.True(profile.SupportsAsync);
        Assert.Contains(profile.Constraints, constraint => constraint.Code == "EPHEMERAL_STORAGE");
        Assert.Contains(profile.Constraints, constraint => constraint.Code == "CONTAINER_NETWORK");
    }
}