using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Links;
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

var app = builder.Build();

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

var links = app.MapGroup("/api/links");
links.MapPost("/", CreateLinkEndpoint.HandleAsync).WithName("CreateLink").WithSummary("Create a short link.");
links.MapGet("/{code}", GetLinkEndpoint.HandleAsync).WithName("GetLink").WithSummary("Get a short link's metadata.");
links.MapDelete("/{code}", DeleteLinkEndpoint.HandleAsync).WithName("DeleteLink").WithSummary("Soft-delete a short link.");
app.MapGet(RedirectEndpoint.Route, RedirectEndpoint.HandleAsync)
    .WithName("RedirectToLink").WithSummary("Redirect to the link's destination.");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

app.Run();

// Exposes the implicit Program class to WebApplicationFactory in the integration tests.
public partial class Program;
