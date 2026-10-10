using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
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
        HttpResponse httpResponse,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Expiry is not checked yet: Day 3 decides 404 vs 410 for expired links.
        var originalUrl = await db.Links
            .AsNoTracking()
            .Where(l => l.Code == code && l.DeletedAt == null)
            .Select(l => l.OriginalUrl)
            .SingleOrDefaultAsync(cancellationToken);

        if (originalUrl is null)
        {
            var logger = loggerFactory.CreateLogger(typeof(RedirectEndpoint));
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogMiss(logger, code);
            }
            return TypedResults.NotFound(); // body written by UseStatusCodePages as ProblemDetails
        }

        // 302 + no-store: every visit reaches the service, so later deletes and click counts stay accurate.
        httpResponse.Headers.CacheControl = "private, no-store";
        return TypedResults.Redirect(originalUrl);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No active short link {Code}")]
    private static partial void LogMiss(ILogger logger, string code);
}
