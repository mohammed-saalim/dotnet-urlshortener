using UrlShortener.Api.Links;

namespace UrlShortener.UnitTests.Links;

public sealed class CreateLinkValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("HTTPS://Example.com/")]
    public void AcceptsAbsoluteHttpAndHttpsUrls(string url)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest(url), Now);

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
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest(url), Now);

        Assert.Equal(["url"], errors.Keys);
    }

    [Fact]
    public void AcceptsUrlOfExactlyMaxLength()
    {
        var url = UrlOfLength(CreateLinkValidator.MaxUrlLength);

        Assert.Empty(CreateLinkValidator.Validate(new CreateLinkRequest(url), Now));
    }

    [Fact]
    public void RejectsUrlLongerThanMaxLength()
    {
        var url = UrlOfLength(CreateLinkValidator.MaxUrlLength + 1);

        Assert.Equal(["url"], CreateLinkValidator.Validate(new CreateLinkRequest(url), Now).Keys);
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("Ab3dEf7hIj")]
    [InlineData("0000")]
    public void AcceptsBase62AliasOfFourToTenCharacters(string alias)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest("https://example.com/", alias), Now);

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
    public void RejectsInvalidOrReservedAlias(string alias)
    {
        var errors = CreateLinkValidator.Validate(new CreateLinkRequest("https://example.com/", alias), Now);

        Assert.Equal(["customAlias"], errors.Keys);
    }

    [Fact]
    public void AcceptsExpiryInTheFuture()
    {
        var request = new CreateLinkRequest("https://example.com/", ExpiresAt: Now.AddSeconds(1));

        Assert.Empty(CreateLinkValidator.Validate(request, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsExpiryNowOrInThePast(int offsetSeconds)
    {
        var request = new CreateLinkRequest("https://example.com/", ExpiresAt: Now.AddSeconds(offsetSeconds));

        Assert.Equal(["expiresAt"], CreateLinkValidator.Validate(request, Now).Keys);
    }

    [Fact]
    public void ReportsEveryInvalidField()
    {
        var request = new CreateLinkRequest("ftp://x", "a", Now.AddDays(-1));

        var errors = CreateLinkValidator.Validate(request, Now);

        Assert.Equal(["customAlias", "expiresAt", "url"], errors.Keys.Order(StringComparer.Ordinal));
    }

    private static string UrlOfLength(int length)
    {
        const string prefix = "https://example.com/";
        return prefix + new string('a', length - prefix.Length);
    }
}
