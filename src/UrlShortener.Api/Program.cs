using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

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

// Outside Development only: in Development the Developer Exception Page (added by WebApplication) handles exceptions
// and keeps BadHttpRequestException's 400; an unconditional UseExceptionHandler sits inside it and turns that into 500.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseHttpsRedirection();

app.MapGroup("/api/links").MapPost("/", CreateLinkEndpoint.HandleAsync);
app.MapGet(RedirectEndpoint.Route, RedirectEndpoint.HandleAsync);

app.Run();

// Exposes the implicit Program class to WebApplicationFactory in the integration tests.
public partial class Program;
