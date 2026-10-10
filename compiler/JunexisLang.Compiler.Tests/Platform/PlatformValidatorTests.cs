using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Platform;

namespace JunexisLang.Compiler.Tests.Platform;

/// <summary>Wspolne helpery dla testow walidacji platform.</summary>
public sealed partial class PlatformValidatorTests
{
    private static string ProfilesRoot => Path.Combine(Directory.GetCurrentDirectory(), "profiles", "embedded");

    private static void WriteProfile(string variant, string content)
    {
        Directory.CreateDirectory(ProfilesRoot);
        File.WriteAllText(Path.Combine(ProfilesRoot, variant + ".yaml"), content);
    }

    private static void DeleteProfile(string variant)
    {
        var path = Path.Combine(ProfilesRoot, variant + ".yaml");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        PlatformProfiles.ClearCache();
    }
}