using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using UrlShortener.Api.Analytics;
using UrlShortener.Api.Data;
using UrlShortener.Api.Links;
using UrlShortener.Api.PublicUrl;
using UrlShortener.Api.RateLimiting;
using UrlShortener.Api.Redirect;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new GenerateShortCode(ShortCode.Generate));
builder.Services.AddDbContext<LinksDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("UrlShortener"),
    sql => sql.EnableRetryOnFailure()));
// Only /health/ready runs this (tag filter below); /health/live must never open a connection, or probes keep the
// Azure SQL free-offer database from auto-pausing.
builder.Services.AddHealthChecks().AddDbContextCheck<LinksDbContext>(tags: ["ready"]);
// Click analytics (ADR-0004): the redirect enqueues, the hosted service batch-inserts.
builder.Services.AddSingleton(new ClickQueue());
builder.Services.AddHostedService<ClickWriterService>();

// Rate limit on create only (ADR-0005): fixed window per client IP.
builder.Services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    // The default rejection is 503 with no Retry-After (CLAUDE.md section 9).
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests" },
            });
    };
    options.AddPolicy(RateLimitOptions.CreatePolicy, httpContext =>
    {
        var limit = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        // Behind Container Apps ingress every client has the proxy's IP unless PublicUrl:TrustForwardedHeaders is on
        // (#9), so there the limit is effectively global. Null (no connection address) falls back to one shared key.
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
                // AutoReplenishment is not set: GetFixedWindowLimiter forces it to false and replenishes from the
                // partitioned limiter's shared timer (API docs remark).
            });
    });
});

// Public base URL and proxy trust (#9). shortUrl comes from configuration, never from the client's Host header.
// AllowedHosts (host filtering, on by default) is "*" in appsettings.json for local runs; appsettings.Production.json
// sets "localhost" so a deploy that forgets to override it fails closed (400). B6 sets the public host in Bicep.
builder.Services.AddOptions<PublicUrlOptions>().BindConfiguration(PublicUrlOptions.SectionName)
    .Validate(o => PublicUrlOptions.IsValidBaseUrl(o.BaseUrl), PublicUrlOptions.BaseUrlMessage)
    .ValidateOnStart();

var app = builder.Build();

// Forwarded headers (#9, ADR-0005 resolution): first middleware, so the rate limiter, HTTPS redirection and error
// handling see the client's IP and scheme. Off by default: the app trusts no proxy. With TrustForwardedHeaders the
// known-proxy lists are cleared, so the immediate peer is trusted whatever its address: correct behind the Container
// Apps ingress (proxy IPs are not fixed), but anywhere a client can reach the app directly it could spoof its IP and
// scheme. ForwardLimit stays at the default 1: only the rightmost X-Forwarded-For entry, the one the ingress appended,
// is used; entries a client sent itself sit to its left and are ignored.
if (app.Services.GetRequiredService<IOptions<PublicUrlOptions>>().Value.TrustForwardedHeaders)
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    forwarded.KnownIPNetworks.Clear(); // KnownNetworks is obsolete in .NET 10 (ASPDEPR005; CLAUDE.md section 9)
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Development convenience only: `docker compose up -d` + `dotnet run` gives a ready database.
    // Other environments do not migrate at startup (production migration path is an open item, see AI log 002).
    if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("UrlShortener")))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:UrlShortener is not configured. Set it with `dotnet user-secrets set` (see docs/SETUP.md).");
    }

    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LinksDbContext>().Database.MigrateAsync();
}

// ProblemDetails for every error in every environment. The handler sits inside the Developer Exception Page, so it
// also answers in Development. Minimal APIs throw BadHttpRequestException (e.g. malformed JSON) only in Development;
// the selector keeps its 4xx instead of a 500. UseStatusCodePages gives empty 4xx (Production's malformed JSON,
// route misses, TypedResults.NotFound()) a ProblemDetails body.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.UseHttpsRedirection();

// After the HTTPS redirect, so an http:// request that gets a 307 does not also spend a create permit.
app.UseRateLimiter();

var links = app.MapGroup("/api/links");
links.MapPost("/", CreateLinkEndpoint.HandleAsync).WithName("CreateLink").WithSummary("Create a short link.")
    .RequireRateLimiting(RateLimitOptions.CreatePolicy);
links.MapGet("/{code}", GetLinkEndpoint.HandleAsync).WithName("GetLink").WithSummary("Get a short link's metadata.");
links.MapDelete("/{code}", DeleteLinkEndpoint.HandleAsync).WithName("DeleteLink").WithSummary("Soft-delete a short link.");
links.MapGet("/{code}/stats", GetStatsEndpoint.HandleAsync)
    .WithName("GetLinkStats").WithSummary("Click statistics for a short link (at-most-once recording; see docs/TESTING.md).");
app.MapGet(RedirectEndpoint.Route, RedirectEndpoint.HandleAsync)
    .WithName("RedirectToLink").WithSummary("Redirect to the link's destination.");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

app.Run();

// Exposes the implicit Program class to WebApplicationFactory in the integration tests.
public partial class Program;
