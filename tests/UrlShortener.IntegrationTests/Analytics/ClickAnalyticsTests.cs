using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UrlShortener.Api.Analytics;
using UrlShortener.Api.Links;
using UrlShortener.Api.Telemetry;

namespace UrlShortener.IntegrationTests.Analytics;

// One class for all click tests: each test class starts its own SQL Server container (issue #21).
// The database is shared across the class, so every assertion is scoped to the test's own link.
public sealed class ClickAnalyticsTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RedirectEnqueuesClickAndWriterPersistsIt()
    {
        var linkId = await InsertLinkAsync("Click01");
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Click01");
        request.Headers.TryAddWithoutValidation("Referer", "https://news.example.com/article?x=1");
        request.Headers.TryAddWithoutValidation("User-Agent", new string('a', 300));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var click = Assert.Single(await PollClicksAsync(linkId, expected: 1));
        Assert.Equal(ApiFactory.Now, click.OccurredAt);
        Assert.Equal("news.example.com", click.ReferrerHost); // host only: path and query can carry personal data
        Assert.Equal(new string('a', 256), click.UserAgent);
    }

    [Fact]
    public async Task RedirectStillRedirectsWhenQueueIsFull()
    {
        await InsertLinkAsync("Click02");
        // No writer: the first click fills the one slot and stays there, so the next two must be dropped.
        using var factory = new ApiFactory(fixture.ConnectionString) { ClickQueueCapacity = 1, ClickWriterEnabled = false };
        using var client = factory.CreateClient(NoRedirects);
        var queue = factory.Services.GetRequiredService<ClickQueue>();

        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("/Click02");

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }

        // Exactly 2: a drop is counted once, not by both the itemDropped callback and TryWrite's return value.
        Assert.Equal(2, queue.Dropped);
    }

    [Fact]
    public async Task DroppedClicksAreObservable()
    {
        await InsertLinkAsync("Click07");
        // Same setup as the queue-full test: one slot, no writer, so the second and third redirects are dropped.
        using var factory = new ApiFactory(fixture.ConnectionString) { ClickQueueCapacity = 1, ClickWriterEnabled = false };
        using var client = factory.CreateClient(NoRedirects);
        // Only this host's meter: MeterListener is process-wide and other test hosts create a meter of the same name.
        // AppMetrics itself is not resolved here, so the test fails if the app does not create the meter at startup.
        var meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        long? observed = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, meterFactory)
                    && instrument.Meter.Name == AppMetrics.MeterName
                    && instrument.Name == "urlshortener.analytics.dropped_clicks")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => observed = value);
        listener.Start();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync("/Click07")).StatusCode);
        }

        listener.RecordObservableInstruments();

        Assert.Equal(2, observed);
    }

    [Fact]
    public async Task ShutdownDrainsPendingClicks()
    {
        // Written straight to the queue and then shut down at once: more than two batches of 100 are still queued
        // when StopAsync runs, so the test fails if the writer stops reading when shutdown begins.
        const int Pending = 250;
        var linkId = await InsertLinkAsync("Click03");
        await using var factory = new ApiFactory(fixture.ConnectionString); // also disposed if an assertion fails
        var queue = factory.Services.GetRequiredService<ClickQueue>();
        for (var i = 0; i < Pending; i++)
        {
            Assert.True(queue.TryWrite(new ClickEvent(linkId, ApiFactory.Now, null, null)));
        }

        await factory.DisposeAsync(); // explicit: this is the shutdown under test (a second dispose is a no-op)

        await using var db = fixture.CreateDbContext();
        Assert.Equal(Pending, await db.Clicks.CountAsync(c => c.LinkId == linkId));
    }

    [Fact]
    public async Task WriterSurvivesAFailedBatch()
    {
        var linkId = await InsertLinkAsync("Click04");
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);
        var queue = factory.Services.GetRequiredService<ClickQueue>();
        var writer = factory.Services.GetServices<IHostedService>().OfType<ClickWriterService>().Single();

        // No such link: the insert violates the foreign key and the whole batch fails.
        Assert.True(queue.TryWrite(new ClickEvent(-1, ApiFactory.Now, null, null)));
        // Wait for that batch to fail before the valid click is queued, so the two cannot share a batch.
        await PollAsync(() => Task.FromResult(writer.FailedBatches == 1));

        var redirect = await client.GetAsync("/Click04");

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Single(await PollClicksAsync(linkId, expected: 1));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    // Deleted and expired links answer 410 since #38 (deleted was 404); neither records a click (ADR-0007).
    [Fact]
    public async Task MissOrGoneLinkDoesNotRecordAClick()
    {
        var deletedId = await InsertLinkAsync("Click05", deletedAt: ApiFactory.Now.AddHours(-1));
        var expiredId = await InsertLinkAsync("Click08", expiresAt: ApiFactory.Now.AddHours(-1));
        var liveId = await InsertLinkAsync("Click06");
        int before;
        await using (var db = fixture.CreateDbContext())
        {
            before = await db.Clicks.CountAsync();
        }

        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(NoRedirects);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Unknown05")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync("/Click05")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync("/Click08")).StatusCode);
        // A hit after the misses: the queue is FIFO, so once its click is stored any click from a miss would be too.
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync("/Click06")).StatusCode);
        await PollClicksAsync(liveId, expected: 1);

        await using var after = fixture.CreateDbContext();
        Assert.Equal(before + 1, await after.Clicks.CountAsync());
        Assert.False(await after.Clicks.AnyAsync(c => c.LinkId == deletedId || c.LinkId == expiredId));
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

    // Clicks are written asynchronously, so poll instead of asserting straight after the redirect.
    private async Task<List<Click>> PollClicksAsync(long linkId, int expected)
    {
        List<Click> clicks = [];
        await PollAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            clicks = await db.Clicks.AsNoTracking().Where(c => c.LinkId == linkId).ToListAsync();
            return clicks.Count >= expected;
        });
        return clicks;
    }

    private static async Task PollAsync(Func<Task<bool>> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(elapsed.Elapsed < PollTimeout, $"Condition not met within {PollTimeout.TotalSeconds} s.");
            await Task.Delay(50);
        }
    }
}
