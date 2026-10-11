using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests.Links;

// Each test uses its own codes/aliases: the database is shared across the class and test order is not guaranteed.
public sealed class CreateLinkEndpointTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task CreatesLinkWithGeneratedCode()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/links", new { url = "https://example.com/path?q=1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal(ShortCode.Length, body.Code.Length);
        Assert.Equal($"/api/links/{body.Code}", response.Headers.Location?.OriginalString);
        Assert.Equal($"http://localhost/{body.Code}", body.ShortUrl);
        Assert.Equal("https://example.com/path?q=1", body.Url);
        Assert.Equal(ApiFactory.Now, body.CreatedAt);
        Assert.Null(body.ExpiresAt);

        await using var db = fixture.CreateDbContext();
        var stored = await db.Links.SingleAsync(l => l.Code == body.Code);
        Assert.False(stored.IsCustomAlias);
        Assert.Equal("https://example.com/path?q=1", stored.OriginalUrl);
    }

    [Fact]
    public async Task CreatesLinkWithCustomAliasAndExpiry()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var expiresAt = ApiFactory.Now.AddDays(7);

        var response = await client.PostAsJsonAsync(
            "/api/links", new { url = "https://example.com/", customAlias = "Launch26", expiresAt });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("Launch26", body.Code);
        Assert.Equal("/api/links/Launch26", response.Headers.Location?.OriginalString);
        Assert.Equal(expiresAt, body.ExpiresAt);

        await using var db = fixture.CreateDbContext();
        Assert.True((await db.Links.SingleAsync(l => l.Code == "Launch26")).IsCustomAlias);
    }

    [Fact]
    public async Task RejectsUnsafeUrlWithFieldError()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/links", new { url = "javascript:alert(1)" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["url"], problem.Errors.Keys);
    }

    // ADR-0007 threats 2 and 3, wired through the endpoint. The base URL is not localhost here, so the own-host
    // rule is shown on its own rather than by the localhost rule.
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://short.example/Abc123")]
    public async Task RejectsLocalNetworkAndOwnHostDestinations(string url)
    {
        using var factory = new ApiFactory(fixture.ConnectionString)
        {
            Settings = { ["PublicUrl:BaseUrl"] = "https://short.example" },
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/links", new { url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["url"], problem.Errors.Keys);
    }

    // ADR-0007 threat 1: the reputation seam's default denylist (Safety:DeniedHosts), subdomains included.
    [Fact]
    public async Task RejectsDeniedHostButCreatesOthers()
    {
        using var factory = new ApiFactory(fixture.ConnectionString)
        {
            Settings = { ["Safety:DeniedHosts:0"] = "evil.example" },
        };
        using var client = factory.CreateClient();

        var denied = await client.PostAsJsonAsync("/api/links", new { url = "https://login.evil.example/reset" });
        var allowed = await client.PostAsJsonAsync("/api/links", new { url = "https://example.com/" });

        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var problem = await denied.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["url"], problem.Errors.Keys);
        Assert.DoesNotContain("evil", string.Concat(problem.Errors["url"]), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task RejectsMalformedJsonWithBadRequest()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var content = new StringContent("{not json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/links", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DuplicateCustomAliasReturnsConflict()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var request = new { url = "https://example.com/", customAlias = "Taken99" };

        var first = await client.PostAsJsonAsync("/api/links", request);
        var second = await client.PostAsJsonAsync("/api/links", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RetriesWithNewCodeWhenGeneratedCodeCollides()
    {
        await InsertLinkAsync("Clash01");
        using var factory = new ApiFactory(fixture.ConnectionString, "Clash01", "Fresh01");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/links", new { url = "https://example.com/" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal("Fresh01", body?.Code);
    }

    [Fact]
    public async Task GivesUpAfterMaxGeneratedCodeCollisions()
    {
        await InsertLinkAsync("Clash02");
        // One spare unique code: if the endpoint retried once too often it would succeed with 201 instead of 500,
        // and the generated-count assertion catches giving up too early.
        var codes = Enumerable.Repeat("Clash02", CreateLinkEndpoint.MaxGeneratedCodeAttempts).Append("Spare01").ToArray();
        using var factory = new ApiFactory(fixture.ConnectionString, codes);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/links", new { url = "https://example.com/" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(CreateLinkEndpoint.MaxGeneratedCodeAttempts, factory.CodesGenerated);
    }

    private async Task InsertLinkAsync(string code)
    {
        await using var db = fixture.CreateDbContext();
        db.Links.Add(new Link { Code = code, OriginalUrl = "https://example.com/", CreatedAt = ApiFactory.Now });
        await db.SaveChangesAsync();
    }
}
