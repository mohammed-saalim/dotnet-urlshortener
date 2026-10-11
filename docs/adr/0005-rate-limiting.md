# ADR-0005: Rate-limit link creation with a per-IP fixed window, 429 + `Retry-After`

- Status: proposed (rate-limit policy is high-impact; values need the engineer's sign-off in the PR)
- Date: 2026-10-10
- Deciders: Mohammed Saalim
- AI assistance: Claude Code drafted options; decision and wording are the engineer's

## Context and problem statement
`POST /api/links` is anonymous and writes a row per call, so one client can fill the database or burn the Azure SQL free-offer budget. The redirect is the hot path and must stay fast and unlimited. Which limiter, which partition key, which limits, and what does a rejected client see?

## Decision drivers
- Framework only (`Microsoft.AspNetCore.RateLimiting`), no package.
- A rejected client must be told when to retry: `429` + `Retry-After`, ProblemDetails body like every other error. The framework default is `503` with no `Retry-After` ([rate limit docs](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-8.0); CLAUDE.md §9).
- The client IP is only correct behind a proxy once forwarded headers are configured with known networks ([proxy docs](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)); that is issue #9, not done yet.
- Simplicity and testability: one policy, values in options.

## Considered options
1. No limiting
2. Fixed window
3. Sliding window
4. Token bucket
5. Concurrency limiter

and, for the partition: one global window / per-IP window / per-IP only after `UseForwardedHeaders` (brownfield decision 3).

## Decision outcome
Chosen option: "2. Fixed window, per client IP, on create only", because it is the simplest limiter that caps requests per period, and its failed lease carries `RetryAfter` for the header.

- Policy `create`, applied with `RequireRateLimiting` to `POST /api/links` only. Redirect, `GET`/`DELETE /api/links/{code}`, stats and health are **not** limited: the redirect is the product, and the others are cheap reads or need a code the caller already owns.
- Partition key: `Connection.RemoteIpAddress`, or the constant `"unknown"` when it is null (brownfield decision 3). The IP is held in memory as a key only, never logged or stored.
- Defaults: **10 requests per 60 s per IP**, `QueueLimit = 0` (reject, don't queue). Options `RateLimiting:PermitLimit` and `RateLimiting:WindowSeconds`, validated on start.
- Rejection: `RejectionStatusCode = 429`; `OnRejected` writes `Retry-After` (whole seconds, rounded up) from `MetadataName.RetryAfter` and a ProblemDetails body (`title: "Too many requests"`) through `IProblemDetailsService`.

### Consequences
- Good: abuse of create is bounded with no package and about 30 lines; clients get a standard 429 they can back off from.
- Bad: **behind Container Apps ingress every client has the proxy's IP until #9 configures forwarded headers**, so in Azure the limit is effectively global (10 creates per minute for everyone). #9 must land before this is meaningful in the cloud. The same applies to any null `RemoteIpAddress` (all share `"unknown"`); TestServer is one such case.
- Bad: counters are **in memory per replica**, so with N replicas the real limit is N × 10, and a restart resets them. Acceptable for a prototype; a shared store (Redis) would be needed for an exact limit.
- Bad: the fixed-window lease reports the whole window as `RetryAfter`, not the time left (observed: 60 s both at once and 5 s later, AI log 010), so `Retry-After` can overstate the wait by up to one window. Safe, not exact.
- Bad: a fixed window allows a burst of up to 2 × limit across a window boundary. Sliding window would smooth that at the cost of segments; not worth it at these values.
- Neutral: per-IP keys come from the connection address, not from headers, so a client cannot mint keys by sending headers. With forwarded headers (#9) only known proxies may set the address; the docs warn that IP partitions are open to spoofing-based DoS otherwise.
- Bad: partition keys are not bounded. A client with many addresses (an IPv6 /64, a botnet) gets a fresh window per address and grows the limiter's key set. The research checklist asks to bound partition keys; keying IPv6 by /64 or capping keys is a follow-up, not done here.

### Confirmation
`tests/UrlShortener.IntegrationTests/RateLimiting/RateLimitingTests.cs`:
- `CreateOverLimitIsTooManyRequestsWithRetryAfter`: limit 2, third POST is 429 with `Retry-After` between 1 and 60 s and an `application/problem+json` body.
- `RedirectIsNotRateLimited`, `GetAndDeleteAreNotRateLimited`: after the create permit is used up (429), redirects still 302 and get/delete still 200/204.
- `OptionsAreValidatedOnStart`: `PermitLimit = 0` fails host start with `OptionsValidationException`.

Red → green output is in [AI log 010](../ai-log/010-rate-limit.md).

## Pros and cons of the options
### 1. No limiting
- Good: nothing to build.
- Bad: an anonymous write endpoint with no bound.
### 2. Fixed window
- Good: simplest; one counter per key; `RetryAfter` metadata on rejection.
- Bad: boundary bursts (up to 2 × limit).
### 3. Sliding window
- Good: smooths boundary bursts.
- Bad: more state per key (segments) for no visible gain at 10 per minute.
### 4. Token bucket
- Good: allows controlled bursts with a steady refill rate.
- Bad: two knobs (bucket size, refill) to explain and tune; no need for bursts here.
### 5. Concurrency limiter
- Good: protects against slow, parallel requests.
- Bad: does not cap requests per period, and its lease has no `RetryAfter` (research report, verdict row on the 503 default).

## More information
- Research: [report](../research/reports/URL%20shortener%20assignment%20validation.md), verdict rows "Rate limiter rejects with 503 and sends no Retry-After by default" and "Partitioning by IP needs ForwardedHeaders".
- Impact analysis: [brownfield scenario](../scenarios/brownfield.md), B4 row and decision 3.
- Follow-up: #9 (forwarded headers with known networks, before B6).
