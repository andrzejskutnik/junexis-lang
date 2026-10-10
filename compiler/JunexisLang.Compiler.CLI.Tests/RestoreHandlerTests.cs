using JunexisLang.Compiler.CLI.Handlers;
using Xunit;

namespace JunexisLang.Compiler.CLI.Tests;

public sealed class RestoreHandlerTests
{
    [Theory]
    [InlineData("https://registry.example", "https://registry.example/v1/snapshot")]
    [InlineData("https://registry.example/base/", "https://registry.example/base/v1/snapshot")]
    public void RegistryBaseAddress_ResolvesSnapshotEndpoint(string value, string expected)
    {
        Assert.Equal(expected, RestoreHandler.ResolveRegistrySnapshotUri(value)?.AbsoluteUri);
    }

    [Theory]
    [InlineData("registry.example")]
    [InlineData("file:///registry")]
    [InlineData("http://registry.example/")]
    [InlineData("https://user@registry.example/")]
    [InlineData("https://registry.example/?channel=test")]
    [InlineData("https://registry.example/#fragment")]
    public void InvalidRegistryBaseAddress_IsRejected(string value)
    {
        Assert.Throws<InvalidDataException>(() => RestoreHandler.ResolveRegistrySnapshotUri(value));
    }
}
