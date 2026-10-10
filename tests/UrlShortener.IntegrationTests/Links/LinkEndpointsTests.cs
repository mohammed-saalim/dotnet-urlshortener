using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests.Links;

// Get and delete. Each test uses its own codes: the database is shared across the class and test order is not guaranteed.
public sealed class LinkEndpointsTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task GetReturnsLinkMetadata()
    {
        var expiresAt = ApiFactory.Now.AddDays(3);
        await InsertLinkAsync("GetMe5", "https://example.com/a?b=c", expiresAt: expiresAt);
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/links/GetMe5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal(
            new LinkResponse("GetMe5", "http://localhost/GetMe5", "https://example.com/a?b=c", ApiFactory.Now, expiresAt),
            body);
    }

    [Fact]
    public async Task GetUnknownCodeIsNotFoundProblem()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/links/Nope55");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DeleteSoftDeletesSoGetAndRedirectAreNotFound()
    {
        await InsertLinkAsync("DelMe5", "https://example.com/");
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var deleted = await client.DeleteAsync("/api/links/DelMe5");
        var get = await client.GetAsync("/api/links/DelMe5");
        var redirect = await client.GetAsync("/DelMe5");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, redirect.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(ApiFactory.Now, db.Links.Single(l => l.Code == "DelMe5").DeletedAt); // row kept, not removed
    }

    [Fact]
    public async Task DeleteTwiceIsNotFound()
    {
        await InsertLinkAsync("DelTwo5", "https://example.com/");
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var first = await client.DeleteAsync("/api/links/DelTwo5");
        var second = await client.DeleteAsync("/api/links/DelTwo5");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateLogsCodeAndHostButNotUrl()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = new ApiFactory(fixture.ConnectionString, "LogMe55");
        using var logged = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(logs)));
        using var client = logged.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/links", new { url = "https://logs.example.com/secret-path?token=abc" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Every category the app logs, including EF Core, so this also shows nothing else writes the URL.
        var all = string.Join('\n', logs.Messages);
        Assert.Contains("Created short link LogMe55 for host logs.example.com", all, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-path", all, StringComparison.Ordinal);
        Assert.DoesNotContain("token=abc", all, StringComparison.Ordinal);
    }

    private async Task InsertLinkAsync(string code, string url, DateTimeOffset? expiresAt = null)
    {
        await using var db = fixture.CreateDbContext();
        db.Links.Add(new Link { Code = code, OriginalUrl = url, CreatedAt = ApiFactory.Now, ExpiresAt = expiresAt });
        await db.SaveChangesAsync();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
