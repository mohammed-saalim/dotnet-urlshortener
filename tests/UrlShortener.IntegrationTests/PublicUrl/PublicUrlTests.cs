using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests.PublicUrl;

// Links are created through the API (generated codes), so tests never share rows in the class's database.
public sealed class PublicUrlTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task ShortUrlIgnoresClientHostHeader()
    {
        using var factory = new ApiFactory(fixture.ConnectionString); // PublicUrl:BaseUrl = http://localhost
        using var client = factory.CreateClient();

        using var post = new HttpRequestMessage(HttpMethod.Post, "/api/links")
        {
            Content = JsonContent.Create(new { url = "https://example.com/" }),
        };
        post.Headers.Host = "evil.example";
        var created = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = await created.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(link);
        Assert.Equal($"http://localhost/{link.Code}", link.ShortUrl);

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/links/{link.Code}");
        get.Headers.Host = "evil.example";
        var fetched = await (await client.SendAsync(get)).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal($"http://localhost/{link.Code}", fetched?.ShortUrl);
    }

    [Fact]
    public async Task ShortUrlUsesConfiguredBaseUrl()
    {
        using var factory = new ApiFactory(fixture.ConnectionString)
        {
            Settings = { ["PublicUrl:BaseUrl"] = "https://sho.rt" },
        };
        using var client = factory.CreateClient();

        var link = await (await CreateAsync(client)).Content.ReadFromJsonAsync<LinkResponse>();

        Assert.NotNull(link);
        Assert.Equal($"https://sho.rt/{link.Code}", link.ShortUrl);
    }

    [Fact]
    public void MissingBaseUrlFailsStartup()
    {
        // Production: no startup migration, so this test needs no database round trip.
        using var factory = new ApiFactory(fixture.ConnectionString)
        {
            EnvironmentName = "Production",
            Settings = { ["PublicUrl:BaseUrl"] = "" },
        };

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("BaseUrl", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://sho.rt ")] // trailing space: Uri.TryCreate trims it, ShortUrl would not
    [InlineData("https://sho.rt\n")]
    [InlineData("https://u:p@sho.rt")]
    [InlineData("https://sho.rt/s/")]
    [InlineData("ftp://sho.rt")]
    public void MalformedBaseUrlFailsStartup(string baseUrl)
    {
        using var factory = new ApiFactory(fixture.ConnectionString)
        {
            EnvironmentName = "Production",
            Settings = { ["PublicUrl:BaseUrl"] = baseUrl },
        };

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task ProductionRejectsUnknownHost()
    {
        // appsettings.Production.json restricts AllowedHosts to localhost until B6 sets the public host: fail closed.
        using var factory = new ApiFactory(fixture.ConnectionString) { EnvironmentName = "Production" };
        using var client = factory.CreateClient();

        using var evil = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        evil.Headers.Host = "evil.example";

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(evil)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode); // Host: localhost
    }

    [Fact]
    public async Task ForwardedForIsIgnoredByDefault()
    {
        using var factory = CreateRateLimitedFactory(trustForwardedHeaders: false);
        using var client = factory.CreateClient();

        var first = await CreateAsync(client, "203.0.113.1");
        var second = await CreateAsync(client, "203.0.113.2");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode); // one shared partition: the header was ignored
    }

    [Fact]
    public async Task ForwardedForIsHonouredWhenTrusted()
    {
        using var factory = CreateRateLimitedFactory(trustForwardedHeaders: true);
        using var client = factory.CreateClient();

        var first = await CreateAsync(client, "203.0.113.1");
        var second = await CreateAsync(client, "203.0.113.2");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode); // a partition per forwarded client IP
    }

    // A non-loopback peer (standing in for the ingress) is what the default known-proxy lists reject, so the trusted
    // test passes only because Program.cs clears them; with TestServer's null peer the lists are never consulted.
    private ApiFactory CreateRateLimitedFactory(bool trustForwardedHeaders) =>
        new(fixture.ConnectionString)
        {
            RemoteIpAddress = IPAddress.Parse("10.0.0.5"),
            Settings =
            {
                ["RateLimiting:PermitLimit"] = "1",
                ["PublicUrl:TrustForwardedHeaders"] = trustForwardedHeaders ? "true" : "false",
            },
        };

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/links")
        {
            Content = JsonContent.Create(new { url = "https://example.com/" }),
        };
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request);
    }
}
