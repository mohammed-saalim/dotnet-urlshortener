using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Analytics;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests.Analytics;

// Clicks are inserted directly, so nothing waits for the writer. The database is shared across the class, so every
// test uses its own codes.
public sealed class LinkStatsTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(ApiFactory.Now.UtcDateTime);

    [Fact]
    public async Task StatsAggregatesClicksByDayAndReferrer()
    {
        var startOfToday = new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var linkId = await InsertLinkAsync("Stats01");
        await InsertClicksAsync(
            linkId,
            (ApiFactory.Now, "a.example"),
            (startOfToday, "a.example"), // first instant of today (UTC)
            (ApiFactory.Now.AddHours(-1), null), // direct visit: counted, but not a referrer
            (startOfToday.AddTicks(-1), "a.example"), // last instant of yesterday
            (ApiFactory.Now.AddDays(-1), "b.example"),
            (ApiFactory.Now.AddDays(-8), "b.example")); // outside the 7 days, still in the total and the referrers
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/links/Stats01/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = await response.Content.ReadFromJsonAsync<LinkStatsResponse>();
        Assert.NotNull(stats);
        Assert.Equal("Stats01", stats.Code);
        Assert.Equal(6, stats.TotalClicks);
        Assert.Equal(
            [
                new DailyClicks(Today.AddDays(-6), 0),
                new DailyClicks(Today.AddDays(-5), 0),
                new DailyClicks(Today.AddDays(-4), 0),
                new DailyClicks(Today.AddDays(-3), 0),
                new DailyClicks(Today.AddDays(-2), 0),
                new DailyClicks(Today.AddDays(-1), 2),
                new DailyClicks(Today, 3),
            ],
            stats.ByDay);
        Assert.Equal([new ReferrerClicks("a.example", 3), new ReferrerClicks("b.example", 2)], stats.TopReferrers);
    }

    [Fact]
    public async Task StatsKeepsTopFiveReferrersByCountThenHost()
    {
        var linkId = await InsertLinkAsync("Stats02");
        await InsertClicksAsync(
            linkId,
            (ApiFactory.Now, "f.example"),
            (ApiFactory.Now, "f.example"),
            (ApiFactory.Now, "e.example"),
            (ApiFactory.Now, "d.example"),
            (ApiFactory.Now, "c.example"),
            (ApiFactory.Now, "b.example"),
            (ApiFactory.Now, "a.example"));
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var stats = await client.GetFromJsonAsync<LinkStatsResponse>("/api/links/Stats02/stats");

        Assert.NotNull(stats);
        Assert.Equal(
            [
                new ReferrerClicks("f.example", 2),
                new ReferrerClicks("a.example", 1),
                new ReferrerClicks("b.example", 1),
                new ReferrerClicks("c.example", 1),
                new ReferrerClicks("d.example", 1),
            ],
            stats.TopReferrers);
    }

    [Fact]
    public async Task StatsForLinkWithoutClicksIsZeroFilled()
    {
        await InsertLinkAsync("Stats03");
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/links/Stats03/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = await response.Content.ReadFromJsonAsync<LinkStatsResponse>();
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalClicks);
        Assert.Equal(Enumerable.Range(-6, 7).Select(i => new DailyClicks(Today.AddDays(i), 0)), stats.ByDay);
        Assert.Empty(stats.TopReferrers);
    }

    [Fact]
    public async Task StatsForUnknownCodeIsNotFound()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/links/Nope404/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ADR-0007: stats still answer for an expired link; only the redirect treats it as gone.
    [Fact]
    public async Task StatsForExpiredLinkAreReturned()
    {
        var linkId = await InsertLinkAsync("Stats05", expiresAt: ApiFactory.Now.AddDays(-1));
        await InsertClicksAsync(linkId, (ApiFactory.Now.AddDays(-2), null));
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/links/Stats05/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<LinkStatsResponse>())?.TotalClicks);
    }

    [Fact]
    public async Task StatsForDeletedLinkIsNotFound()
    {
        var linkId = await InsertLinkAsync("Stats04", deletedAt: ApiFactory.Now.AddHours(-1));
        await InsertClicksAsync(linkId, (ApiFactory.Now.AddHours(-2), "a.example")); // rows are kept, not reported
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/links/Stats04/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<long> InsertLinkAsync(
        string code, DateTimeOffset? deletedAt = null, DateTimeOffset? expiresAt = null)
    {
        await using var db = fixture.CreateDbContext();
        var link = new Link
        {
            Code = code,
            OriginalUrl = "https://example.com/",
            CreatedAt = ApiFactory.Now,
            DeletedAt = deletedAt,
            ExpiresAt = expiresAt,
        };
        db.Links.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }

    private async Task InsertClicksAsync(long linkId, params (DateTimeOffset OccurredAt, string? ReferrerHost)[] clicks)
    {
        await using var db = fixture.CreateDbContext();
        db.Clicks.AddRange(clicks.Select(c => new Click { LinkId = linkId, OccurredAt = c.OccurredAt, ReferrerHost = c.ReferrerHost }));
        await db.SaveChangesAsync();
    }
}
