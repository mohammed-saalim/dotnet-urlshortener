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

    [Fact]
    public async Task SoftDeletedCodeIsNotFound()
    {
        await InsertLinkAsync("Deleted4", "https://example.com/", deletedAt: ApiFactory.Now.AddHours(-1));
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        var response = await client.GetAsync("/Deleted4");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private async Task InsertLinkAsync(string code, string url, DateTimeOffset? deletedAt = null)
    {
        await using var db = fixture.CreateDbContext();
        db.Links.Add(new Link { Code = code, OriginalUrl = url, CreatedAt = ApiFactory.Now, DeletedAt = deletedAt });
        await db.SaveChangesAsync();
    }
}
