# AI log 003 — Short-code generator and POST /api/links with validation and retry-on-conflict

| Field | Value |
|---|---|
| Issue | #3 |
| Branch | `feat/03-create-link` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | high-impact (destination-URL validation) |

## Intent
Create short links with a cryptographically random 7-char Base62 code or a custom alias, validating the destination before storing it.

## Constraints given to the AI
- TDD (test first, seen failing) for the short-code generator and the destination-URL validator; endpoint integration tests alongside.
- `RandomNumberGenerator.GetString`; no `System.Random` (CA5394 is an error).
- Uniqueness by insert + catch unique violation (2601/2627) + retry with a new code, max 5. Never check-then-insert. Custom alias violation → 409, no retry.
- `TypedResults`, `MapGroup("/api/links")`, ProblemDetails, `CancellationToken` passed through, feature folder `Links/`.
- Minimal: static `ShortCode`, small validator, endpoint mapping. No repository/service layer, caching, rate limiting or analytics.
- Never log the raw destination URL. Only `Microsoft.AspNetCore.Mvc.Testing` pre-approved as a new package. No commits, no PR.

## Acceptance criteria (runnable)
- Unit: code length 7, alphabet Base62 → `GeneratedCodeIsSevenBase62Characters`
- Unit: distribution sanity → `ThousandGeneratedCodesAreDistinct` + `EveryAlphabetCharacterAppearsAcrossManyCodes` (5000 codes, all 62 characters appear)
- Unit: validator rejects `javascript:` / `data:` / `file:` / relative / userinfo URLs → `RejectsUnsafeOrMalformedUrls` (16 cases), plus length, alias and expiry tests
- Integration: 201 with body `{code, shortUrl, url, createdAt, expiresAt}` → `CreatesLinkWithGeneratedCode`, `CreatesLinkWithCustomAliasAndExpiry`
- Integration: 400 on bad URL → `RejectsUnsafeUrlWithFieldError` (asserts `errors` has exactly `url`)
- Integration: 409 on duplicate alias → `DuplicateCustomAliasReturnsConflict`
- Retry path covered by a test that forces a collision → `RetriesWithNewCodeWhenGeneratedCodeCollides` (+ mutation check below), `GivesUpAfterMaxGeneratedCodeCollisions`

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #3 per the issue and spec (validation rules, response shape, 409/500 behaviour, `TimeProvider`), hard rules from CLAUDE.md §6-§9, test list, checks to paste.
2. Subagent → advisor model (plan review before writing). Advice taken: hand-written validator as a pure function with camelCase keys; store the raw validated string and length-check that same string (not `AbsoluteUri`, which can grow past `nvarchar(2048)`); keep the tracked entity across retries; per-test `ApiFactory(connectionString, codes)` instead of a second class fixture; `new GenerateShortCode(...)` for DI registration; cover "distribution sanity" explicitly; no logging in this task.
3. Subagent → advisor model (malformed-JSON 500 finding). Advice taken: check whether the unconditional `UseExceptionHandler()` was the cause before choosing a fix; it was (see "Malformed JSON" below).
4. Subagent → nuget.org search + flat-container APIs for the one package added.

## Packages added (verified on nuget.org 2026-10-10)

| Package | Version | Owner(s) on nuget.org | Downloads | Why |
|---|---|---|---|---|
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.12 | aspnet, dotnetframework, Microsoft (verified prefix) | 402 M | `WebApplicationFactory<Program>` for in-memory endpoint tests. Latest stable 10.0.x, same patch as the EF packages. Integration test project only. Brings `Microsoft.AspNetCore.TestHost` 10.0.12 transitively |

```
$ dotnet list tests/UrlShortener.IntegrationTests package --include-transitive | grep -i "testhost\|mvc.testing"
   > Microsoft.AspNetCore.Mvc.Testing      10.0.12     10.0.12
   > Microsoft.AspNetCore.TestHost                                10.0.12
   > Microsoft.TestPlatform.TestHost                              17.14.1
```
Vulnerability scan run immediately after adding it: none (also in "Checks run").

## Decisions (for the engineer to question)
- **Hand-written validator, not `AddValidation()` + DataAnnotations.** Every rule that matters (scheme allow-list, no userinfo, non-empty host, whitespace/control chars, Base62 alias, reserved words, "in the future" against an injected clock) would need a custom attribute anyway, and the .NET 10 validation pipeline adds a source generator and endpoint filter for no gain. `CreateLinkValidator.Validate(request, now)` is a pure function, so the TDD table tests need no host. Error keys are the JSON names (`url`, `customAlias`, `expiresAt`); messages never echo the submitted value.
- **Scheme allow-list (`http`, `https`), not a deny-list.** `Uri.Scheme` is already lower-cased, so `HTTPS://Example.com/` is accepted (tested). The allow-list also covers a Linux-only trap: `Uri.TryCreate("/rooted/path", Absolute)` succeeds there as `file:///rooted/path`; it is rejected by scheme (test case `/rooted/path`, so CI on `ubuntu-latest` checks it).
- **Store the raw validated string, not `uri.AbsoluteUri`.** `AbsoluteUri` canonicalises (adds `/`, percent-encodes), which could push a 2040-char input past the 2048 column after validation passed. The length check runs on the stored string, and response `url` equals request `url`.
- **Reject whitespace and control characters** (added during TDD, test first). Probe output showed `Uri.TryCreate` accepts them and silently fixes them in the parsed `Uri`, but the raw string is what is stored and what G4 will send as `Location`:
  ```
  [ https://example.com/ ] -> True host='example.com' abs=[https://example.com/]
  [https://example.com/a\r\nSet-Cookie: x] -> True host='example.com' abs=[https://example.com/a%0D%0ASet-Cookie:%20x]
  [https://example.com/a b] -> True host='example.com' abs=[https://example.com/a%20b]
  [https://example.com/\t] -> True host='example.com' abs=[https://example.com/]
  [https://例子.测试/] -> True host='例子.测试' abs=[https://例子.测试/]
  ```
  (Kestrel would refuse a CR/LF header value rather than allow header injection, but that is a 500 at redirect time instead of a 400 at create time.) Clients must percent-encode.
- **Non-ASCII (IDN) URLs pass validation** (last probe line). Not changed here; G4 must decide how to write them into `Location` (e.g. `IdnHost` / `AbsoluteUri`), since Kestrel rejects non-ASCII header values by default.
- **Reserved aliases** `api`, `health`, `openapi`, `scalar`, `admin`, compared case-insensitively. `api` is unreachable today (min length 4 rejects it first); kept so the list documents the app's own route prefixes if the length rule changes. Day 3 expands the list.
- **`GenerateShortCode` delegate registered in DI** (default `ShortCode.Generate`), not an interface. It is the only seam that can force a collision: pre-inserting rows cannot collide with a random generator. The test factory swaps in a queue (`Clash01`, `Fresh01`) after pre-inserting `Clash01`.
- **Retry loop keeps the same tracked entity**: after a failed `SaveChangesAsync` it is still `Added`, so the loop assigns a new `Code` and saves again. Only `DbUpdateException` with an inner `SqlException` 2601/2627 is caught; anything else propagates.
- **After 5 collisions: `InvalidOperationException("No unique short code after 5 attempts.")` with the last `DbUpdateException` as inner**, left to the exception handler (500 ProblemDetails). At 62^7 ≈ 3.5e12 codes, five in a row means the generator is broken, so fail loudly instead of looping.
- **`TimeProvider.System` singleton; tests use a fixed-time subclass** (6 lines, no `Microsoft.Extensions.TimeProvider.Testing` package). `createdAt` is asserted exactly.
- **`shortUrl` = `{Request.Scheme}://{Request.Host}/{code}`** per the spec. With `AllowedHosts: "*"` the host is client-controlled, so a caller can get a `shortUrl` with any host in its own response. Harmless for the caller itself, but it should become a configured base URL or a restricted `AllowedHosts` before the Azure deploy (B6).
- **No logging added.** The spec said "if you log at all"; G5 owns logging. This removes the raw-URL-in-logs risk for this task.
- **`UseExceptionHandler()` outside Development only** (see next section).

## Malformed JSON: 500 in Development (found and fixed)
A temporary probe test (deleted afterwards, never committed) posted `{not json`. With `app.UseExceptionHandler()` registered unconditionally:
```
badjson: 500 application/problem+json
```
Cause: in Development `RouteHandlerOptions.ThrowOnBadRequest` is on, so a bad body throws `BadHttpRequestException` (status 400). The Developer Exception Page keeps that 400, but an unconditional `UseExceptionHandler()` sits inside it, catches first, and returns 500. Moved it inside `if (!app.Environment.IsDevelopment())` (the template shape). Re-probe:
```
500: 500 application/problem+json {"type":"...#section-15.6.1","title":"System.InvalidOperationException","status":500,"detail":"No unique short code after 5 attempts.",...}
409: 409 application/problem+json {"type":"...#section-15.5.10","title":"The custom alias is already in use.","status":409,"traceId":"..."}
badjson: 400 application/problem+json {"type":"...#section-15.5.1","title":"Microsoft.AspNetCore.Http.BadHttpRequestException","status":400,"detail":"Failed to read parameter \"CreateLinkRequest request\" f...
missing url: 400 {"type":"...#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"url":["Required."]},"traceId":"..."}
```
(Development detail is the Developer Exception Page; outside Development the 500 is the generic `"An error occurred while processing your request."` with no exception detail, as seen in the first probe.) Regression test `RejectsMalformedJsonWithBadRequest` asserts status only, because outside Development `ThrowOnBadRequest` is off and the 400 has an empty body; making that a ProblemDetails body (`UseStatusCodePages`) is left to G5, which owns ProblemDetails. Mutation check: restoring the unconditional handler fails that test:
```
  Failed UrlShortener.IntegrationTests.Links.CreateLinkEndpointTests.RejectsMalformedJsonWithBadRequest [564 ms]
Expected: BadRequest
Actual:   InternalServerError
```

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `tests/UrlShortener.UnitTests/Links/ShortCodeTests.cs` | generated (before the code) | Length/alphabet, 1000 distinct, all 62 characters appear in 5000 codes |
| `tests/UrlShortener.UnitTests/Links/CreateLinkValidatorTests.cs` | generated (before the code), then edited | Table tests for accepted/rejected URLs, exact max length vs +1, alias rules, expiry, all-fields-reported. Edited: 4 whitespace/control cases added test-first after the `Uri.TryCreate` probe |
| `src/UrlShortener.Api/Links/ShortCode.cs` | generated (skeleton throwing `NotImplementedException`, then implementation) | `RandomNumberGenerator.GetString(Base62, 7)` + `GenerateShortCode` delegate |
| `src/UrlShortener.Api/Links/CreateLinkValidator.cs` | generated (skeleton returning no errors, then implementation), then edited | Edited: whitespace/control-character rule |
| `src/UrlShortener.Api/Links/CreateLinkEndpoint.cs` | generated | Request/response records + handler: validate, insert, catch 2601/2627, 409 for alias, retry generated code up to 5 |
| `src/UrlShortener.Api/Links/CreateLink.cs` | generated, then deleted | Temporary home of `CreateLinkRequest` for the TDD skeleton; moved into `CreateLinkEndpoint.cs` |
| `tests/UrlShortener.IntegrationTests/ApiFactory.cs` | generated | `WebApplicationFactory<Program>` per test: `UseSetting` connection string (host setting, visible before the host builds), fixed `TimeProvider`, optional scripted codes |
| `tests/UrlShortener.IntegrationTests/Links/CreateLinkEndpointTests.cs` | generated, then edited | 7 tests. Edited: `RejectsMalformedJsonWithBadRequest` added after the probe finding |
| `src/UrlShortener.Api/Program.cs` | edited | `AddProblemDetails`, `TimeProvider.System`, `GenerateShortCode`, non-Development `UseExceptionHandler`, `MapGroup("/api/links").MapPost("/", ...)` |
| `Directory.Packages.props`, `tests/UrlShortener.IntegrationTests/UrlShortener.IntegrationTests.csproj` | edited | `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 |
| `CLAUDE.md` §9 | edited | Two gotchas (below) |
| (temporary) `TempProbeTests.cs` | generated, then deleted | Printed status/body for 500, 409, malformed JSON, missing url. Evidence only |
| (proposal) `AddValidation()` + DataAnnotations | rejected | Custom attributes for nearly every rule; harder to unit test |
| (proposal) store `uri.AbsoluteUri` | rejected | Can exceed the column after validation; changes what the user submitted |
| (proposal) pre-insert rows to force a collision | rejected | Cannot collide with a random generator; the delegate seam can |
| (proposal) `ExceptionHandlerOptions.StatusCodeSelector` for `BadHttpRequestException` | rejected | The template shape (handler outside Development) fixes the cause; Production empty-body 400 is G5's ProblemDetails work |

## TDD sequence (real output)

**Unit red 1: tests written first, no production types**
```
$ dotnet build --nologo -v q tests/UrlShortener.UnitTests
    0 Warning(s)
    25 Error(s)
CreateLinkValidatorTests.cs(101,27): error CS0246: The type or namespace name 'CreateLinkRequest' could not be found
CreateLinkValidatorTests.cs(15,22): error CS0103: The name 'CreateLinkValidator' does not exist in the current context
ShortCodeTests.cs(12,20): error CS0103: The name 'ShortCode' does not exist in the current context
...
```

**Unit red 2: skeletons compile (`Generate()` throws, `Validate()` returns no errors)**
```
$ dotnet test --nologo tests/UrlShortener.UnitTests
  Failed UrlShortener.UnitTests.Links.ShortCodeTests.GeneratedCodeIsSevenBase62Characters
  Error Message:
   System.NotImplementedException : The method or operation is not implemented.
  Failed UrlShortener.UnitTests.Links.CreateLinkValidatorTests.RejectsUnsafeOrMalformedUrls(url: "ftp://x")
  Error Message:
   Assert.Equal() Failure: Collections differ
  Failed ...RejectsUnsafeOrMalformedUrls(url: "javascript:alert(1)")
  Failed ...RejectsUnsafeOrMalformedUrls(url: "https://user:pw@example.com/")
  Failed ...RejectsInvalidOrReservedAlias(alias: "HEALTH")
  Failed ...RejectsExpiryNowOrInThePast(offsetSeconds: 0)
  ... (32 failures in total)
Failed!  - Failed:    32, Passed:     8, Skipped:     0, Total:    40, Duration: 517 ms - UrlShortener.UnitTests.dll (net10.0)
```
The 8 passing are the "accepts" tests, which an always-empty validator trivially passes.

**Unit green 1**
```
Passed!  - Failed:     0, Passed:    40, Skipped:     0, Total:    40, Duration: 294 ms - UrlShortener.UnitTests.dll (net10.0)
```

**Unit red 3: whitespace/control cases added after the probe**
```
  Failed ...RejectsUnsafeOrMalformedUrls(url: "https://example.com/a b")
  Failed ...RejectsUnsafeOrMalformedUrls(url: " https://example.com/")
  Failed ...RejectsUnsafeOrMalformedUrls(url: "https://example.com/a\r\nSet-Cookie: x=1")
  Failed ...RejectsUnsafeOrMalformedUrls(url: "https://example.com/\t")
Failed!  - Failed:     4, Passed:    40, Skipped:     0, Total:    44, Duration: 128 ms - UrlShortener.UnitTests.dll (net10.0)
```

**Unit green 2**
```
Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 373 ms - UrlShortener.UnitTests.dll (net10.0)
```

**Integration red: tests + factory written before the endpoint**
```
$ dotnet build --nologo -v q tests/UrlShortener.IntegrationTests
Links\CreateLinkEndpointTests.cs(105,50): error CS0103: The name 'CreateLinkEndpoint' does not exist in the current context
Links\CreateLinkEndpointTests.cs(21,61): error CS0246: The type or namespace name 'LinkResponse' could not be found
Links\CreateLinkEndpointTests.cs(47,61): error CS0246: The type or namespace name 'LinkResponse' could not be found
Links\CreateLinkEndpointTests.cs(97,61): error CS0246: The type or namespace name 'LinkResponse' could not be found
```

**Integration green** (first run after the endpoint, 3 persistence + 6 endpoint tests)
```
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Mutation check: the retry test depends on the retry.** `MaxGeneratedCodeAttempts` temporarily set to 1, then restored:
```
  Failed UrlShortener.IntegrationTests.Links.CreateLinkEndpointTests.RetriesWithNewCodeWhenGeneratedCodeCollides [360 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: Created
Failed!  - Failed:     1, Passed:     8, Skipped:     0, Total:     9, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

## Checks run
```
$ dotnet build --nologo -v q
    0 Warning(s)
    0 Error(s)

$ dotnet format --verify-no-changes --no-restore; echo $?
0

$ dotnet test --nologo; echo $?
Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 463 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
0

$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.

$ git status --short
 M Directory.Packages.props
 M src/UrlShortener.Api/Program.cs
 M tests/UrlShortener.IntegrationTests/UrlShortener.IntegrationTests.csproj
?? src/UrlShortener.Api/Links/CreateLinkEndpoint.cs
?? src/UrlShortener.Api/Links/CreateLinkValidator.cs
?? src/UrlShortener.Api/Links/ShortCode.cs
?? tests/UrlShortener.IntegrationTests/ApiFactory.cs
?? tests/UrlShortener.IntegrationTests/Links/CreateLinkEndpointTests.cs
?? tests/UrlShortener.UnitTests/Links/
```
(`CLAUDE.md` and this log were written after the status snapshot and also show as modified/untracked.)

## Gotchas encountered
Added to CLAUDE.md §9: `Uri.TryCreate` accepts and silently fixes whitespace/CR/LF; an unconditional `UseExceptionHandler()` turns Development's `BadHttpRequestException` 400 into 500.

## Open items
- G4: how to emit non-ASCII (IDN) destinations in `Location`.
- G5: ProblemDetails body for framework 400s outside Development (`UseStatusCodePages`).
- B6: `shortUrl` built from the client `Host` header with `AllowedHosts: "*"`; use a configured base URL or restrict hosts.
- `/security-review` and a fresh-context `/code-review` before merge (high-impact: destination-URL validation).
- `docs/HANDOFF.md` not updated by the subagent (coordinator's end-of-session step).

## Sign-off
(pending engineer review of the PR: destination-URL validation)

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`); the engineer signs off in the PR. Gates re-run independently: build 0/0, format exit 0, 44 unit + 10 integration passed, no vulnerable packages.

**`/security-review` (fresh Opus agent):** no HIGH/MEDIUM findings. Candidates rejected with reasons: Host-header reflection in `shortUrl` (affects only the forger's own response; hardening tracked as #9), scheme allow-list bypasses (`Uri.Scheme` normalises; `https:\evil.com` resolves the same in `System.Uri` and browsers), CRLF via stored URL (all non-printable rejected on the raw string; `%0d%0a` never decoded), IDN/homograph (inherent to shorteners), 409 as alias oracle (aliases are public by design), SQLi/randomness/error leakage (EF parameterised, CSPRNG, no echo of input).

**`/code-review medium` (fresh context): 6 findings, triaged:**

| Finding | Outcome | What changed |
|---|---|---|
| Non-ASCII URLs pass validation but Kestrel rejects them in a `Location` header (G4 would 500 on an accepted link) | **fixed** | Validator now requires printable ASCII (`'!'..'~'`) on the raw string, replacing the whitespace/control rule it subsumes. 3 unit cases added (`café`, IDN host, NBSP). Message tells clients to percent-encode / use punycode |
| `GivesUpAfterMaxGeneratedCodeCollisions` cannot detect an off-by-one in the attempt count | **fixed** | `ApiFactory` counts codes requested; test supplies one spare unique code (over-retry would yield 201) and asserts exactly `MaxGeneratedCodeAttempts` were consumed |
| Non-Development error pipeline (`UseExceptionHandler`) has no test coverage | deferred → #5 | G5 owns global ProblemDetails; acceptance criterion added: a `UseEnvironment("Production")` test |
| Malformed JSON outside Development returns a bare 400 (breaks "ProblemDetails for all errors") | deferred → #5 | Added to #5 with the `StatusCodeSelector` / `UseStatusCodePages` fix |
| New `WebApplicationFactory` per test re-runs the startup migration | noted → #5 | Correct but slow; shared factory for tests without scripted codes suggested in #5 |
| `shortUrl` from client `Host` with `AllowedHosts "*"` | deferred → **#9 (new, Day 2, high-impact)** | Configured `PublicBaseUrl` option + `AllowedHosts` + forwarded headers before the Azure deploy |

Agent decisions accepted as-is: store the raw validated string (not `AbsoluteUri`), hand-written validator over `AddValidation()`, delegate seam for collisions, `api` kept in the reserved list for documentation even though the 4-char minimum rejects it first.

```
$ dotnet build --nologo -v q
    0 Warning(s)   0 Error(s)
$ dotnet format --verify-no-changes --no-restore; echo $?
0
$ dotnet test --no-build --nologo
Passed!  - Failed: 0, Passed: 47, Total: 47 - UrlShortener.UnitTests.dll
Passed!  - Failed: 0, Passed: 10, Total: 10 - UrlShortener.IntegrationTests.dll
```
