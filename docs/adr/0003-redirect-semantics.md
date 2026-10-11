# ADR-0003: Redirect with 302 and `Cache-Control: private, no-store`

- Status: accepted (expiry behaviour deferred, see below)
- Date: 2026-10-10
- Deciders: Mohammed Saalim
- AI assistance: Claude Code drafted options; decision and wording are the engineer's

## Context and problem statement
`GET /{code}` sends visitors to the destination. The status code and cache headers decide whether browsers and intermediaries remember the redirect. If they do, later deletes and click counting stop working because requests never reach the service. Which status and headers does the redirect use, and what do unknown, deleted and expired codes return?

## Decision drivers
- Links can be deleted now (`DELETE /api/links/{code}`) and will expire later.
- Day 2 adds click analytics: every visit must reach the service.
- RFC 9110 cacheability rules, which in practice are decided by cache headers.
- Simplicity: one query, no cache layer yet.

## Considered options
1. 301 Moved Permanently
2. 302 Found, with `Cache-Control: private, no-store`
3. 307 or 308 (method-preserving)

## Decision outcome
Chosen option: "2. 302 with `Cache-Control: private, no-store`", because every click then reaches the service, so deletes take effect immediately and analytics can count visits.

- Unknown or soft-deleted code: **404** ProblemDetails (`application/problem+json`).
- Expired code: **not handled on Day 1**; expired links still redirect. Whether expiry returns 410 (with `no-store`) or 404 is decided on Day 3 (planned ADR-0007). This ADR does not pre-empt that decision.

### Consequences
- Good: deletes and later edits are effective immediately; Day 2 click counting sees every visit.
- Bad: no edge or browser caching of redirects, so every click costs a database query and its latency. A server-side cache (HybridCache) is a Day 2 option and does not change this contract.
- Neutral: the 404 carries no explicit cache header yet. RFC 9110 makes 404 and 410 heuristically cacheable ([§15.5.11](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.11) covers 410), and the research report advises `no-store` on 410; to be added with the Day 3 decision.
- Neutral: `Location` is the stored string as-is. The validator guarantees printable ASCII, which Kestrel accepts in a header (found in the code review recorded in AI log 003).

### Confirmation
- `RedirectEndpointTests.KnownCodeRedirectsWithFoundAndNoStore` asserts 302, the exact `Location` and `Cache-Control: private, no-store` ([`RedirectEndpointTests.cs`](../../tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs)).
- `UnknownCodeIsNotFoundProblem` and `SoftDeletedCodeIsNotFound` cover 404; removing the `DeletedAt == null` filter made the latter fail (AI log 004 mutation check).
- Real response, from [AI log 005](../ai-log/005-api-surface.md): `HTTP/1.1 302 Found` with `Cache-Control: private, no-store` and the `Location` header.

## Pros and cons of the options
### 1. 301
- Good: browsers and intermediaries may cache it, so repeat visits skip the service.
- Bad: RFC 9110 §15.4 makes 301 and 308 heuristically cacheable ([§15.4](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.4)); a cached redirect bypasses deletes, edits and click counts. Cache headers decide in practice (RFC 9111 defines the caching rules).

### 2. 302 + `private, no-store`
- Good: see decision. Shlink and Kutt default to 302 per the research report ([Shlink docs](https://shlink.io/documentation/environment-variables/)).
- Bad: no caching benefit.

### 3. 307 / 308
- Good: preserve the request method; 308 is the permanent variant.
- Bad: method preservation is irrelevant for a GET-only redirect; 308 is cacheable like 301, and 307 adds nothing over 302 here. Not researched further.

## More information
Observed vendor behaviour, **from the research pass's own curl observations, not vendor documentation** ([report](../research/reports/URL%20shortener%20assignment%20validation.md), verdict row "bit.ly uses 301"; listed as weaker evidence under "Gaps in this research"):

| Service | Observed |
|---|---|
| bit.ly | 301 + `private, max-age=90` |
| TinyURL | 301 + `no-store` |
| Dub | 302 ([Dub blog](https://dub.co/blog/301-vs-302-redirect)) |

The bit.ly and TinyURL rows show that a 301 with a short `max-age` or `no-store` is also workable; the header, not the status code, decides. The conservative choice for a service with deletable links is 302 plus `no-store`.

- Research: report section "Domain design guidance" (Redirects, 404 / 410).
- Implementation: [AI log 004](../ai-log/004-redirect.md).
