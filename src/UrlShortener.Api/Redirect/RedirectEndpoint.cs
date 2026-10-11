using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Analytics;
using UrlShortener.Api.Data;

namespace UrlShortener.Api.Redirect;

public static partial class RedirectEndpoint
{
    // Routing, not security (the lookup below is parameterised): one segment of the 4-10 Base62 characters a code or
    // alias can have, so /api, multi-segment paths (/api/links, /health/live) and dotted ones (/openapi/v1.json) never
    // match. A single-segment literal route such as /scalar would match the pattern, but literal routes win.
    public const string Route = "/{code:regex(^[A-Za-z0-9]{{4,10}}$)}";

    public static async Task<Results<RedirectHttpResult, NotFound>> HandleAsync(
        string code,
        LinksDbContext db,
        ClickQueue clicks,
        TimeProvider timeProvider,
        HttpRequest httpRequest,
        HttpResponse httpResponse,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Expiry is not checked yet: Day 3 decides 404 vs 410 for expired links.
        var link = await db.Links
            .AsNoTracking()
            .Where(l => l.Code == code && l.DeletedAt == null)
            .Select(l => new { l.Id, l.OriginalUrl })
            .SingleOrDefaultAsync(cancellationToken);

        if (link is null)
        {
            var logger = loggerFactory.CreateLogger(typeof(RedirectEndpoint));
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogMiss(logger, code);
            }
            return TypedResults.NotFound(); // body written by UseStatusCodePages as ProblemDetails
        }

        // Never waits and cannot fail the redirect: a full queue drops the click (ADR-0004).
        clicks.TryWrite(new ClickEvent(
            link.Id, timeProvider.GetUtcNow(), ReferrerHost(httpRequest), UserAgent(httpRequest)));

        // 302 + no-store: every visit reaches the service, so later deletes and click counts stay accurate.
        httpResponse.Headers.CacheControl = "private, no-store";
        return TypedResults.Redirect(link.OriginalUrl);
    }

    // Host only: the referrer's path and query can carry personal data. Too long for the column → null.
    private static string? ReferrerHost(HttpRequest request) =>
        Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host) // "http:///path" parses with an empty host; store null, not ""
        && uri.Host.Length <= Click.ReferrerHostMaxLength
            ? uri.Host
            : null;

    private static string? UserAgent(HttpRequest request)
    {
        var userAgent = request.Headers.UserAgent.ToString();
        return userAgent.Length == 0 ? null : userAgent[..Math.Min(userAgent.Length, Click.UserAgentMaxLength)];
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No active short link {Code}")]
    private static partial void LogMiss(ILogger logger, string code);
}
