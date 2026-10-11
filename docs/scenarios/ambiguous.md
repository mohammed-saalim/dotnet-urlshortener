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

## Validation (filled in by A2)
To come: test names, red runs, the review triage, and live curls against Azure.
