# AI log 019 — expiry-safety

| Field | Value |
|---|---|
| Issue | #38 |
| Branch | `feat/38-expiry-safety` |
| Date | 2026-10-11 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent; the coordinator reviews, runs the gates, commits) |
| Risk level | high-impact (redirect semantics, destination-URL validation) |

## Intent
Implement ADR-0007: expired and deleted links answer 410 on the redirect, and create rejects destinations that point at the clicker's own network, at `localhost`, at this service, or at a denylisted host.

## Constraints given to the AI
No new packages, no schema change, no migration, no network call on the create path. `CreateLinkValidator` stays a pure static function (public host passed in). `IPAddress` APIs, not string prefixes. Reputation seam about 40 lines, no HTTP client. TDD per rule with the red run recorded. New integration tests go into existing classes (one SQL Server container per class, CLAUDE.md §9). Smallest reversible implementation; stop and report anything that grows past ADR-0007.

## Acceptance criteria (runnable)
1. Redirect: expired (`ExpiresAt <= now`) and deleted → 410, ProblemDetails, `Cache-Control: no-store`, no click enqueued; unknown → 404; unexpired → 302 + `private, no-store`. One query.
2. `GET /api/links/{code}` and `/stats`: expired → 200 with the past `expiresAt`; deleted → 404.
3. Create → 400 under `url` (value not echoed) for non-public IP literals, `localhost`/`*.localhost`, the service's own host; reserved aliases extended; odd IPv4 forms pinned by a test.
4. `IUrlReputationChecker` + `Safety:DeniedHosts` denylist, wired into create.
5. Red run recorded per rule.
6. The deleted → 404 redirect tests change to 410.
7. Docs: SETUP, TESTING, scenarios/ambiguous.md validation section, ADR-0007 consequences if a finding changes them, CLAUDE.md §9.

## Prompts (summarised, secrets redacted)
1. Coordinator: issue #38 text, CLAUDE.md, the AI-log template; implement on `feat/38-expiry-safety`, do not commit.
2. Reviewer (advisor model) before coding: a third test asserts deleted → 404 on the redirect (`ClickAnalyticsTests.MissDoesNotRecordAClick`); add the parameter before the rules so the rule tests can go red; `ApiFactory`'s base URL is `http://localhost`, so the self-host integration test needs its own base URL; read what `Uri` does with odd IPv4 forms before writing any gap text.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `src/UrlShortener.Api/Redirect/RedirectEndpoint.cs` | edited | One query without the `DeletedAt == null` filter, projecting `ExpiresAt`/`DeletedAt`; deleted or `ExpiresAt <= now` → `TypedResults.Problem(410)` + `Cache-Control: no-store`, returned before the click is enqueued. One title for both ("expired or been deleted"): ADR-0007 does not ask to tell them apart. `now` is read once and reused for the click. Removed the "Expiry is not checked yet" comment. |
| `src/UrlShortener.Api/Links/CreateLinkValidator.cs` | edited | New required `publicHost` parameter (pure function kept, no options injected). `HostError`: IP literals parsed from `IdnHost` (no brackets) with `IPAddress.TryParse`, IPv4-mapped → `MapToIPv4`, IPv4 ranges by byte checks, IPv6 by `IsLoopback`/`IPv6Any`/`IsIPv6LinkLocal`/`IsIPv6UniqueLocal`; a literal that does not parse is rejected (fails closed). `localhost`/`*.localhost` and own host compared after trimming a trailing dot. Own host: **host only, port and scheme ignored** (simpler; another port on our host is still us). Eight reserved aliases added; comment updated. |
| `src/UrlShortener.Api/Links/CreateLinkEndpoint.cs` | edited | Passes `new Uri(BaseUrl).Host` to the validator; calls `IUrlReputationChecker` after validation (so it only sees a well-formed http(s) URL); denied → `ValidationProblem` under `url`, message "Is not allowed.". |
| `src/UrlShortener.Api/Safety/SafetyOptions.cs` | generated | `Safety:DeniedHosts` (`string[]`, empty default). |
| `src/UrlShortener.Api/Safety/UrlReputationChecker.cs` | generated | `IUrlReputationChecker.IsDeniedAsync(Uri, CancellationToken)` + `DenylistReputationChecker`: exact host or subdomain, case-insensitive, trailing dot trimmed on both sides. 30 lines with the options class. |
| `src/UrlShortener.Api/Program.cs` | edited | Registers `SafetyOptions` (`ValidateDataAnnotations().ValidateOnStart()`, which has nothing to check today; kept for the §7 pattern, said in a comment) and the checker as a singleton. |
| `tests/UrlShortener.UnitTests/Links/CreateLinkValidatorTests.cs` | edited | Every call passes `PublicHost = "short.example"` (not `example.com`, which the existing tests use as a destination). New: IP rejects (20 cases incl. IPv4-mapped, zone ID, shorthand IPv4) and range-edge accepts (15), shorthand-IPv4 pin, `localhost` rejects/look-alike accepts, own host rejects (port, case, trailing dot) and subdomain accept, messages do not echo the URL, 8 reserved aliases. |
| `tests/UrlShortener.UnitTests/Safety/DenylistReputationCheckerTests.cs` | generated | Listed host and subdomains denied; look-alikes (`notevil.example`, `evil.example.com`, host in the path) allowed; empty list allows all. |
| `tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs` | edited | `SoftDeletedCodeIsNotFound` → `SoftDeletedCodeIsGoneWithNoStore` (engineer's decision in ADR-0007: deleted is 410 like expired). New `CodeExpiringNowIsGoneWithNoStore` (boundary), `CodeExpiringAfterNowStillRedirects`. Shared assertion: 410, `application/problem+json`, no `Location`, `no-store`. |
| `tests/UrlShortener.IntegrationTests/Links/LinkEndpointsTests.cs` | edited | `DeleteSoftDeletesSoGetAndRedirectAreNotFound` → `DeleteSoftDeletesSoGetIsNotFoundAndRedirectIsGone` (same reason). New `GetExpiredLinkReturnsMetadataWithPastExpiry`. |
| `tests/UrlShortener.IntegrationTests/Analytics/ClickAnalyticsTests.cs` | edited | `MissDoesNotRecordAClick` → `MissOrGoneLinkDoesNotRecordAClick`: the deleted link now expects 410, and an expired link (`Click08`) is added and must record no click. **This third deleted → 404 assertion was not listed in the issue** (found by the reviewer, line 153). |
| `tests/UrlShortener.IntegrationTests/Analytics/LinkStatsTests.cs` | edited | New `StatsForExpiredLinkAreReturned`; insert helper takes `expiresAt`. |
| `tests/UrlShortener.IntegrationTests/Links/CreateLinkEndpointTests.cs` | edited | New `RejectsLocalNetworkAndOwnHostDestinations` (base URL `https://short.example`, so the own-host case is not hidden behind the `localhost` rule) and `RejectsDeniedHostButCreatesOthers` (`Safety:DeniedHosts:0`). No new test classes (one container per class, §9). |
| `docs/SETUP.md`, `docs/TESTING.md`, `docs/scenarios/ambiguous.md`, `CLAUDE.md` §9 | edited | Walkthrough: deleted → 410, metadata 404, a rejected metadata-address create; "not enforced yet" sentence replaced. Coverage rows. Validation table with test names and red runs. §9: what `Uri` does with shorthand IPv4, IPv6 brackets/zone IDs and trailing dots. |
| (proposal) separate titles for expired vs deleted 410 | rejected | Not asked for by ADR-0007; one place to change if wanted. |
| (proposal) "expired" curl example in SETUP | rejected | The API only accepts a future `expiresAt`, so an expired link takes waiting (or a SQL update); one sentence says so instead. |
| (proposal) CGNAT 100.64/10, multicast, broadcast, NAT64/6to4/IPv4-compatible IPv6 | not built | Outside ADR-0007's list; reported as a question below. |
| (proposal) edit ADR-0003 ("soft-deleted → 404") | not done | ADR-0007 supersedes that line; whether to annotate an accepted ADR is the engineer's call (question below). |
| (proposal) ADR-0007 consequences on shorthand IPv4 | not needed | `Uri` normalises them, so the rule covers them and the consequences stay as written. |

## Red runs (TDD)
Every rule test was written before the code and run red. Unit (validator, with the parameter added but no rule logic):
```
$ dotnet test tests/UrlShortener.UnitTests -c Release --no-build
[FAIL] CreateLinkValidatorTests.RejectsInvalidOrReservedAlias(alias: "docs")            (8 new aliases, all failed)
[FAIL] CreateLinkValidatorTests.RejectsLoopbackPrivateLinkLocalAndUnspecifiedIpLiterals(url: "http://169.254.169.254/latest/meta-data/")   (19 of 19 failed)
[FAIL] CreateLinkValidatorTests.RejectsOwnPublicHost(url: "http://SHORT.Example:8443/x")   (3 of 3)
[FAIL] CreateLinkValidatorTests.RejectsLocalhostNames(url: "http://localhost./")            (5 of 5)
[FAIL] CreateLinkValidatorTests.HostRuleMessagesDoNotEchoTheUrl(url: "http://10.0.0.1/secret-path")
   System.Collections.Generic.KeyNotFoundException : The given key 'url' was not present in the dictionary.
Failed!  - Failed:    38, Passed:    69, Skipped:     0, Total:   107
```
`UriNormalisesShorthandIpv4ToDottedQuad` passed in that same run: it pins framework behaviour, not code of ours. The accept tests (range edges, look-alikes) also passed; they guard against a rule written too wide.

Unit (denylist, with a stub returning false):
```
[FAIL] DenylistReputationCheckerTests.DeniesListedHostAndItsSubdomains(url: "https://bad.example/")
   Assert.True() Failure
Expected: True
Actual:   False
(the stub returns false for every host; 4 [FAIL] lines were shown before `head -14` cut the output, the run ended "exit 1")
```

Integration (tests written, endpoints unchanged):
```
$ dotnet test tests/UrlShortener.IntegrationTests -c Release --no-build
[FAIL] CreateLinkEndpointTests.RejectsDeniedHostButCreatesOthers        Expected: BadRequest  Actual: Created
[FAIL] RedirectEndpointTests.SoftDeletedCodeIsGoneWithNoStore           Expected: Gone  Actual: NotFound
[FAIL] RedirectEndpointTests.CodeExpiringNowIsGoneWithNoStore           Expected: Gone  Actual: Found
[FAIL] ClickAnalyticsTests.MissOrGoneLinkDoesNotRecordAClick            Expected: Gone  Actual: NotFound
[FAIL] LinkEndpointsTests.DeleteSoftDeletesSoGetIsNotFoundAndRedirectIsGone   Expected: Gone  Actual: NotFound
Failed!  - Failed:     5, Passed:    57, Skipped:     0, Total:    62
```
The first run of `MissOrGoneLinkDoesNotRecordAClick` failed for the wrong reason (duplicate key: `Click07` is already used in the class); renamed to `Click08` and rerun red: `Expected: Gone, Actual: NotFound`.
Green without a red, stated plainly: `RejectsLocalNetworkAndOwnHostDestinations` (the validator was already wired through the endpoint when it was written; the rules themselves went red at unit level), `GetExpiredLinkReturnsMetadataWithPastExpiry` and `StatsForExpiredLinkAreReturned` (characterisation: those endpoints never checked expiry).
The `[fe80::1%25eth0]` case was added after green, once a probe showed a zone ID stays in `IdnHost`; it passes (link-local).

Mutation checks on the redirect (each reverted afterwards):
```
--- mutation 1: ExpiresAt < now instead of <=
[FAIL] RedirectEndpointTests.CodeExpiringNowIsGoneWithNoStore
Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7
--- mutation 2: enqueue the click before the 410 check
[FAIL] ClickAnalyticsTests.MissOrGoneLinkDoesNotRecordAClick
   Assert.Equal() Failure: Values differ
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

`Uri` probe (`dotnet fsi`, .NET 10):
```
http://[::1]/ | Host=[::1] | IdnHost=::1 | DnsSafeHost=::1 | IPv6
http://[fe80::1%25eth0]/ | Host=[fe80::1] | IdnHost=fe80::1%25eth0 | DnsSafeHost=fe80::1%25eth0 | IPv6
http://localhost./ | Host=localhost. | IdnHost=localhost. | DnsSafeHost=localhost. | Dns
http://SHORT.Example:8443/ | Host=short.example | IdnHost=short.example | DnsSafeHost=short.example | Dns
```

## Checks run
Baseline before any change:
```
$ dotnet build -c Release
    0 Error(s)
$ dotnet test -c Release --no-build
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 109 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54, Duration: 22 s - UrlShortener.IntegrationTests.dll (net10.0)
```

After the change (all four gates):
```
$ dotnet build -c Release
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.63
exit=0
$ dotnet format --verify-no-changes
exit=0
$ dotnet test -c Release --no-build
Passed!  - Failed:     0, Passed:   119, Skipped:     0, Total:   119, Duration: 485 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    62, Skipped:     0, Total:    62, Duration: 26 s - UrlShortener.IntegrationTests.dll (net10.0)
exit=0
$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.
exit=0
```

## Decisions for the engineer to question
1. **Own host ignores port and scheme.** `http://short.example:8443/x` is rejected when the base URL is `https://short.example`. Simpler, and a link to another port on our host is still a link to us. Exact host only: `www.short.example` is accepted.
2. **One 410 title for expired and deleted** ("This short link has expired or been deleted."). Telling them apart would reveal whether the owner deleted it; ADR-0007 does not ask for it.
3. **IP literal that `IPAddress.TryParse` cannot parse is rejected** (fails closed). Not expected to happen once `Uri` has classified the host as IPv4/IPv6; it costs no extra code.
4. **Ranges not covered** (ADR-0007 does not list them): CGNAT 100.64.0.0/10, multicast 224/4, broadcast 255.255.255.255, IPv6 forms that embed IPv4 other than IPv4-mapped (NAT64 `64:ff9b::/96`, 6to4 `2002::/16`, deprecated IPv4-compatible `::a.b.c.d`). Each is one line if wanted.
5. **Reserved aliases added:** `docs`, `login`, `logout`, `static`, `assets`, `status`, `metrics`, `swagger`. Dotted paths (`robots.txt`, `favicon.ico`) never match the redirect route, so they need no entry. `api` stays although 4+ characters makes it unreachable.
6. **Denylist entries are not validated** (no host-format check): a malformed entry simply never matches. `ValidateDataAnnotations` is registered for consistency but has no attributes to check.
7. **ADR-0003 still says soft-deleted → 404.** ADR-0007 supersedes that line; not edited, since changing an accepted ADR is the engineer's call (a "superseded in part by ADR-0007" note would be the usual fix).
8. **Rate limiting in tests:** each new create test makes at most 2 POSTs on its own factory, well under the default 10/minute.

## Gotchas encountered
- Added to §9: `Uri` normalises shorthand IPv4 to a dotted quad; IPv6 `Host` has brackets and drops the zone ID while `IdnHost` keeps it; a trailing dot stays in `Host`.
- Not a §9 item, but cost a rerun: integration test classes share one database, so a new link code must be unique within the class (`Click07` was taken).

## Coordinator review
Opus 5.5 (coordinator), after the subagent's hand-back. Diff read in full; gates re-run (build 0/0, format exit 0, 119 unit + 62 integration, no vulnerable packages) before committing `c5797a3` and launching both reviews on it.

**`/security-review` (fresh Opus agent, committed diff): 1 HIGH finding, fixed.**
- **Bypass with a trailing dot.** `System.Uri` classifies `127.0.0.1.` as `HostNameType.Dns`, so the IP branch was skipped. The name branch trimmed the dot only for the `localhost` and own-host comparisons, so `http://169.254.169.254./`, `http://192.168.1.1.:8080/`, `http://127.1./`, `http://2130706433./`, `http://0x7f.0.0.1./` and `http://0./` were accepted (201). Browsers (WHATWG URL) drop the dot and go to the address. The reviewer proved it with a scratch console app running the exact `HostError` logic.
- **Fix:** `HostError` re-parses the host without its trailing dots and uses the re-parsed host type.
- **Hardening, same pass:** an IPv6 zone ID is cleared before the range checks (`[::1%251]` made `IsLoopback` false; browsers reject zone IDs, so it was not exploitable).
- **TDD:** `RejectsIpLiteralsHiddenByTrailingDotOrZoneId` (8 cases) was written first. Red: `Failed: 8, Passed: 0`.
- **A mistake of mine during the fix:** the first version read `address.ScopeId` on IPv4, which throws `SocketException: The attempted operation is not supported for the type of object referenced`. 32 unit tests caught it, including every IPv4 accept and reject case. It is now guarded by `AddressFamily`.
- **Other things the reviewer checked and found fine:** shorthand, octal and hex forms without a dot; percent-encoded and non-ASCII hosts; IPv4-mapped addresses; the own-host rule; denylist suffix matching; the 410 body and headers.

**Fresh-context `/code-review`: 7 findings → 1 duplicate (fixed above), 4 fixed, 1 documented, 1 left.**

| # | Finding | Outcome |
|---|---|---|
| 1 | Trailing dot bypass | Same as the security finding; fixed. CLAUDE.md §9 entry added. |
| 2 | ADR-0003 says deleted → 404 and cites the renamed test | **Fixed:** status line now "superseded in part by ADR-0007"; history not rewritten (MADR). |
| 3 | `plan.md` says "no-op default + denylist" | **Left:** the plan is the approved historical plan; ADR-0007 is the record. |
| 4 | `Safety:DeniedHosts` entries never validated (`*.evil.example`, `https://evil.example` start the app and never match; Unicode never matches `IdnHost`) | **Fixed:** `SafetyOptions.IsValidHost` (ASCII, `Uri.CheckHostName` is Dns or IPv4) in `.Validate(...).ValidateOnStart()`, replacing a `ValidateDataAnnotations` that had nothing to check. Tests `RejectsEntriesThatCouldNeverMatch` (6 cases) and `AcceptsPlainHostEntries` (4). |
| 5 | CGNAT, broadcast, multicast and IPv4-embedding IPv6 forms are accepted | **Documented:** added to ADR-0007's "not built" list. Each is a one-line rule; none is in the threat model's examples. |
| 6 | Own-host rule compared `Host` (config) with `IdnHost` (destination) | **Fixed:** `IdnHost` on both sides. The missing test pinning the order of validation before reputation is left: the order is visible in four lines. |
| 7 | `AssertGoneProblemWithNoStore` did not read the body | **Fixed:** it asserts `status` 410 and the endpoint's title, so a bare 410 filled in by `UseStatusCodePages` would fail. |

Final gates after the fixes:
```
$ dotnet build -c Release --nologo -v q
    0 Warning(s)
    0 Error(s)
$ dotnet format --verify-no-changes --no-restore; echo $?
0
$ dotnet test -c Release --no-build --nologo
Passed!  - Failed: 0, Passed: 137, Total: 137 - UrlShortener.UnitTests.dll
Passed!  - Failed: 0, Passed: 62, Total: 62 - UrlShortener.IntegrationTests.dll
```
An earlier run showed `2 Error(s)` and spurious failures while the code-review agent was building the same checkout at the same time (file locks). A clean rebuild had 0 errors. The lesson: don't run gates while a review agent builds the same tree.

## Sign-off
Reviewed the full diff, understood it, accept ownership. — <name>, YYYY-MM-DD
