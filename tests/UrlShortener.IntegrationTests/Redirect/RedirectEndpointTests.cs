using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests.Redirect;

// Each test uses its own codes: the database is shared across the class and test order is not guaranteed.
public sealed class RedirectEndpointTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task KnownCodeRedirectsWithFoundAndNoStore()
    {
        await InsertLinkAsync("Go4Known", "https://example.com/landing?ref=a%20b");
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        var response = await client.GetAsync("/Go4Known");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/landing?ref=a%20b", response.Headers.Location?.OriginalString);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
    }

    [Fact]
    public async Task CodeDifferingOnlyInCaseIsNotFound()
    {
        await InsertLinkAsync("CaseTst4", "https://example.com/");
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        var response = await client.GetAsync("/casetst4");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownCodeIsNotFoundProblem()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        var response = await client.GetAsync("/Unknown4");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ADR-0007: deleted answers 410 like expired (was 404 until #38): the code existed and is permanently gone.
    [Fact]
    public async Task SoftDeletedCodeIsGoneWithNoStore()
    {
        await InsertLinkAsync("Deleted4", "https://example.com/", deletedAt: ApiFactory.Now.AddHours(-1));
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        var response = await client.GetAsync("/Deleted4");

        AssertGoneProblemWithNoStore(response);
    }

    // ExpiresAt == now is expired: the same boundary the create validator uses for "must be in the future".
    [Fact]
    public async Task CodeExpiringNowIsGoneWithNoStore()
    {
        await InsertLinkAsync("Expired4", "https://example.com/", expiresAt: ApiFactory.Now);
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        var response = await client.GetAsync("/Expired4");

        AssertGoneProblemWithNoStore(response);
    }

    [Fact]
    public async Task CodeExpiringAfterNowStillRedirects()
    {
        await InsertLinkAsync("NotYet4", "https://example.com/", expiresAt: ApiFactory.Now.AddSeconds(1));
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        var response = await client.GetAsync("/NotYet4");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task RedirectRouteDoesNotShadowApiOpenApiHealthOrScalar()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        var created = await client.PostAsJsonAsync("/api/links", new { url = "https://example.com/" });
        var openApi = await client.GetAsync("/openapi/v1.json");
        var health = await client.GetAsync("/health/live");
        // One segment, so it matches the code pattern; the literal route wins and Scalar adds its trailing slash.
        var scalar = await client.GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("scalar/", scalar.Headers.Location?.OriginalString);
    }

    // 410 is cacheable by default (RFC 9110 15.5.11); no-store keeps a browser or proxy from remembering it.
    private static void AssertGoneProblemWithNoStore(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private async Task InsertLinkAsync(
        string code, string url, DateTimeOffset? deletedAt = null, DateTimeOffset? expiresAt = null)
    {
        await using var db = fixture.CreateDbContext();
        db.Links.Add(new Link
        {
            Code = code,
            OriginalUrl = url,
            CreatedAt = ApiFactory.Now,
            DeletedAt = deletedAt,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
    }
}
