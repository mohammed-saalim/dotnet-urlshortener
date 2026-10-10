using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests.ErrorPipeline;

// Production environment: no Developer Exception Page, so this is the pipeline a real client sees.
public sealed class ProductionErrorPipelineTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task MalformedJsonIsBadRequestProblem()
    {
        using var factory = new ApiFactory(fixture.ConnectionString) { EnvironmentName = "Production" };
        using var client = factory.CreateClient();

        using var content = new StringContent("{not json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/links", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownRouteIsNotFoundProblem()
    {
        using var factory = new ApiFactory(fixture.ConnectionString) { EnvironmentName = "Production" };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/x");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnhandledExceptionIsProblemWithoutStackTrace()
    {
        await using (var db = fixture.CreateDbContext())
        {
            db.Links.Add(new Link { Code = "Clash03", OriginalUrl = "https://example.com/", CreatedAt = ApiFactory.Now });
            await db.SaveChangesAsync();
        }

        var codes = Enumerable.Repeat("Clash03", CreateLinkEndpoint.MaxGeneratedCodeAttempts).ToArray();
        using var factory = new ApiFactory(fixture.ConnectionString, codes) { EnvironmentName = "Production" };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/links", new { url = "https://example.com/" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<ProblemDetails>());
    }
}
