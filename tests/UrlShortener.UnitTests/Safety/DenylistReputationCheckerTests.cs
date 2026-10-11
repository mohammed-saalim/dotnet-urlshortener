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

    // Startup validation of the entries: anything here would start the app and then never match (code review of #38).
    [Theory]
    [InlineData("*.evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("evil.example:443")]
    [InlineData("evil.example/path")]
    [InlineData("bücher.example")]
    [InlineData(" ")]
    public void RejectsEntriesThatCouldNeverMatch(string entry)
    {
        Assert.False(SafetyOptions.IsValidHost(entry));
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example.")]
    [InlineData("xn--bcher-kva.example")]
    [InlineData("203.0.113.7")]
    public void AcceptsPlainHostEntries(string entry)
    {
        Assert.True(SafetyOptions.IsValidHost(entry));
    }
}
