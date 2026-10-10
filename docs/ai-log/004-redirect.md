# AI log 004 — GET /{code} redirect with 302 and no-store caching

| Field | Value |
|---|---|
| Issue | #4 |
| Branch | `feat/04-redirect` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | low |

## Intent
Redirect visitors to the destination without letting intermediaries cache the redirect, so later analytics and deletes keep working.

## Constraints given to the AI
- TDD: integration tests written first and seen failing, through `WebApplicationFactory` + Testcontainers.
- `TypedResults`, `CancellationToken` passed through, feature folder `Redirect/`, match `CreateLinkEndpoint` style.
- One `AsNoTracking()` query selecting only what is needed.
- No click recording, no caching, no new packages, no `X-Robots-Tag`, no logging of destination URLs.
- Expiry not handled (Day 3 decides 404 vs 410). No commits, no PR.

## Acceptance criteria (runnable)
- Known code → 302 with exact `Location` and `Cache-Control: private, no-store` → `KnownCodeRedirectsWithFoundAndNoStore`
- Unknown code → 404 ProblemDetails → `UnknownCodeIsNotFoundProblem` (asserts `application/problem+json`)
- Case-sensitive lookup end to end → `CodeDifferingOnlyInCaseIsNotFound` (stored `CaseTst4`, requested `casetst4`)
- Soft-deleted → 404 → `SoftDeletedCodeIsNotFound` (+ mutation check below)
- Route does not collide with `/api`, `/openapi` → `RedirectRouteDoesNotShadowApiOrOpenApi` (POST `/api/links` 201, GET `/openapi/v1.json` 200). `/health` does not exist yet (G5); see "Decisions".

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #4 per the issue and a spec (302 + `Location` + `private, no-store`, 404 ProblemDetails for unknown/soft-deleted, Base62 4-10 route constraint, one `AsNoTracking` query, expiry out of scope), hard rules from CLAUDE.md §6-§9, test list, checks to paste.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs` | generated (before the code) | 5 tests; client with `AllowAutoRedirect = false`; rows inserted through `fixture.CreateDbContext()` |
| `src/UrlShortener.Api/Redirect/RedirectEndpoint.cs` | generated, then edited | Edited: `NotFound()` → `Problem(404)` after observing an empty 404 body (see below); route comment corrected (`/scalar` does match the pattern) |
| `src/UrlShortener.Api/Program.cs` | edited | `using UrlShortener.Api.Redirect;` + `app.MapGet(RedirectEndpoint.Route, RedirectEndpoint.HandleAsync)` after the `/api/links` group |
| (proposal) `TypedResults.NotFound()` | rejected | Observed: 404 with no body and no `Content-Type` under `AddProblemDetails()` alone (ProblemDetails for empty status results needs `UseStatusCodePages`) |
| (proposal) `:alpha` + `:length(4,10)` constraints | rejected | `alpha` is letters only; codes contain digits. Regex is the simplest correct constraint |
| (proposal) select `OriginalUrl` + `DeletedAt`, test `DeletedAt` in C# | rejected for now | Filtering `DeletedAt == null` in SQL and selecting only `OriginalUrl` is still one query and fewer lines. Day 3 (410 for expired/deleted) will need `DeletedAt`/`ExpiresAt` in the projection to tell them apart from unknown |
| (proposal) shared `ApiFactory` per class | rejected | Would need a second fixture combining the container and the factory; a per-test factory matches `CreateLinkEndpointTests` (startup-migration cost already noted in #5) |

## TDD sequence (real output)

**Red: tests written, no endpoint mapped**
```
$ dotnet test --nologo tests/UrlShortener.IntegrationTests --filter "FullyQualifiedName~Redirect"
  Failed UrlShortener.IntegrationTests.Redirect.RedirectEndpointTests.UnknownCodeIsNotFoundProblem [44 ms]
Expected: "application/problem+json"
Actual:   null
  Failed UrlShortener.IntegrationTests.Redirect.RedirectEndpointTests.KnownCodeRedirectsWithFoundAndNoStore [51 ms]
Expected: Found
Actual:   NotFound
Failed!  - Failed:     2, Passed:     3, Skipped:     0, Total:     5, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```
The three passing tests are 404/shadowing assertions that hold trivially while no route exists; the mutation check below shows the soft-delete one depends on the code.

**Still red: endpoint with `TypedResults.NotFound()`**
```
  Failed UrlShortener.IntegrationTests.Redirect.RedirectEndpointTests.UnknownCodeIsNotFoundProblem [55 ms]
Expected: "application/problem+json"
Actual:   null
Failed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Green: `TypedResults.Problem(statusCode: 404, title: "Short link not found.")`**
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Mutation check: the soft-delete test depends on the filter.** `&& l.DeletedAt == null` temporarily removed, then restored:
```
  Failed UrlShortener.IntegrationTests.Redirect.RedirectEndpointTests.SoftDeletedCodeIsNotFound [266 ms]
Expected: NotFound
Actual:   Found
Failed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

## Decisions (for the engineer to question)
- **Route constraint `regex(^[A-Za-z0-9]{4,10}$)`** (written `{{4,10}}` in the C# string, because braces are route-template syntax). One segment, Base62, length 4-10 = the custom alias range; generated codes are 7. `/api` (3 chars), every multi-segment path (`/api/links`, `/health/live`, `/openapi/v1.json`) and anything with a dot never reach the handler. **Caveat:** a single-segment literal such as `/scalar` or `/health` *does* match the pattern; it is safe only because ASP.NET routing ranks literal segments above parameters. That is also why G3's reserved-alias list matters: an alias `scalar` would be shadowed by the Scalar UI.
- **404 for unknown and soft-deleted alike, via `TypedResults.Problem(404)`, not `TypedResults.NotFound()`.** `NotFound()` produced an empty body with no `Content-Type` (red run above), because `AddProblemDetails()` only shapes bodies written by the exception handler / status-code-pages middleware. A global `UseStatusCodePages()` would fix it for every endpoint; that belongs to G5 (#5, ProblemDetails for all errors), so the fix here is local and matches the 409 in `CreateLinkEndpoint`.
- **Expired links still redirect** in this task (`ExpiresAt` is not read). Day 3's ambiguous scenario decides 404 vs 410 (ADR-0006).
- **`Cache-Control: private, no-store` on the 302 only.** The 404 has no explicit cache header (not in the spec); revisit with the 410 decision if negative caching matters.
- **`Location` is the stored string as-is.** The G3 validator guarantees printable ASCII with no whitespace, so Kestrel accepts it as a header value. The test uses a percent-encoded query (`ref=a%20b`) and asserts the exact string, so it is neither re-encoded nor decoded.
- **One query:** `AsNoTracking().Where(Code == code && DeletedAt == null).Select(OriginalUrl).SingleOrDefaultAsync(ct)`. Case sensitivity comes from the column collation (G2), not the query; `CodeDifferingOnlyInCaseIsNotFound` proves it end to end.
- **No logging** added; G5 owns logging (code + host only).

## Checks run
```
$ dotnet build --nologo -v q
    0 Warning(s)
    0 Error(s)

$ dotnet format --verify-no-changes --no-restore; echo $?
0

$ dotnet test --nologo; echo $?
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 477 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
0

$ git status --short
 M src/UrlShortener.Api/Program.cs
?? src/UrlShortener.Api/Redirect/
?? tests/UrlShortener.IntegrationTests/Redirect/
```
(This log was written after the status snapshot and also shows as untracked.)

## Gotchas encountered
None added to CLAUDE.md §9. (`NotFound()` without a body under `AddProblemDetails` is recorded above; it is documented framework behaviour and G5's global fix will cover it.)

## Open items
- G5 (#5): `UseStatusCodePages()` so empty status results (and framework 400s) become ProblemDetails globally.
- Day 3: expiry → 404 vs 410 (ADR-0006); will need `ExpiresAt`/`DeletedAt` in the projection.
- `docs/HANDOFF.md` not updated by the subagent (coordinator's end-of-session step).

## Sign-off
(pending engineer review of the PR)

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`); gates re-run independently: build 0/0, format exit 0, 47 unit + 15 integration passed. Not high-impact (no schema, validation, policy or infra change), so no `/security-review` or `/code-review` this time per CLAUDE.md §6.6.

| Agent decision | Outcome | Rationale |
|---|---|---|
| Filter `DeletedAt == null` in SQL and select only `OriginalUrl` | accepted | Simpler than the brief's "select both"; Day 3 changes the selection anyway when it adds 410 |
| `TypedResults.Problem(404)` locally instead of `UseStatusCodePages` | accepted | Matches the 409 in `CreateLinkEndpoint`; the global pipeline is #5's job |
| Expired links still redirect | accepted | Specified; Day 3 (ambiguous scenario) decides 404 vs 410 |
| No cache header on 404 | accepted | RFC 9110 makes 404 heuristically cacheable; add `no-store` when 410 lands in Day 3 |
| Regex route constraint; literal routes win over `/scalar`, `/health` | accepted | Reserved-alias list in G3 already covers those names |
