using Microsoft.Extensions.Options;
using UrlShortener.Api.Safety;

namespace UrlShortener.UnitTests.Safety;

public sealed class DenylistReputationCheckerTests
{
    private static readonly DenylistReputationChecker Checker =
        new(Options.Create(new SafetyOptions { DeniedHosts = ["evil.example", "Bad.Example."] }));

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("https://EVIL.example:8443/path")]
    [InlineData("https://evil.example./")]
    [InlineData("https://login.evil.example/")]
    [InlineData("https://a.b.evil.example/")]
    [InlineData("https://bad.example/")]
    public async Task DeniesListedHostAndItsSubdomains(string url)
    {
        Assert.True(await Checker.IsDeniedAsync(new Uri(url), CancellationToken.None));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://notevil.example/")]
    [InlineData("https://evil.example.com/")]
    [InlineData("https://example.com/evil.example")]
    public async Task AllowsOtherHosts(string url)
    {
        Assert.False(await Checker.IsDeniedAsync(new Uri(url), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyDenylistAllowsEverything()
    {
        var checker = new DenylistReputationChecker(Options.Create(new SafetyOptions()));

        Assert.False(await checker.IsDeniedAsync(new Uri("https://evil.example/"), CancellationToken.None));
    }
}
