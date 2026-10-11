using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;

namespace UrlShortener.Api.Analytics;

public sealed record LinkStatsResponse(
    string Code, long TotalClicks, IReadOnlyList<DailyClicks> ByDay, IReadOnlyList<ReferrerClicks> TopReferrers);

public sealed record DailyClicks(DateOnly Date, long Clicks);

public sealed record ReferrerClicks(string Host, long Clicks);

public static class GetStatsEndpoint
{
    public const int Days = 7; // including today
    public const int TopReferrerCount = 5;

    public static async Task<Results<Ok<LinkStatsResponse>, NotFound>> HandleAsync(
        string code,
        LinksDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Soft-deleted links are not found, like GET /api/links/{code} and the redirect; their Clicks rows are kept.
        var linkId = await db.Links
            .Where(l => l.Code == code && l.DeletedAt == null)
            .Select(l => (long?)l.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (linkId is not long id)
        {
            return TypedResults.NotFound(); // body written by UseStatusCodePages as ProblemDetails
        }

        var clicks = db.Clicks.Where(c => c.LinkId == id);

        var total = await clicks.LongCountAsync(cancellationToken);

        // Days are UTC. The writer stores OccurredAt from TimeProvider.GetUtcNow() (offset +00:00), so SQL Server's
        // CONVERT(date, OccurredAt), which takes the date in the stored offset, is the UTC date.
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var firstDay = today.AddDays(-(Days - 1));
        var start = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var countsByDay = await clicks
            .Where(c => c.OccurredAt >= start)
            .GroupBy(c => c.OccurredAt.Date)
            .Select(g => new { Day = g.Key, Clicks = g.LongCount() })
            .ToDictionaryAsync(d => DateOnly.FromDateTime(d.Day), d => d.Clicks, cancellationToken);
        var byDay = Enumerable.Range(0, Days)
            .Select(i => firstDay.AddDays(i))
            .Select(day => new DailyClicks(day, countsByDay.GetValueOrDefault(day)))
            .ToList();

        // Null host = no referrer (direct visit) or one that was not an absolute http(s) URL; not a "referrer".
        var topReferrers = await clicks
            .Where(c => c.ReferrerHost != null)
            .GroupBy(c => c.ReferrerHost!)
            .OrderByDescending(g => g.LongCount())
            .ThenBy(g => g.Key)
            .Take(TopReferrerCount)
            .Select(g => new ReferrerClicks(g.Key, g.LongCount()))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new LinkStatsResponse(code, total, byDay, topReferrers));
    }
}
