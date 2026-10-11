# AI log 011 — Public base URL, AllowedHosts and forwarded headers

| Field | Value |
|---|---|
| Issue | #9 |
| Branch | `feat/11-public-base-url` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | high-impact (request trust boundary, infra-facing configuration) |

## Intent
Stop deriving `shortUrl` from the client-controlled `Host` header and make the client IP and scheme correct behind the Container Apps ingress (issue #9).

## Constraints given to the AI
- Minimal: one options class, forwarded-headers middleware, `AllowedHosts` config, `shortUrl` from the option, one test class, this log. No new packages.
- TDD for `ShortUrlIgnoresClientHostHeader` (red first).
- Existing tests unchanged; only `ApiFactory` may supply the new setting.
- `Program.cs`: new blocks only, no reordering (B5 runs in parallel). Do not touch `RedirectEndpoint.cs`, `perf/`, or the Performance section of `docs/TESTING.md`.
- No new ADR (ADR-0006 was then reserved for Day 3; renumbered to ADR-0007 in B6): append a "Resolution (#9)" to ADR-0005.
- Verify every `ForwardedHeadersOptions` detail against the official docs. No commit/push/PR; no `.env`/secrets/`.azure/`.

## Acceptance criteria (runnable)
- Public base URL option validated at startup; `shortUrl` uses it, not the request → `ShortUrlUsesConfiguredBaseUrl`, `MissingBaseUrlFailsStartup`
- `Host: evil.example` no longer changes `shortUrl` → `ShortUrlIgnoresClientHostHeader` (POST and GET)
- `UseForwardedHeaders` first, configurable for ACA, before the rate limiter partitions by IP → `ForwardedForIsIgnoredByDefault`, `ForwardedForIsHonouredWhenTrusted`
- `AllowedHosts` restricted outside Development → `appsettings.Production.json`; the 3 `ProductionErrorPipelineTests` still green
- Existing 86 tests unchanged and green → 91 total below

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #9 per the spec (`PublicUrlOptions` with `BaseUrl` and `TrustForwardedHeaders`, Development default, `ApiFactory` default, endpoints use the option, `UseForwardedHeaders` first and off by default, `AllowedHosts` decision, rate-limit comment, ADR-0005 resolution), the five named tests, this log, `TESTING.md` row; confirm the `KnownNetworks.Clear()` guidance on the official docs page.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `src/UrlShortener.Api/PublicUrl/PublicUrlOptions.cs` | generated | `BaseUrl` `[Required]` `[Url]` plus `IsValidBaseUrl` (absolute, http/https, path `/` only, no query/fragment) wired through `OptionsBuilder.Validate`; `TrustForwardedHeaders` default false; `ShortUrl(code)` joins with exactly one slash |
| `src/UrlShortener.Api/Program.cs` | edited | Block 1 before `builder.Build()`: options binding with validation on start, comment on `AllowedHosts`. Block 2 right after `builder.Build()`: `UseForwardedHeaders` only when trusted, before every other middleware. Rate-limit partition comment now points at `TrustForwardedHeaders`. Two `using`s. No line reordered |
| `src/UrlShortener.Api/Links/CreateLinkEndpoint.cs`, `GetLinkEndpoint.cs` | edited | `HttpRequest` parameter replaced by `IOptions<PublicUrlOptions>`; `shortUrl = publicUrl.Value.ShortUrl(code)`; the "#9 replaces both" comment retired |
| `src/UrlShortener.Api/appsettings.Development.json` | edited | `PublicUrl:BaseUrl = http://localhost:5078` (the `http` profile in `launchSettings.json`, confirmed) |
| `src/UrlShortener.Api/appsettings.Production.json` | generated | `AllowedHosts: "localhost"`, a fail-closed placeholder; B6 overrides it with the public host |
| `src/UrlShortener.Api/appsettings.json` | unchanged | `AllowedHosts: "*"` stays for local runs (comment in `Program.cs`, JSON has none) |
| `tests/.../ApiFactory.cs` | edited | One line: `UseSetting("PublicUrl:BaseUrl", "http://localhost")` before the `Settings` loop, so tests can override it |
| `tests/.../PublicUrl/PublicUrlTests.cs` | generated, then edited | The five tests. Edited after probe 1 below: the two `X-Forwarded-For` tests passed even with the `Clear()` lines removed, because TestServer's null peer skips the known-proxy check. They now add a startup filter (via `WithWebHostBuilder`, no `ApiFactory` change) that sets the peer to `10.0.0.5`, which the default loopback lists reject |
| (temp) `tests/.../PublicUrl/ZzProbe.cs` | generated then deleted | Probe 2: does `appsettings.Production.json`'s `AllowedHosts` actually load and filter under the test host |
| `docs/adr/0005-rate-limiting.md` | edited | "Resolution (#9)" paragraph appended; nothing above it changed |
| `docs/TESTING.md` | edited | Coverage row; the "share one partition" limitation now says per-IP is tested via `X-Forwarded-For`. Performance section untouched |
| `CLAUDE.md` §9 | edited | `ASPDEPR005`, host filtering on probes, and one clause on the TestServer `RemoteIpAddress` note |
| (spec) `options.KnownNetworks.Clear()` | **rejected** | Obsolete in .NET 10 (`ASPDEPR005`), which `TreatWarningsAsErrors` makes a build error. `KnownIPNetworks.Clear()` used instead (docs below) |
| (proposal) `new Uri(new Uri(BaseUrl), code)` | rejected | `Uri` normalises the configured string (host case, trailing slash). String join keeps exactly what was configured, with one slash |
| (proposal) register the middleware always with `KnownProxies` from configuration | rejected | ACA ingress addresses are not fixed, so there is no list to configure; a bool is the honest shape. A list can be added if a fixed proxy ever exists |
| (proposal) `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` env var instead of code | rejected | Same effect (clears lists, For + Proto per the breaking-change page) but invisible in code and untestable through `ApiFactory` settings; the option documents the trust decision where it is made |
| (proposal) troubleshooting row in `SETUP.md` for a missing base URL | not done | Development always has the value from `appsettings.Development.json`; only a misconfigured deploy hits it, and the message names the key (below) |

## Docs confirmed
- <https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0> (the research report's link, verdict row "Partitioning by IP needs ForwardedHeaders"): default `ForwardedHeaders.None`; "should run before other middleware"; `ForwardLimit` default 1, processed right to left; `KnownProxies`/`KnownNetworks` default to loopback only, and the original remote IP must match them before headers are processed; "Only allow trusted proxies and networks to forward headers. Otherwise, IP spoofing attacks are possible." The page's `KnownNetworks.Clear(); KnownProxies.Clear();` sample appears only in the older-version section ("Forward the scheme for Linux and non-IIS reverse proxies"); the .NET 10 section points at `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, which "doesn't enable features such as the KnownProxies option".
- <https://learn.microsoft.com/aspnet/core/breaking-changes/10/ipnetwork-knownnetworks-obsolete?view=aspnetcore-10.0>: `KnownNetworks` and `Microsoft.AspNetCore.HttpOverrides.IPNetwork` are obsolete from .NET 10 (`ASPDEPR005`); use `KnownIPNetworks` (`System.Net.IPNetwork`).
- <https://learn.microsoft.com/aspnet/core/breaking-changes/8/forwarded-headers-unknown-proxies?view=aspnetcore-10.0>: clearing `KnownNetworks` and `KnownProxies` "to allow any proxy or network to forward these headers" is the documented way to trust any peer, "isn't recommended due to security risks"; "In cloud environments, the proxy IPs can change over the lifetime of the app".

## Decisions (for the engineer to question)
- **`AllowedHosts`: restrictive placeholder in `appsettings.Production.json` (`localhost`).** Meets the issue's "restricted in non-Development configuration" without breaking the Production-environment tests (TestServer's host is `localhost`; all 3 `ProductionErrorPipelineTests` and `MissingBaseUrlFailsStartup` run as Production and pass). It **fails closed**: if B6 forgets to set `AllowedHosts` (env var `AllowedHosts`), every request, `/health/*` included, gets a 400. Loud and safe, but B6 must also check which `Host` the Container Apps health probes send, because host filtering applies to them too. Alternative was leaving `*` and relying only on B6; rejected because a forgotten setting would then be silent. `Staging` or other environments fall back to `*` from `appsettings.json`.
- **`TrustForwardedHeaders = true` trusts any peer.** Correct only when nothing but the ACA ingress can reach the container (ingress on, no other exposure). If the app is ever reachable directly, a client picks its own rate-limit key and scheme. Stated in the option's doc comment, `Program.cs` and ADR-0005.
- **`ForwardLimit` left at 1.** The ingress appends the client address as the rightmost entry; anything a client sent itself is to the left and ignored. If ACA ever adds more than one hop, the key becomes the inner proxy (global limit again), which is the safe failure.
- **`X-Forwarded-Host` not forwarded.** `shortUrl` comes from configuration, so the request host is never used for links; host filtering still sees the real `Host`.
- **`BaseUrl` must have no path.** Hosting under a sub-path (`https://x/s/`) is rejected on purpose; nothing needs it.
- **The trust switch is read once at startup** (`IOptions.Value` after `Build()`), so it is fixed for the process lifetime. This also means a bad `PublicUrl` section fails at that line with the same `OptionsValidationException` as `ValidateOnStart` would.
- **String `BaseUrl` rather than `Uri`.** Matches the other options (`[Url]` works on strings); no analyzer complained.

## Checks run

**Red: `ShortUrlIgnoresClientHostHeader` written first, before any production or `ApiFactory` change:**
```
  Failed UrlShortener.IntegrationTests.PublicUrl.PublicUrlTests.ShortUrlIgnoresClientHostHeader [1 s]
  Error Message:
   Assert.Equal() Failure: Strings differ
                  ↓ (pos 7)
Expected: "http://localhost/OizuZn0"
Actual:   "http://evil.example/OizuZn0"
                  ↑ (pos 7)
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Green: whole integration suite after implementing** (first run after the change; no test needed a second attempt):
```
Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 40 s - UrlShortener.IntegrationTests.dll (net10.0)
```
`CreatesLinkWithGeneratedCode` and `GetReturnsLinkMetadata` still see `http://localhost/{code}`, which shows `ApiFactory`'s `UseSetting` outranks `appsettings.Development.json`'s `http://localhost:5078`.

**Probe 1: are the `Clear()` lines load-bearing?** First version of the tests (null peer), both `Clear()` lines commented out:
```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```
So the test did not exercise the trust lists. After adding the `10.0.0.5` peer filter: with both `Clear()` lines, all 5 `PublicUrlTests` pass; with both commented out:
```
  Failed UrlShortener.IntegrationTests.PublicUrl.PublicUrlTests.ForwardedForIsHonouredWhenTrusted [886 ms]
Expected: Created
Actual:   TooManyRequests
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 886 ms - UrlShortener.IntegrationTests.dll (net10.0)
```
Clearing `KnownIPNetworks` + `KnownProxies` is enough on .NET 10: the obsolete `KnownNetworks` is not consulted separately (or is the same list). Lines restored.

**Probe 2: host filtering under the test host** (`GET /health/live`, temporary theory test, deleted):
```
PROBE Production Host=localhost -> 200 OK
PROBE Production Host=evil.example -> 400 BadRequest
PROBE Development Host=evil.example -> 200 OK
```
`appsettings.Production.json` loads and fails closed; Development keeps `*`. Not kept as a test (the spec named five); a `ProductionRejectsUnknownHost` test is an easy addition if wanted.

**Startup without a base URL** (Release build, `ASPNETCORE_ENVIRONMENT=Production`, no `PublicUrl` section):
```
Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException: DataAnnotation validation failed for 'PublicUrlOptions' members: 'BaseUrl' with the error: 'The BaseUrl field is required.'.; PublicUrl:BaseUrl must be an absolute http or https URL with no path, query or fragment.
   at Microsoft.Extensions.Options.OptionsFactory`1.Create(String name)
   at Microsoft.Extensions.Options.UnnamedOptionsManager`1.get_Value()
```

**Gates:**
```
$ dotnet build -c Release --nologo -v q
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet format --verify-no-changes --no-restore
format exit: 0
$ dotnet test -c Release --nologo
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 461 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 34 s - UrlShortener.IntegrationTests.dll (net10.0)
```
```
$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.
$ git status --short
 M CLAUDE.md
 M docs/TESTING.md
 M docs/adr/0005-rate-limiting.md
 M src/UrlShortener.Api/Links/CreateLinkEndpoint.cs
 M src/UrlShortener.Api/Links/GetLinkEndpoint.cs
 M src/UrlShortener.Api/Program.cs
 M src/UrlShortener.Api/appsettings.Development.json
 M tests/UrlShortener.IntegrationTests/ApiFactory.cs
?? src/UrlShortener.Api/PublicUrl/
?? src/UrlShortener.Api/appsettings.Production.json
?? tests/UrlShortener.IntegrationTests/PublicUrl/
$ git diff --stat
 CLAUDE.md                                         |  3 ++-
 docs/TESTING.md                                   |  3 ++-
 docs/adr/0005-rate-limiting.md                    |  3 +++
 src/UrlShortener.Api/Links/CreateLinkEndpoint.cs  |  6 +++--
 src/UrlShortener.Api/Links/GetLinkEndpoint.cs     |  8 +++---
 src/UrlShortener.Api/Program.cs                   | 31 +++++++++++++++++++++--
 src/UrlShortener.Api/appsettings.Development.json |  3 +++
 tests/UrlShortener.IntegrationTests/ApiFactory.cs |  1 +
 8 files changed, 49 insertions(+), 9 deletions(-)
```
(Gates re-run after the probes and the test edit, probe file deleted, `Clear()` lines restored. Status taken before this log was added; the log is also untracked. `ForwardedForIsHonouredWhenTrusted` was red only through probe 1, not before the middleware existed; `ForwardedForIsIgnoredByDefault` is its control.)

Tests: 86 → 91 (+5 integration). No existing test edited; `ApiFactory` gained one line.

## Gotchas encountered
Added to CLAUDE.md §9: `KnownNetworks` obsolete in .NET 10 (`ASPDEPR005`) while the proxy docs page still shows it. The TestServer `RemoteIpAddress` note gained a sentence: a null peer skips the known-proxy check, so forwarded-headers tests need a real peer address (probe 1). Host filtering on health probes is a B6 check, kept in open items rather than §9 (not hit yet).

## Open items
- High-impact: `/security-review` and a fresh-context `/code-review` before merge (CLAUDE.md §6.6); not run by the subagent.
- B6: set `PublicUrl__BaseUrl`, `AllowedHosts` and `PublicUrl__TrustForwardedHeaders=true` in Bicep; verify the probe `Host` against host filtering; confirm the ingress appends exactly one `X-Forwarded-For` entry.
- `docs/HANDOFF.md` not updated by the subagent (coordinator's end-of-session step; B5 runs in parallel).

## Coordinator review
Fable 5.1 (coordinator session), after the subagent's hand-back. Gates re-run by the coordinator before the reviews: build 0/0, format exit 0, no vulnerable packages, 47 unit + 44 integration passed.

**`/security-review` (fresh Opus agent, committed diff): no findings.** Examined: forwarded-header trust (off by default, documented when on, feeds only the rate-limit key), `shortUrl` construction (client `Host`/`Scheme` removed; base URL from validated configuration; `X-Forwarded-Host` not processed), host filtering (fails closed), configuration validation (configuration is trusted input), no new logging or response fields.

**Fresh-context `/code-review`: 8 findings → 5 fixed, 3 deferred with reasons.**

| # | Finding | Outcome |
|---|---|---|
| 1 | `IsValidBaseUrl` used `Uri.TryCreate`, which accepts surrounding whitespace and CR/LF (the CLAUDE.md §9 gotcha the destination validator already handles); `ShortUrl` joins the raw string, so `"https://sho.rt "` passed startup and corrupted every `shortUrl`. Reviewer verified on .NET 10. | **Fixed:** the raw string must be printable ASCII (same rule as `CreateLinkValidator`). New theory `MalformedBaseUrlFailsStartup` covers trailing space and `\n`. |
| 5 | User info accepted in the base URL (`https://u:p@sho.rt`), inconsistent with the destination validator. | **Fixed:** `uri.UserInfo.Length == 0`; theory case added. |
| 4 | `[Required]` + `[Url]` were redundant with the custom check and weaker (`[Url]` is a prefix check that also accepts `ftp://`), producing two contradictory messages on a bad value. | **Fixed:** attributes and `ValidateDataAnnotations()` removed; one message. `ftp://` and a sub-path are theory cases. |
| 3 | `AllowedHosts: localhost` in Production is the change most likely to take a deploy down (probes 400 until B6 sets the host) and had no test after probe 2 was deleted. Reviewer also notes the 400 is written by `HostFilteringMiddleware` before `UseStatusCodePages`, so it has no ProblemDetails body. | **Partly fixed, decision surfaced:** `ProductionRejectsUnknownHost` added (evil host 400, `localhost` 200). Whether to keep the fail-closed placeholder or `*` is the engineer's call in the PR; nothing reads `Host` any more, so host filtering is defence in depth, not protection of `shortUrl`. |
| 7 | `CreateRateLimitedFactory` returned the `WithWebHostBuilder` derivative, so the parent `ApiFactory` was never disposed and the shape differed from every other test class. | **Fixed:** the peer-address startup filter moved into `ApiFactory` behind `RemoteIpAddress { get; init; }`, like `ClickQueueCapacity`; the test uses the normal `using var factory` shape. |
| 8 | `brownfield.md` still named the option `PublicBaseUrl` and said forwarded headers would use "known networks". | **Fixed:** two one-line edits. |
| 2 | A tenth `IClassFixture<SqlServerFixture>` class is one more SQL Server container on the runner (the PR #20 flake source). | **Deferred:** one class per issue is the existing convention and reads better for review; the engineer's rule is to do #21 only if the flake recurs. Noted in the PR as the trigger. |
| 6 | Third copy of the http/https scheme predicate. | **Deferred:** three one-liners; a helper would be an abstraction for a predicate that has not changed. |

Tests after the fixes: `PublicUrlTests` 5 → 11 (+5 theory cases, +1 host test); affected classes (`PublicUrlTests`, `ProductionErrorPipelineTests`, `RateLimitingTests`) 18/18; full suite in the PR body.

## Sign-off
(pending engineer review of the PR)
