using System.Globalization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using Scalar.AspNetCore;
using UrlShortener.Api.Analytics;
using UrlShortener.Api.Data;
using UrlShortener.Api.Links;
using UrlShortener.Api.PublicUrl;
using UrlShortener.Api.RateLimiting;
using UrlShortener.Api.Redirect;
using UrlShortener.Api.Safety;
using UrlShortener.Api.Telemetry;

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

// Destination reputation (ADR-0007): a host denylist behind a seam; an external service would replace the class.
// ValidateDataAnnotations has no attributes to check today; registered the same way as every other options class.
builder.Services.AddOptions<SafetyOptions>().BindConfiguration(SafetyOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<IUrlReputationChecker, DenylistReputationChecker>();

// Startup migration switch (ADR-0006).
builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Operational telemetry (issue #28, ADR-0006): requests, failures, SQL dependencies and metrics to Application Insights.
// Opt-in: with no connection string (local runs, every test) the OpenTelemetry SDK is not registered at all; the
// exporter would otherwise throw "A connection string was not found". The meter exists either way, so it is testable.
builder.Services.AddSingleton<AppMetrics>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    // The distro collects Microsoft.AspNetCore.Hosting and System.Net.Http only; add the app's meter and the built-in
    // rate-limiting meter (aspnetcore.rate_limiting.requests, tagged with policy and result).
    builder.Services.AddOpenTelemetry().UseAzureMonitor()
        .WithMetrics(metrics => metrics.AddMeter(AppMetrics.MeterName, "Microsoft.AspNetCore.RateLimiting"));

    // Liveness probes would otherwise be most of the request rows on an idle app (cost against the daily cap, noise in
    // Performance/Failures). The request-duration metric still counts them.
    builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        options.Filter = context => !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase));
}

var app = builder.Build();

// Nothing else depends on AppMetrics, so create it now: an observable instrument only exists once its meter does.
_ = app.Services.GetRequiredService<AppMetrics>();

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
}

// Development (`docker compose up -d` + `dotnet run`) and the single-replica Azure deployment migrate here; the default
// is off. Runs before the app listens, so no request sees an old schema (ADR-0006).
if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
{
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
