using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
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

app.UseHttpsRedirection();

app.Run();

// Exposes the implicit Program class to WebApplicationFactory in the integration tests.
public partial class Program;
