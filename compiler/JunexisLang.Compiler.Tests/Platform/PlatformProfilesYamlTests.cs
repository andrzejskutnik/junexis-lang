using JunexisLang.Compiler.Core.Platform;

namespace JunexisLang.Compiler.Tests.Platform;

/// <summary>Testy profili platform opartych o YAML z fallbackiem do wartosci wbudowanych.</summary>
public sealed partial class PlatformProfilesYamlTests
{
    /// <summary>Sprawdza domyslne rozwiazywanie wariantu dla kazdej platformy.</summary>
    [Fact]
    public void GetProfile_DefaultVariant_UsesCorrectDefault()
    {
        var embeddedProfile = PlatformProfiles.GetProfile("@embedded");
        var serverProfile = PlatformProfiles.GetProfile("@server");
        var reportProfile = PlatformProfiles.GetProfile("@report");

        Assert.Equal("generic", embeddedProfile.Variant);
        Assert.Equal("linux", serverProfile.Variant);
        Assert.Equal("browser", reportProfile.Variant);
    }

    /// <summary>Sprawdza ponowne wykorzystanie cache dla powtarzanych odczytow.</summary>
    [Fact]
    public void GetProfile_CachesResults()
    {
        var profile1 = PlatformProfiles.GetProfile("@server", "linux");
        var profile2 = PlatformProfiles.GetProfile("@server", "linux");

        Assert.Same(profile1, profile2);
    }

    /// <summary>Sprawdza, ze rozne warianty sa cachowane niezaleznie.</summary>
    [Fact]
    public void GetProfile_DifferentVariants_CacheSeparately()
    {
        var profile1 = PlatformProfiles.GetProfile("@embedded", "esp32c3");
        var profile2 = PlatformProfiles.GetProfile("@embedded", "stm32f4");

        Assert.NotSame(profile1, profile2);
        Assert.Equal("esp32c3", profile1.Variant);
        Assert.Equal("stm32f4", profile2.Variant);
    }

    /// <summary>Sprawdza szybkie odrzucenie nieprawidlowych nazw platform.</summary>
    [Fact]
    public void GetProfile_InvalidPlatform_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => PlatformProfiles.GetProfile("@invalid"));
        Assert.Contains("Unknown platform", ex.Message);
    }

    /// <summary>Sprawdza kluczowe limity i mozliwosci dla reprezentatywnych profili z YAML.</summary>
    [Theory]
    [InlineData("@embedded", "esp32", 532480L, 32, true, true)]
    [InlineData("@embedded", "esp32c3", 409600L, 16, false, true)]
    [InlineData("@embedded", "esp32s3", 524288L, 32, true, true)]
    [InlineData("@embedded", "stm32f103", 20480L, 4, false, false)]
    [InlineData("@embedded", "rp2040", 270336L, 16, true, false)]
    [InlineData("@server", "linux", 34359738368L, 100000, true, true)]
    [InlineData("@report", "browser", 2147483648L, 100, true, true)]
    public void LoadYaml_VerifyCoreLimits(string platform, string variant, long expectedMemory, int expectedTasks, bool expectedThreading, bool expectedNetworking)
    {
        var profile = PlatformProfiles.GetProfile(platform, variant);

        Assert.Equal(expectedMemory, profile.MaxMemoryBytes);
        Assert.Equal(expectedTasks, profile.MaxConcurrentTasks);
        Assert.Equal(expectedThreading, profile.SupportsThreading);
        Assert.Equal(expectedNetworking, profile.SupportsNetworking);
    }

    /// <summary>Sprawdza, ze ogolny profil embedded eksponuje swoje ograniczenie ostrzegawcze.</summary>
    [Fact]
    public void LoadYaml_GenericEmbedded_HasConstraintWarning()
    {
        var profile = PlatformProfiles.GetProfile("@embedded", "generic");

        Assert.NotEmpty(profile.Constraints);
        var constraint = profile.Constraints.First();
        Assert.Equal("GENERIC_PROFILE", constraint.Code);
        Assert.Equal("warning", constraint.Severity);
    }

    /// <summary>Sprawdza, ze profil przegladarkowy zachowuje oczekiwane ograniczenia.</summary>
    [Fact]
    public void LoadYaml_BrowserReport_HasExpectedConstraints()
    {
        var profile = PlatformProfiles.GetProfile("@report", "browser");

        Assert.Equal(3, profile.Constraints.Count);
        Assert.Contains(profile.Constraints, constraint => constraint.Code == "NO_FILE_IO");
        Assert.Contains(profile.Constraints, constraint => constraint.Code == "CORS");
        Assert.Contains(profile.Constraints, constraint => constraint.Code == "MEMORY_LIMIT");
    }
}
