namespace UrlShortener.Api.PublicUrl;

/// <summary>
/// The public address of the service (issue #9). Section <c>PublicUrl</c>. <c>shortUrl</c> is built from
/// <see cref="BaseUrl"/>, never from the request's <c>Host</c>, which the client controls.
/// </summary>
public sealed class PublicUrlOptions
{
    public const string SectionName = "PublicUrl";

    public const string BaseUrlMessage =
        "PublicUrl:BaseUrl must be an absolute http or https URL (printable ASCII) with no user info, path, query or fragment.";

    /// <summary>Required: a missing value fails startup. Development sets it in appsettings; Azure sets it in Bicep (B6).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// True registers forwarded-headers middleware that trusts <c>X-Forwarded-For</c>/<c>-Proto</c> from the immediate
    /// peer, whatever its address. Only safe when every request arrives through a platform ingress (Container Apps);
    /// anywhere else a client could set its own IP (rate-limit partition) and scheme. Default false.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    public string ShortUrl(string code) => $"{BaseUrl.TrimEnd('/')}/{code}";

    // Checked on the raw string, like the destination validator: Uri.TryCreate accepts surrounding whitespace and
    // CR/LF (CLAUDE.md section 9) and ShortUrl joins the string as configured, so a padded value would corrupt every
    // shortUrl. A deployment value with a trailing space fails startup instead.
    public static bool IsValidBaseUrl(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.All(c => c is >= '!' and <= '~')
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0
        && uri.AbsolutePath == "/"
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0;
}
