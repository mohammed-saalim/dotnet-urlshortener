using Microsoft.Extensions.Options;

namespace UrlShortener.Api.Safety;

/// <summary>
/// Is this destination known to be malicious (ADR-0007 threat 1)? Called on create, after validation. Async so an
/// external reputation service can replace the denylist without changing the caller.
/// </summary>
public interface IUrlReputationChecker
{
    Task<bool> IsDeniedAsync(Uri destination, CancellationToken cancellationToken);
}

/// <summary>The default checker: the configured host denylist, no network call.</summary>
public sealed class DenylistReputationChecker(IOptions<SafetyOptions> options) : IUrlReputationChecker
{
    // Exact host or any subdomain; a trailing dot names the same host, so it is trimmed on both sides.
    public Task<bool> IsDeniedAsync(Uri destination, CancellationToken cancellationToken)
    {
        var host = destination.IdnHost.TrimEnd('.');
        return Task.FromResult(options.Value.DeniedHosts
            .Select(denied => denied.TrimEnd('.'))
            .Any(denied => host.Equals(denied, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + denied, StringComparison.OrdinalIgnoreCase)));
    }
}
