# AI log 006 — SETUP, greenfield scenario, ADR-0001/0002/0003

| Field | Value |
|---|---|
| Issue | #6 |
| Branch | `docs/06-day1-docs` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-sonnet-5-5` (subagent delegated by the coordinator session) |
| Risk level | low |

## Intent
Make Day 1 reviewable: a reviewer can clone, run and understand why the stack, short-code design and redirect semantics were chosen.

## Constraints given to the AI
- Documentation only: no code, tests, csproj, settings or workflow changes.
- Every technical claim sourced from the repo files or from a URL already cited in the research report; where the report says UNVERIFIED, say so.
- Quote the brief in at most a few words; do not open the brief PDF.
- Do not read `.env`, `secrets.json`, `.azure/`. No commit, no push, no PR.

## Acceptance criteria (runnable)
- Fresh clone to `docker compose up` to `dotnet run` to curl create and redirect works by following SETUP.md only. Not executed by the agent in this task (docs only, no Docker run); the commands were copied from AI logs 002 and 005 and PR #11, where they were run. **The engineer or coordinator should do one cold run of SETUP.md.**
- Every ADR has a Confirmation section naming a test or command. See the Checks section.

## Prompts (summarised, secrets redacted)
1. Coordinator to subagent: write the six documents and the README status change from the listed sources, with the hard rules above and the report-back items.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `docs/SETUP.md` | generated | Prerequisites, compose, user-secrets, run, curl walkthrough, tests, gates, troubleshooting. Port 5078 from `launchSettings.json`; commands from AI logs 002 and 005 and PR #11 |
| `docs/scenarios/greenfield.md` | generated | Requirement, decomposition (table + Mermaid `graph LR`), execution with six examples from the AI logs, validation tables, 5-minute checks |
| `docs/adr/0001-stack-and-database.md` | generated | MADR per the template; seven options, including the research report's provider-switch recommendation recorded as not chosen |
| `docs/adr/0002-short-codes-and-collation.md` | generated | Collision maths from the report; `_CS_AS` verified, `_BIN2` marked UNVERIFIED |
| `docs/adr/0003-redirect-semantics.md` | generated | RFC 9110 sections as cited in the report; vendor behaviour labelled as research-pass curl observations |
| `README.md` | edited | Only the "Status: Day 0" line, now Day 1 with pointers to the scenario and SETUP |
| `docs/ai-log/006-day1-docs.md` | generated | This file |
| (not included) The report's cache and analytics options, hosting options table, Archify, Spec Kit, commit-trailer material | rejected | Day 2/3 or process topics, not Day 1 decisions; keeps the ADRs tight |
| (not included) A claim that the `.env.example` password rule is "8+ chars, 3 of 4 classes" | rejected | The agent could not read `.env.example` (blocked by the permission rules), and no allowed source states the rule. SETUP says "see `.env.example`" instead |
| (not included) PowerShell/`curl.exe` guidance for Windows | rejected | Not sourced from any repo file; SETUP says commands are for a bash-style shell |
| (not included) Claims about RFC 9111 beyond "governs caching rules" | rejected | The report cites RFC 9110 sections only; RFC 9111 is named in the task but no section is cited anywhere in the research |

## Decisions the engineer should question
- **ADR status "accepted" and date 2026-10-10.** The decisions were made earlier (plan) but the ADR text is new; the engineer must approve the wording. All three were written after the code, not before.
- **ADR-0001 option 4 (provider switch).** The research report *recommended* SQLite default plus SQL Server; the plan overrode it. The ADR records that disagreement openly. Check you are comfortable with how it reads.
- **ADR-0001 "no production migration path yet"** is taken from AI log 002 ("open item for B6"). It is true today but will be stale after Day 2.
- **ADR-0002 "Five consecutive collisions is about (2.8e-7)^5"** and the Crockford base32 figure (32^7 about 3.4e10) are my own arithmetic, not from the report; both are labelled so in the text. The birthday 50% point and 2.8e-7 are the report's numbers.
- **ADR-0003 vendor table** (bit.ly 301 + `private, max-age=90`, TinyURL 301 + `no-store`, Dub 302) comes from the researcher's curl observations; the report lists it as weaker evidence. It is labelled that way. Nothing was re-checked.
- **ADR-0003 treats expiry as undecided** and names ADR-0007 as the planned home (plan.md numbers ADR-0004 and ADR-0005 for analytics and rate limiting, and ADR-0007 for the A1 assumptions; renumbered in B6, AI log 013). Check this numbering is still wanted.
- **greenfield.md "G5 risk"**: AI log 005 and PR #12 disagree with themselves (log says low, PR body ticks high-impact because the CI fix touched agent settings). I wrote "low at review, marked high-impact after". The PR body does not show `/security-review` or `/code-review` ticked; I wrote "not run" based on the AI log's coordinator review.
- **greenfield.md model names**: "Opus 5.5 subagents" for G2 to G5 and "coordinating session" for G1 come from the log header tables.
- **Evidence table figures are not re-run** by this agent; they are the G5 AI log and PR #12 results. SETUP was not executed either.
- **SETUP optional custom-alias example** (`MyAlias1`, `expiresAt` 2030) is not taken verbatim from a log; it is built from the request shape (`url`, `customAlias`, `expiresAt`) and the alias rules (4 to 10 Base62) in AI log 003. Test `CreatesLinkWithCustomAliasAndExpiry` covers the behaviour but I did not run the exact curl.

## Checks run
```
$ git status --short
 M README.md
?? docs/SETUP.md
?? docs/adr/0001-stack-and-database.md
?? docs/adr/0002-short-codes-and-collation.md
?? docs/adr/0003-redirect-semantics.md
?? docs/ai-log/006-day1-docs.md
?? docs/scenarios/
$ (link check script over the five new docs)
linkcheck-done        (no MISSING lines)
```

Relative links verified with a script (`test -e` on each link target resolved from the file's directory, `%20` decoded):

| File | Links checked (all exist) |
|---|---|
| `docs/SETUP.md` | `../.github/workflows/ci.yml`, `../CLAUDE.md`, `../global.json`, `../src/UrlShortener.Api/Properties/launchSettings.json`, `adr/0001-stack-and-database.md`, `ai-log/005-api-surface.md`, `plan.md` |
| `docs/scenarios/greenfield.md` | `../../.github/workflows/ci.yml`, `../../CLAUDE.md`, `../SETUP.md`, `../adr/0001-...`, `0002-...`, `0003-...`, `../ai-log/001` to `006`, `../plan.md` |
| `docs/adr/0001-stack-and-database.md` | `../../.github/workflows/ci.yml`, `../../global.json`, `../../tests/UrlShortener.IntegrationTests/SqlServerFixture.cs`, `../ai-log/001`, `002`, `../plan.md`, `0002-short-codes-and-collation.md`, the research report |
| `docs/adr/0002-short-codes-and-collation.md` | `LinkPersistenceTests.cs`, `RedirectEndpointTests.cs`, `CreateLinkEndpointTests.cs`, `ShortCodeTests.cs`, `../ai-log/002`, `003`, the research report |
| `docs/adr/0003-redirect-semantics.md` | `RedirectEndpointTests.cs`, `../ai-log/004`, `005`, the research report |

Test names cited in the ADR Confirmation sections were checked against the test sources with `grep` (all present): `CodesDifferingOnlyByCaseAreDistinctRows`, `CodeColumnUsesCaseSensitiveCollation`, `DuplicateCodeIsRejectedByUniqueIndex`, `CodeDifferingOnlyInCaseIsNotFound`, `KnownCodeRedirectsWithFoundAndNoStore`, `UnknownCodeIsNotFoundProblem`, `SoftDeletedCodeIsNotFound`, `RetriesWithNewCodeWhenGeneratedCodeCollides`, `GivesUpAfterMaxGeneratedCodeCollisions`, `DuplicateCustomAliasReturnsConflict`.

## Gotchas encountered
None added to CLAUDE.md section 9. Observation for the coordinator: the agent's shell policy denied reading `.env.example` (matches the `.env` deny pattern), even though the task asked for it. Consider whether `.env.example` should be exempted in `.claude/settings.json` (a high-impact file; not touched here).

## Sign-off
(pending engineer review of the PR)

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`). Read ADR-0001/0002/0003 and `greenfield.md` in full, spot-checked `SETUP.md` commands. Docs only, so no code gates apply; `git status` shows only the listed files plus `docs/HANDOFF.md` (coordinator's own update).

**Cold run of `SETUP.md`** (the acceptance criterion the agent could not cover): fresh `git clone` of this branch into a scratch directory, random throwaway `MSSQL_SA_PASSWORD` (never printed or stored), connection string via the `ConnectionStrings__UrlShortener` environment variable (equivalent to the user-secrets step), `docker compose up -d`, `dotnet run`, then the walkthrough. Container, volume and clone removed afterwards.
```
sqlserver health: healthy
/health/live   HTTP/1.1 200 OK
/health/ready  HTTP/1.1 200 OK
POST /api/links            HTTP/1.1 201 Created   (code=0Dc2fmJ)
GET /api/links/{code}      HTTP/1.1 200 OK
GET /{code}                HTTP/1.1 302 Found
                           Cache-Control: private, no-store
                           Location: https://example.com/docs?q=1
DELETE x2                  204 No Content, then 404 Not Found
GET /{code} after delete   404 Not Found, Content-Type: application/problem+json
GET /scalar                302 (redirects to the UI)
```
App log showed EF command logging with `Parameters=[]` (values are not logged without `EnableSensitiveDataLogging`), so destination URLs did not appear.

| Agent point | Outcome |
|---|---|
| "CLAUDE.md §6.4 still requires trailers" | **Not a contradiction**: §6.4 says minimal commits, no trailers. The agent misread or had a stale view |
| HANDOFF stale | Expected; the coordinator updates it per task (done in this PR) |
| G5 marked high-impact after the fact with no reviews run | Accurate; `greenfield.md` states it plainly. Accepted as a recorded process deviation rather than retro-running reviews on merged code |
| `.env.example` unreadable to the agent (`Read(**/.env.*)` deny) | Known cost of the deny rule; SETUP points at the file. Not changing agent settings in a docs PR |
| ADR status/date, ADR-0007 numbering, "no production migration path yet" | Accepted: decisions were taken in the approved plan before code; numbering matches the plan; ADR-0001's consequence will be superseded by the Day 2 infra decision |
| 5-minute check #4 (ready fails when DB stopped) inferred, not tested | Accepted; candidate for a small test in Day 2 |
