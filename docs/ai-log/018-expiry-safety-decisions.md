# AI log 018 — expiry-safety-decisions

| Field | Value |
|---|---|
| Issue | #36 |
| Branch | `docs/36-expiry-safety-decisions` |
| Date | 2026-10-11 |
| Tool / model | Claude Code / `claude-opus-5-5` (coordinator; docs, no subagent) |
| Risk level | low (docs); it scopes the high-impact A2 |

## Intent
Turn "links should expire and be safe" into explicit questions, the engineer's answers and reversible assumptions, before any code.

## Constraints given to the AI
Docs only. Every assumption reversible. State the threat model. Don't pre-decide what changes behaviour: ask.

## Prompts (summarised)
1. Coordinator read the current code: the redirect ignores `ExpiresAt`; deleted → 404 in the redirect, metadata and stats; two tests assert the deleted redirect's 404 (`SoftDeletedCodeIsNotFound`, `DeleteSoftDeletesSoGetAndRedirectAreNotFound`); `CreateLinkValidator` rules; five reserved aliases.
2. Reviewer (advisor model) on scope: surface deleted 404 → 410 explicitly, because it changes existing tests; keep DNS out (TOCTOU, network in tests); the validator stays a pure function with the public host passed in; use `IPAddress` APIs for ranges, not string prefixes.
3. Four questions to the engineer, with the recommended option first. **Answers (2026-10-11): 410 for both; no default TTL; minimal rules + seam; no click on expired.** All four were the recommended options.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `docs/adr/0007-expiry-and-safety.md` | generated | Expiry table and safety table, each row with "reversible by"; threat model; what is not built and why. |
| `docs/scenarios/ambiguous.md` | generated | Questions with who answered each (engineer / assumption / threat model), threat model, A2 plan. |
| (option) DNS resolution at create | rejected | Engineer's answer; TOCTOU, network dependency on create and in tests. |
| (option) external reputation API | rejected | Engineer's answer; secret, HTTP client, failure policy. The seam is where it would plug in. |
| (option) default TTL of one year | rejected | Engineer's answer; changes every create response, while no default is reversible. |

## Checks run
Docs only. Links checked by hand: ADR-0007 ↔ `scenarios/ambiguous.md`.

## Sign-off
Reviewed and merged by the engineer in PR #37; I understood the change and accept ownership of it. — Mohammed Saalim, sign-off recorded 2026-10-11
