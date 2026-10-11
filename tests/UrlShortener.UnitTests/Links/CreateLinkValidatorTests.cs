using UrlShortener.Api.Links;

namespace UrlShortener.UnitTests.Links;

public sealed class CreateLinkValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private const string PublicHost = "short.example";

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("HTTPS://Example.com/")]
    public void AcceptsAbsoluteHttpAndHttpsUrls(string url)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://x")]
    [InlineData("relative/path")]
    [InlineData("/rooted/path")]
    [InlineData("https://user:pw@example.com/")]
    [InlineData("https://user@example.com/")]
    [InlineData("https://")]
    [InlineData(" https://example.com/")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com/a\r\nSet-Cookie: x=1")]
    [InlineData("https://example.com/\t")]
    [InlineData("https://example.com/café")]
    [InlineData("https://例子.测试/")]
    [InlineData("https://example.com/ ")]
    public void RejectsUnsafeOrMalformedUrls(string? url)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost);

        Assert.Equal(["url"], errors.Keys);
    }

    [Fact]
    public void AcceptsUrlOfExactlyMaxLength()
    {
        var url = UrlOfLength(CreateLinkValidator.MaxUrlLength);

        Assert.Empty(CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost));
    }

    [Fact]
    public void RejectsUrlLongerThanMaxLength()
    {
        var url = UrlOfLength(CreateLinkValidator.MaxUrlLength + 1);

        Assert.Equal(["url"], CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost).Keys);
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("Ab3dEf7hIj")]
    [InlineData("0000")]
    public void AcceptsBase62AliasOfFourToTenCharacters(string alias)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest("https://example.com/", alias), Now, PublicHost);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("abcdefghijk")]
    [InlineData("abc-def")]
    [InlineData("abc_def")]
    [InlineData("abc def")]
    [InlineData("cafés")]
    [InlineData("api")]
    [InlineData("health")]
    [InlineData("HEALTH")]
    [InlineData("OpenApi")]
    [InlineData("scalar")]
    [InlineData("Admin")]
    [InlineData("docs")]
    [InlineData("Login")]
    [InlineData("logout")]
    [InlineData("static")]
    [InlineData("assets")]
    [InlineData("status")]
    [InlineData("metrics")]
    [InlineData("swagger")]
    public void RejectsInvalidOrReservedAlias(string alias)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest("https://example.com/", alias), Now, PublicHost);

        Assert.Equal(["customAlias"], errors.Keys);
    }

    [Fact]
    public void AcceptsExpiryInTheFuture()
    {
        var request = new CreateLinkRequest("https://example.com/", ExpiresAt: Now.AddSeconds(1));

        Assert.Empty(CreateLinkValidator.Validate(request, Now, PublicHost));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsExpiryNowOrInThePast(int offsetSeconds)
    {
        var request = new CreateLinkRequest("https://example.com/", ExpiresAt: Now.AddSeconds(offsetSeconds));

        Assert.Equal(["expiresAt"], CreateLinkValidator.Validate(request, Now, PublicHost).Keys);
    }

    [Fact]
    public void ReportsEveryInvalidField()
    {
        var request = new CreateLinkRequest("ftp://x", "a", Now.AddDays(-1));

        var errors = CreateLinkValidator.Validate(request, Now, PublicHost);

        Assert.Equal(["customAlias", "expiresAt", "url"], errors.Keys.Order(StringComparer.Ordinal));
    }

    // ADR-0007 threat 2: an IP literal on the clicker's own network (router page, localhost service, cloud metadata).
    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.255.255.254:8080/admin")]
    [InlineData("http://[::1]/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.255/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[fd12:3456::1]/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fe80::1%25eth0]/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://0.1.2.3/")]
    [InlineData("http://[::]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[::ffff:169.254.169.254]/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0x7f.1/")]
    [InlineData("http://127.1/")]
    public void RejectsLoopbackPrivateLinkLocalAndUnspecifiedIpLiterals(string url)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost);

        Assert.Equal(["url"], errors.Keys);
    }

    // System.Uri classifies an IPv4 literal with a trailing dot as a DNS name, while browsers (WHATWG URL) drop the dot
    // and go to the address. Found by the security review of #38. A zone ID must not hide loopback either.
    [Theory]
    [InlineData("http://127.0.0.1./")]
    [InlineData("http://169.254.169.254./latest/meta-data/")]
    [InlineData("http://192.168.1.1.:8080/admin")]
    [InlineData("http://127.1./")]
    [InlineData("http://2130706433./")]
    [InlineData("http://0x7f.0.0.1./")]
    [InlineData("http://0./")]
    [InlineData("http://[::1%251]/")]
    public void RejectsIpLiteralsHiddenByTrailingDotOrZoneId(string url)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost);

        Assert.Equal(["url"], errors.Keys);
    }

    // The range edges, so a rule written one octet too wide fails here.
    [Theory]
    [InlineData("http://8.8.8.8/")]
    [InlineData("http://9.255.255.255/")]
    [InlineData("http://11.0.0.0/")]
    [InlineData("http://126.255.255.255/")]
    [InlineData("http://128.0.0.0/")]
    [InlineData("http://172.15.255.255/")]
    [InlineData("http://172.32.0.0/")]
    [InlineData("http://169.253.255.255/")]
    [InlineData("http://169.255.0.0/")]
    [InlineData("http://192.167.255.255/")]
    [InlineData("http://192.169.0.0/")]
    [InlineData("http://1.0.0.0/")]
    [InlineData("http://[2606:4700::1111]/")]
    [InlineData("http://[fe00::1]/")]
    [InlineData("http://[::ffff:8.8.8.8]/")]
    public void AcceptsPublicIpLiterals(string url)
    {
        Assert.Empty(CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost));
    }

    // Pins what System.Uri does with shorthand IPv4 (decimal, hex, two-part): if it normalises them to a dotted quad,
    // the IP rule above sees 127.0.0.1; if not, they reach the validator as DNS names.
    [Theory]
    [InlineData("http://2130706433/")]
    [InlineData("http://0x7f.1/")]
    [InlineData("http://127.1/")]
    public void UriNormalisesShorthandIpv4ToDottedQuad(string url)
    {
        var uri = new Uri(url);

        Assert.Equal(UriHostNameType.IPv4, uri.HostNameType);
        Assert.Equal("127.0.0.1", uri.Host);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST:8080/")]
    [InlineData("http://localhost./")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://a.b.LocalHost./")]
    public void RejectsLocalhostNames(string url)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost);

        Assert.Equal(["url"], errors.Keys);
    }

    [Theory]
    [InlineData("http://localhost.example.com/")]
    [InlineData("http://mylocalhost.com/")]
    [InlineData("http://notlocalhost/")]
    public void AcceptsNamesThatOnlyContainLocalhost(string url)
    {
        Assert.Empty(CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost));
    }

    // ADR-0007 threat 3: a link to the shortener itself (loops, chains). Host only: any port, either scheme.
    [Theory]
    [InlineData("https://short.example/Abc123")]
    [InlineData("http://SHORT.Example:8443/x")]
    [InlineData("https://short.example./x")]
    public void RejectsOwnPublicHost(string url)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost);

        Assert.Equal(["url"], errors.Keys);
    }

    [Fact]
    public void AcceptsSubdomainOfOwnPublicHost()
    {
        // Exact host only: another service under the same domain is not this service.
        Assert.Empty(CreateLinkValidator.Validate(new CreateLinkRequest("https://www.short.example/"), Now, PublicHost));
    }

    [Theory]
    [InlineData("http://10.0.0.1/secret-path")]
    [InlineData("http://secret.localhost/secret-path")]
    [InlineData("https://short.example/secret-path")]
    public void HostRuleMessagesDoNotEchoTheUrl(string url)
    {
        var message = Assert.Single(CreateLinkValidator.Validate(new CreateLinkRequest(url), Now, PublicHost)["url"]);

        Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10.0.0.1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("short.example", message, StringComparison.Ordinal);
    }

    private static string UrlOfLength(int length)
    {
        const string prefix = "https://example.com/";
        return prefix + new string('a', length - prefix.Length);
    }
}
