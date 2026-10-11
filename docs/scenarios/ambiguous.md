# Scenario 3: Ambiguous, "links should expire and be safe"

Day 3. The requirement is one sentence with two undefined words. The work here is to make the ambiguity explicit, get the engineer's decisions on what changes behaviour, record the rest as reversible assumptions, and only then build the smallest thing that satisfies them. Decision record: [ADR-0007](../adr/0007-expiry-and-safety.md).

## Requirement
"Links should expire and be safe". Nothing says what expiry looks like to a caller, who sets it, what happens afterwards, or what "safe" protects against.

## What already exists
- `POST /api/links` accepts an optional `expiresAt`, validated as "in the future". It is stored (`Links.ExpiresAt`, `datetimeoffset`) and returned, but **never checked**: the redirect follows an expired link.
- Deleted links (soft delete) answer **404** on the redirect, `GET /api/links/{code}` and `/stats`.
- `CreateLinkValidator` rejects URLs over 2048 characters, non-printable-ASCII, non-http(s), missing host, and user info, plus five reserved aliases.

## Questions, and who answered them

| # | Question | Options | Answer | By |
|---|---|---|---|---|
| 1 | What does a redirect to an expired link return? | 404 / 410 | **410 Gone**, ProblemDetails, `no-store` | Planned in CLAUDE.md §7, confirmed |
| 2 | What does a deleted link return? | 410 for both / 410 expired, 404 deleted | **410 for both** (changes two existing tests) | Engineer, 2026-10-11 |
| 3 | Default expiry when `expiresAt` is omitted? | none / 1 year | **None** | Engineer, 2026-10-11 |
| 4 | What does "safe" cover? | minimal rules + seam / + DNS resolution / + external reputation API | **Minimal rules + seam** | Engineer, 2026-10-11 |
| 5 | Does an expired redirect record a click? | no / yes, flagged | **No** | Engineer, 2026-10-11 |
| 6 | When exactly is a link expired? | `<` / `<=` | `ExpiresAt <= now` (matches the validator) | Assumption |
| 7 | Do metadata and stats still answer for an expired link? | yes / 410 there too | **Yes**, 200 with the past `expiresAt` | Assumption |
| 8 | Are expired rows purged, and can codes be reused? | purge / keep | **Keep; never reuse codes** | Assumption |
| 9 | Can a link's expiry or destination change? | immutable / `PATCH` | **Immutable** | Assumption |
| 10 | Is "safe" about the server fetching URLs (SSRF)? | — | **No.** The service never fetches a destination; the risk is to the person who clicks. | Threat model |

The assumptions (6–9) are the cheapest to reverse. ADR-0007 says where each one lives.

## Threat model for "safe"
1. **A malicious destination** (phishing, malware) → reputation seam with a configurable denylist.
2. **A destination on the clicker's own network** (router page, `localhost`, cloud metadata on 169.254.169.254) → reject private, loopback, link-local and unspecified IP literals, and `localhost`.
3. **The shortener pointing at itself** (loops, chains that hide the final destination) → reject our own public host.
4. **An alias that takes a served path** → a longer reserved list.

Deliberately out of scope, each with its reason in ADR-0007: DNS resolution at create time, an external reputation API, homograph detection, re-checking at redirect time, blocking other shorteners.

## Plan for A2 (smallest reversible implementation)
1. Redirect: 410 + `no-store` for expired and deleted (TDD: red first). No click enqueued for either.
2. Validator: IP-literal, `localhost` and self-host rules, plus the reserved aliases (TDD per rule, pure-function unit tests).
3. `IUrlReputationChecker` + denylist implementation (`Safety:DeniedHosts`), wired into create.
4. Docs: SETUP walkthrough (deleted → 410), TESTING coverage rows, CLAUDE.md §9 for anything surprising.
5. `/security-review` and a fresh-context `/code-review`, then the engineer's sign-off (destination-URL validation is high-impact).

## Execution

| Step | Issue → PR | Who | Result |
|---|---|---|---|
| A1: questions, answers, ADR-0007 | #36 → #37 | Coordinator drafts; engineer answers the four questions that change behaviour | This page and [ADR-0007](../adr/0007-expiry-and-safety.md); [AI log 018](../ai-log/018-expiry-safety-decisions.md) |
| A2: implementation | #38 → #39 | Fresh Opus subagent, test first for every rule; coordinator review and gates; then `/security-review` and a fresh `/code-review` | 137 unit + 62 integration; one HIGH bypass found by the security review and fixed test first; [AI log 019](../ai-log/019-expiry-safety.md) |
| A3: perf gate | folded in | Coordinator | The Azure measurement (#35) stands in for it; no cache exists to compare on and off ([summary](../ENGINEERING_SUMMARY.md#performance)) |
| A4: final docs | #40 → #41 | Fresh Opus subagent, coordinator spot-checks against sources | [`ARCHITECTURE.md`](../ARCHITECTURE.md), [`ENGINEERING_SUMMARY.md`](../ENGINEERING_SUMMARY.md) |

## Validation (filled in by A2)
Implementation: issue #38, [AI log 019](../ai-log/019-expiry-safety.md). Each rule's test was written first and run red; the failure lines are in the AI log.

| Decision | Tests | Red run (before the code) |
|---|---|---|
| Expired → 410 + `no-store`, boundary `<=` | `RedirectEndpointTests.CodeExpiringNowIsGoneWithNoStore`, `CodeExpiringAfterNowStillRedirects` | `Expected: Gone, Actual: Found` |
| Deleted → 410 (was 404) | `RedirectEndpointTests.SoftDeletedCodeIsGoneWithNoStore`, `LinkEndpointsTests.DeleteSoftDeletesSoGetIsNotFoundAndRedirectIsGone` | `Expected: Gone, Actual: NotFound` |
| No click for a 410 | `ClickAnalyticsTests.MissOrGoneLinkDoesNotRecordAClick` | `Expected: Gone, Actual: NotFound` |
| Metadata and stats still answer for expired | `LinkEndpointsTests.GetExpiredLinkReturnsMetadataWithPastExpiry`, `LinkStatsTests.StatsForExpiredLinkAreReturned` | none: already true (the endpoints never checked expiry); characterisation tests |
| Non-public IP literals rejected | `CreateLinkValidatorTests.RejectsLoopbackPrivateLinkLocalAndUnspecifiedIpLiterals` (+ `AcceptsPublicIpLiterals` for the range edges) | 19 cases: `KeyNotFoundException`/no `url` error |
| IPv4 literal hidden by a trailing dot (`127.0.0.1.`, `169.254.169.254.`), IPv6 zone ID | `CreateLinkValidatorTests.RejectsIpLiteralsHiddenByTrailingDotOrZoneId` | found by the security review after A2; `Failed: 8, Passed: 0` before the fix |
| Shorthand IPv4 (`2130706433`, `0x7f.1`, `127.1`) | `CreateLinkValidatorTests.UriNormalisesShorthandIpv4ToDottedQuad` | passed on first run: `Uri` already normalises them to `127.0.0.1`, so the IP rule covers them; no gap to document |
| `localhost`, `*.localhost` | `CreateLinkValidatorTests.RejectsLocalhostNames`, `AcceptsNamesThatOnlyContainLocalhost` | 5 cases failed |
| Own public host | `CreateLinkValidatorTests.RejectsOwnPublicHost`, `AcceptsSubdomainOfOwnPublicHost` | 3 cases failed |
| Messages do not echo the URL | `CreateLinkValidatorTests.HostRuleMessagesDoNotEchoTheUrl` | 3 cases failed |
| Reserved aliases | `CreateLinkValidatorTests.RejectsInvalidOrReservedAlias` (8 new cases) | 8 cases failed |
| Reputation seam (denylist) | `DenylistReputationCheckerTests.*`, `CreateLinkEndpointTests.RejectsDeniedHostButCreatesOthers` | `Assert.True() Failure` (stub returning false); `Expected: BadRequest, Actual: Created` |

Reviews: the security review found one HIGH bypass (trailing dot), fixed with TDD. The code review raised 7 findings: 5 fixed, 1 documented, 1 left (AI log 019, "Coordinator review"). Engineer merged #39 (2026-10-11). Live on Azure: every rule and both 410 paths verified ([AI log 019](../ai-log/019-expiry-safety.md), "Live check on Azure").
