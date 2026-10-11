using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Data;
using UrlShortener.Api.PublicUrl;
using UrlShortener.Api.Safety;

namespace UrlShortener.Api.Links;

public sealed record CreateLinkRequest(string? Url, string? CustomAlias = null, DateTimeOffset? ExpiresAt = null);

public sealed record LinkResponse(
    string Code, string ShortUrl, string Url, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public static partial class CreateLinkEndpoint
{
    // A collision is rare (62^7 codes) and five in a row means something is broken, so stop and surface a 500.
    public const int MaxGeneratedCodeAttempts = 5;

    public static async Task<Results<Created<LinkResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        CreateLinkRequest request,
        LinksDbContext db,
        GenerateShortCode generateCode,
        TimeProvider timeProvider,
        IOptions<PublicUrlOptions> publicUrl,
        IUrlReputationChecker reputation,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var errors = CreateLinkValidator.Validate(request, now, new Uri(publicUrl.Value.BaseUrl).Host);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // After validation, so the checker only sees well-formed http(s) URLs (ADR-0007 threat 1).
        if (await reputation.IsDeniedAsync(new Uri(request.Url!), cancellationToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["Is not allowed."] });
        }

        var link = new Link
        {
            Code = request.CustomAlias ?? generateCode(),
            OriginalUrl = request.Url!, // non-null: validated above
            IsCustomAlias = request.CustomAlias is not null,
            CreatedAt = now,
            ExpiresAt = request.ExpiresAt,
        };
        db.Links.Add(link);

        // Insert and let the unique index decide; never check-then-insert (two requests can pass the check together).
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                if (link.IsCustomAlias)
                {
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status409Conflict, title: "The custom alias is already in use.");
                }

                if (attempt == MaxGeneratedCodeAttempts)
                {
                    throw new InvalidOperationException(
                        $"No unique short code after {MaxGeneratedCodeAttempts} attempts.", ex);
                }

                link.Code = generateCode(); // the entity is still in the Added state, so the next save retries the insert
            }
        }

        // Host only: the full URL can carry tokens or personal data in its path and query.
        // IsEnabled guard: CA1873 (Release build) flags the Uri parse as wasted work when Information is off.
        var logger = loggerFactory.CreateLogger(typeof(CreateLinkEndpoint));
        if (logger.IsEnabled(LogLevel.Information))
        {
            var host = new Uri(link.OriginalUrl).Host; // a local: CA1873 inspects log arguments, not the guard
            LogCreated(logger, link.Code, host);
        }

        var body = new LinkResponse(
            link.Code,
            publicUrl.Value.ShortUrl(link.Code),
            link.OriginalUrl,
            link.CreatedAt,
            link.ExpiresAt);
        return TypedResults.Created($"/api/links/{link.Code}", body);
    }

    // Source-generated: CA1848 (an error under latest-recommended + TreatWarningsAsErrors) rejects LogInformation(...).
    [LoggerMessage(Level = LogLevel.Information, Message = "Created short link {Code} for host {Host}")]
    private static partial void LogCreated(ILogger logger, string code, string host);

    // 2601: duplicate key in a unique index; 2627: unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
