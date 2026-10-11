# AI log 010 — Rate limit on POST /api/links: 429 + Retry-After; ADR-0005

| Field | Value |
|---|---|
| Issue | #17 |
| Branch | `feat/10-rate-limit` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | high-impact (rate-limit policy) |

## Intent
Protect link creation from abuse with the built-in ASP.NET Core rate limiter, returning 429 with `Retry-After` (issue #17).

## Constraints given to the AI
- Framework rate limiter only; no new packages. One options class, one ADR, one new test class, one AI log. No over-engineering.
- TDD: `CreateOverLimitIsTooManyRequestsWithRetryAfter` first and red, then implement.
- Per-IP fixed window with a constant fallback key (brownfield decision 3); do **not** add `UseForwardedHeaders` (#9); state the proxy caveat.
- Redirect, GET, DELETE, stats and health not limited. Options validated on start.
- `Program.cs`: new blocks only, no reordering (B3 runs in parallel and adds a `MapGet` to the same group).
- No commit/push/PR; no reading `.env`/secrets/`.azure/`.

## Acceptance criteria (runnable)
- N+1 POSTs → 429 with `Retry-After` → `CreateOverLimitIsTooManyRequestsWithRetryAfter` (also asserts the problem+json body)
- Redirect not limited → `RedirectIsNotRateLimited`; get/delete not limited → `GetAndDeleteAreNotRateLimited`
- Policy values in options, validated on start → `OptionsAreValidatedOnStart`
- ADR-0005 (algorithm, limits, partition key, what is not limited and why) → `docs/adr/0005-rate-limiting.md`
- Existing 77 tests unchanged and green → 81 total below

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #17 per the spec (options class, policy `create`, 429 + `Retry-After` + ProblemDetails in `OnRejected`, `UseRateLimiter` after `UseStatusCodePages`, `RequireRateLimiting` on the POST only, `ApiFactory.Settings`), the four named tests, ADR-0005, this log, `TESTING.md`; confirm whether TestServer sets `RemoteIpAddress`; verify the API shapes on learn.microsoft.com.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `src/UrlShortener.Api/RateLimiting/RateLimitOptions.cs` | generated | `PermitLimit` `[Range(1, int.MaxValue)]` default 10, `WindowSeconds` `[Range(1, 86400)]` default 60; section name and policy name as constants |
| `src/UrlShortener.Api/Program.cs` | edited | One new block after the click registrations: options binding + `ValidateDataAnnotations().ValidateOnStart()`, `AddRateLimiter` (429, `OnRejected`, policy `create`). `app.UseRateLimiter()` after `UseStatusCodePages()`. `.RequireRateLimiting(...)` appended to the existing `MapPost` as a continuation line. No existing line reordered |
| `src/UrlShortener.Api/appsettings.json` | edited, then **reverted by the coordinator** | The `RateLimiting` section duplicated the code defaults (a third copy lived in the test); code defaults are now the single source, overridable via configuration |
| `tests/.../ApiFactory.cs` | edited | `Settings` init property (`IDictionary<string, string?>`), each entry applied with `UseSetting` |
| `tests/.../RateLimiting/RateLimitingTests.cs` | generated | The four tests. Redirect and get/delete tests first exhaust the create permit (assert a 429), so they prove those routes are unlimited while the limiter is actually saturated, not just that they work |
| `docs/adr/0005-rate-limiting.md` | generated | Status `proposed` until the engineer signs off the policy values |
| `docs/TESTING.md` | edited | Coverage row; known limitation (one shared partition in tests) |
| `CLAUDE.md` §9 | edited | TestServer `RemoteIpAddress` is null (below) |
| (temp) `tests/.../ZzProbe.cs` | generated then deleted (three times) | 1: record `RemoteIpAddress` under TestServer. 2: print the real 429 response. 3: does `Retry-After` count down |
| (proposal) set only `Retry-After` in `OnRejected` and let `UseStatusCodePages` write the body | rejected | The spec fixes the title "Too many requests"; the body would depend on a framework default for 429 that was not verified |
| (proposal) manual JSON fallback when `IProblemDetailsService.TryWriteAsync` returns false | rejected | `AddProblemDetails()` is always registered. If the writer declines (e.g. an `Accept` it cannot satisfy), nothing is written and `UseStatusCodePages` upstream is expected to add its plain-text fallback (reasoned, not run); the 429 and `Retry-After` stand either way. No code for it |
| (proposal) `UseForwardedHeaders` | rejected (out of scope) | Issue #9; caveat stated in ADR-0005 and in the code comment on the partition key |

## API shapes verified
Read from <https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0>: `RateLimiterOptions.RejectionStatusCode`, `OnRejected = async (context, cancellationToken) => ...` with `context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)` and `context.HttpContext`, `options.AddPolicy(name, httpContext => RateLimitPartition....)`, `RateLimitPartition.GetFixedWindowLimiter(partitionKey, factory)`, `.RequireRateLimiting(name)`, the `RemoteIpAddress?.ToString() ?? "unknown"` partition example, and the spoofing/DoS warning for IP partitions. The page says `UseRateLimiter` must run after `UseRouting` for endpoint policies; `WebApplication` adds `UseRouting` implicitly at the start of the pipeline, and the green run confirms the endpoint policy fires.

## Observations
- **TestServer does not set `RemoteIpAddress`.** Probe (`IStartupFilter` middleware capturing `Connection.RemoteIpAddress`, one request to `/health/live`):
  ```
  PROBE RemoteIpAddress = null
  ```
  So all test requests use the `"unknown"` fallback key. The tests pass whether the key is null-fallback or loopback (one key either way); they prove the limit, not per-IP separation.
- **Real 429 response** (limit 1, second POST):
  ```
  PROBE 429 TooManyRequests
  PROBE Retry-After: 60
  PROBE Content-Type: application/problem+json
  PROBE {"title":"Too many requests","status":429,"traceId":"00-e4d33190354e89fc96e199157224f173-300820035ae22729-00"}
  ```
- **`Retry-After` does not count down.** Third probe (limit 1; reject, wait 5 s, reject again):
  ```
  PROBE immediately: 429 Retry-After 60
  PROBE after 5 s:   429 Retry-After 60
  ```
  The fixed-window lease reports the whole window, not the time left in it, so the header can overstate the wait by up to one window. Safe (a client never retries too early), but not exact. The test asserts 1-60 s, which holds either way.
- `OptionsValidationException` reaches the test unwrapped from `factory.CreateClient()`, so the test asserts the exact type and that the message names `PermitLimit`.

## Decisions (for the engineer to question)
- **Values 10 per 60 s per IP.** A guess sized for a demo; rate-limit policy is high-impact (§6.6), so ADR-0005 stays `proposed` until you sign it off.
- **Effectively global in Azure until #9.** Behind Container Apps ingress every request has the proxy's IP, so all users share 10 creates per minute. #9 should land before B6 deploys this.
- **Per-replica, in-memory counters**: N replicas means N × the limit; restarts reset. Stated in ADR-0005.
- **Unbounded partition keys.** A client with many addresses (IPv6 /64) gets a window per address. The research checklist says to bound keys; left as a follow-up in ADR-0005.
- **`AutoReplenishment = true`** is set as the spec asks. The docs say the `RateLimitPartition.GetTokenBucketLimiter` helper forces it to `false` and replenishes all partitions from one shared timer; the same may hold for `GetFixedWindowLimiter`, **not verified**. Either way the window resets.
- **Options are read per request** from `IOptions<RateLimitOptions>` inside the policy; the limiter factory runs once per key, so values are fixed after a key's first request (no reload).
- **No `RequestServices` null case or `TryWriteAsync` false case handled** (see rejected proposals).
- **OpenAPI does not list the 429** on `CreateLink` (no `Produces`). Out of the spec; easy to add if wanted.
- **`Retry-After` is rounded up** (`Math.Ceiling`); the docs sample truncates. It is always the full window (observed above), so it overstates; computing the real time left would need our own window clock. Not done.

## Checks run

**Red 1: test written, nothing wired** (`ApiFactory.Settings` added so the test compiles):
```
  Failed UrlShortener.IntegrationTests.RateLimiting.RateLimitingTests.CreateOverLimitIsTooManyRequestsWithRetryAfter [2 s]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: TooManyRequests
Actual:   Created
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 2 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Red 2: limiter and policy wired, no `RejectionStatusCode`/`OnRejected`** (the CLAUDE.md §9 default; the assertion stops at the status, so the missing header was not separately asserted in this run):
```
[xUnit.net 00:00:20.39]       Expected: TooManyRequests
[xUnit.net 00:00:20.39]       Actual:   ServiceUnavailable
  Failed UrlShortener.IntegrationTests.RateLimiting.RateLimitingTests.CreateOverLimitIsTooManyRequestsWithRetryAfter [1 s]
```

**Green: the four new tests:**
```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Gates (probe deleted):**
```
$ dotnet build -c Release --nologo -v q
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet format --verify-no-changes --no-restore
format exit: 0
$ dotnet test -c Release --nologo
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 674 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    34, Skipped:     0, Total:    34, Duration: 5 s - UrlShortener.IntegrationTests.dll (net10.0)
$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.
$ git status --short
 M CLAUDE.md
 M docs/TESTING.md
 M src/UrlShortener.Api/Program.cs
 M src/UrlShortener.Api/appsettings.json
 M tests/UrlShortener.IntegrationTests/ApiFactory.cs
?? docs/adr/0005-rate-limiting.md
?? src/UrlShortener.Api/RateLimiting/
?? tests/UrlShortener.IntegrationTests/RateLimiting/
$ git diff --stat
 CLAUDE.md                                         |  1 +
 docs/TESTING.md                                   |  2 +
 src/UrlShortener.Api/Program.cs                   | 48 ++++++++++++++++++++++-
 src/UrlShortener.Api/appsettings.json             |  6 ++-
 tests/UrlShortener.IntegrationTests/ApiFactory.cs |  7 ++++
 5 files changed, 62 insertions(+), 2 deletions(-)
```
(Status taken before this log was written; the log itself is also untracked.) Tests: 77 → 81 (+4 integration). No existing test edited.

## Gotchas encountered
Added to CLAUDE.md §9: TestServer leaves `RemoteIpAddress` null, so all integration-test requests share the fallback partition. The 503 default (already in §9) was reproduced in red 2.

## Open items
- High-impact (rate-limit policy): `/security-review` and a fresh-context `/code-review` before merge (CLAUDE.md §6.6); not run by the subagent. Engineer sign-off on the values in the PR; then ADR-0005 → `accepted`.
- #9 before B6, or the limit is global in Azure.
- `docs/HANDOFF.md` not updated by the subagent (coordinator's end-of-session step; B3 runs in parallel).

## Sign-off
Reviewed and merged by the engineer in PR #24; I understood the change and accept ownership of it. — Mohammed Saalim, sign-off recorded 2026-10-11

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`); gates re-run independently in the agent's worktree: build -c Release 0/0, format exit 0, 47 unit + 34 integration, no vulnerable packages. High-impact (rate-limit policy), so both review tools ran. Built in parallel with B3 (#16) in a separate worktree.

**`/security-review` (fresh Opus agent): no HIGH/MEDIUM findings.** Partition key comes only from the TCP peer (no forwarded headers are read anywhere in `src/`); `Retry-After` is an integer from lease metadata; the rejection body is fixed; the limiter sits inside the exception handler so a failure in it is a generic 500 ProblemDetails; `Range` + `ValidateOnStart` mean configuration cannot switch the limit off without the app refusing to start.

**`/code-review medium` (fresh context): 5 findings, triaged:**

| Finding | Outcome | What changed |
|---|---|---|
| `AutoReplenishment = true` is dead: `GetFixedWindowLimiter` forces it to `false` and replenishes from the partitioned limiter's shared timer (API docs remark; closes the "not verified" item above) | **fixed** | Line removed, comment explains why |
| `UseRateLimiter` before `UseHttpsRedirection`: an `http://` POST spends a permit, gets a 307, then spends another | **fixed** | `UseRateLimiter()` moved after `UseHttpsRedirection()` |
| Policy values in three places (`appsettings.json`, property initialisers, a test literal) would diverge on sign-off changes | **fixed** | `appsettings.json` section removed; code defaults are the single source; the test reads `IOptions<RateLimitOptions>` from the host instead of a literal 60 |
| `OptionsAreValidatedOnStart` ran in Development and migrated the database before asserting an options error | **fixed** | Factory runs as `Production` for that test (no migration, no DB dependency) |
| `docs/HANDOFF.md` stale | **fixed** | Updated in this commit (coordinator's end-of-task step) |

Agent decisions accepted: 10 per 60 s per IP as the proposed values (engineer signs off in the PR; ADR-0005 stays "proposed" until then), `"unknown"` fallback key, no `UseForwardedHeaders` here (#9), per-replica in-memory counters stated as a consequence, no cap on partition count noted as a follow-up, `Retry-After` reporting the full window rather than the remaining time (safe, overstated by up to one window).
