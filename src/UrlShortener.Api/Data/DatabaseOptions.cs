namespace UrlShortener.Api.Data;

/// <summary>Database startup behaviour (ADR-0006). Section <c>Database</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// True applies pending EF Core migrations before the app starts serving. Default false. Development sets it in
    /// appsettings; Azure sets <c>Database__MigrateOnStartup=true</c> in Bicep. Safe only with a single replica.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}
