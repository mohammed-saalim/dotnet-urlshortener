# AI log 020 — final-docs

| Field | Value |
|---|---|
| Issue | #40 |
| Branch | `docs/40-final-docs` |
| Date | 2026-10-11 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | low (docs) |

## Intent
Finish the deliverables: `docs/ARCHITECTURE.md`, `docs/ENGINEERING_SUMMARY.md`, a current README, a final CLAUDE.md pass and the end-state HANDOFF; fold A3 (perf gate) in as a note.

## Constraints given to the AI
Docs only; do not commit, push or open a PR. Every number, finding and decision from the repo with a link to its source; no invented metrics. Brief quoted a few words at most; no employer references. Mermaid in Markdown, simple syntax. ADR statuses unchanged (0005/0006/0007 stay proposed). Surgical CLAUDE.md edits only.

## Acceptance criteria (runnable)
- ARCHITECTURE has components, create / redirect + click write / stats flows and the Azure deployment as Mermaid → files below; Mermaid checked for characters that break labels.
- The summary covers what was built, how AI was used and governed, generated/edited/rejected examples and review catches, quality gates, limitations, next steps → `docs/ENGINEERING_SUMMARY.md`.
- README status, links and process current (no trailers) → link check below.
- Test counts cited are re-run → `dotnet test -c Release` below.

## Prompts (summarised, secrets redacted)
1. Coordinator: issue #40 text, CLAUDE.md, the list of sources (README, plan, HANDOFF, SETUP, TESTING, ADRs, scenarios, AI logs 001–019, infra, `Program.cs` and feature folders, CI, agent settings, build props, `gh pr list` / `gh issue list`) and the six deliverables.
2. Reviewer (advisor model) before writing: keep the model history exact (Fable 5.1 coordinator through AI log 013); cite both 119 and 137 for the #38 catch; report sign-off state as found, not as "signed off"; take column types from the migrations, not the plan; make README links real links; check link case, since Windows hides case mismatches that 404 on GitHub.

## Sources read
README, `docs/plan.md`, HANDOFF, SETUP, TESTING, ADR-0001 to 0007, the three scenario docs, AI logs 001–019 (coordinator review, checks and artifact tables in full for 002–019), `Program.cs` and every file under `Links/`, `Redirect/`, `Analytics/`, `Safety/`, `PublicUrl/`, `RateLimiting/`, `Telemetry/`, `Data/`, both migrations, `infra/*.bicep`, `azure.yaml`, `ci.yml`, `.claude/settings.json`, `Directory.Build.props`, the brief in `docs/brief/` (to check coverage only), merged PRs and all issues via `gh`, and the sign-off lines of the high-impact PR bodies.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `docs/ARCHITECTURE.md` | generated | Component diagram and table, middleware order, sequence diagrams for create, redirect + click write and stats, ER diagram from the migrations, Azure deployment diagram and table, cross-cutting (ProblemDetails, logging/telemetry, health, options, security boundaries), ADR index. Every element links to its ADR or AI log. |
| `docs/ENGINEERING_SUMMARY.md` | generated | Plan and rationale; what was built per scenario; the governance loop (diagram + table), models, deviations, secure AI usage with its limits; 8 generated/edited/rejected examples; 8 review catches; gates, test counts by stage, mutation checks, live evidence; performance (A3 folded in); limitations table; next for production; pending engineer actions. |
| `README.md` | edited | Status (three scenarios done, deployed, test count); every path a real link; the process line and "How AI was used" corrected: commits carry no `Assisted-by:`/`AI-Log:` trailers; links to ARCHITECTURE and ENGINEERING_SUMMARY. |
| `CLAUDE.md` | edited | Four lines. §6.1 notes the engineer's 2026-10-11 waiver of issues for small items (AI log 017, PR #35), recorded so far only in HANDOFF. §5 repo map listed a `Health/` folder that does not exist (health checks are in `Program.cs`) and missed `Safety/`, `PublicUrl/`, `RateLimiting/`, `Telemetry/`, `Data/`, `Migrations/`. §9 `attribution.commit` said "trailers are written by hand per commit", contradicting §6.4. §9 `Uri.TryCreate` said the validator rejects whitespace and control characters; since AI log 003 it accepts printable ASCII only. |
| `docs/HANDOFF.md` | edited | End state, next steps (merge, sign-offs, teardown, #21, housekeeping), one session-log row continuing from the previous one. |
| `docs/ai-log/020-final-docs.md` | generated | This log. |
| (proposal) a "Validation" section in `docs/scenarios/brownfield.md` | not done | Outside the issue's file list; the other two scenario docs have one. Raised as an open question in HANDOFF. |
| (proposal) flip ADR-0005/0006/0007 to accepted because the PR sign-off boxes are ticked | rejected | The brief says statuses stay until the engineer signs off; the name/date fields in the PRs are empty. Listed as pending. |
| (proposal) the live Azure hostname in README | rejected | Torn down in about a week; the AI logs hold the live evidence. |
| (proposal) a §9 gotcha for the build lock below | rejected | AI log 019 already records the same lesson ("don't run gates while a review agent builds the same tree"). |

## Checks run

Tests, to confirm the counts cited:
```
$ dotnet test -c Release        (first attempt)
CSC : error CS2012: Cannot open '...\src\UrlShortener.Api\obj\Release\net10.0\UrlShortener.Api.dll' for writing
      -- The process cannot access the file ... because it is being used by another process.; file may be locked by 'VBCSCompiler' (27056)
(the pipe into `tail` hid the failure: the shell reported exit 0)

$ dotnet build-server shutdown; dotnet test -c Release
Passed!  - Failed:     0, Passed:   137, Skipped:     0, Total:   137, Duration: 261 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    62, Skipped:     0, Total:    62, Duration: 36 s - UrlShortener.IntegrationTests.dll (net10.0)
```

Link check (`python -I` script in the session scratchpad; every relative Markdown link in the six touched files: target exists with exact case on every path segment, `%20` decoded; `#anchors` resolved against GitHub-style heading slugs; `http(s)` skipped):
```
$ python -I check.py <repo> README.md CLAUDE.md docs/ARCHITECTURE.md docs/ENGINEERING_SUMMARY.md docs/HANDOFF.md docs/ai-log/020-final-docs.md
202 relative links checked in 6 files, 0 broken
exit 0

$ python -I check.py <selftest>   (a case mismatch, a missing anchor, a missing file, one good link)
a.md:1: B.md -> case mismatch
a.md:2: b.md#nope -> no anchor #nope
a.md:4: missing.md -> missing
4 relative links checked in 1 files, 3 broken
exit 1
```

Mermaid: every block scanned for `;`, `#`, `<=`/`>=` inside labels and messages (a `;` ends a statement in sequence diagrams); two found in ARCHITECTURE and reworded. The flowcharts and sequence diagrams follow the syntax already rendering in `docs/scenarios/brownfield.md`. Not rendered here; GitHub rendering is checked in the PR.

## Not verified
- That the Mermaid diagrams render on GitHub (no renderer run locally). Two constructs are new to this repo and not proven by an existing diagram: the `erDiagram` in ARCHITECTURE and an edge from a subgraph (`API -. ... .-> AI`).
- Who ticked the sign-off checkboxes in the PR bodies; only that they are ticked with empty name/date fields.
- The live Azure deployment today: the "deployed and verified" statements rest on AI logs 014–019 (2026-10-11), not on a new request.

## Gotchas encountered
- A concurrent build in the same checkout (VBCSCompiler holding `obj/Release/.../UrlShortener.Api.dll`) fails `dotnet test` with CS2012; `dotnet build-server shutdown` and a re-run fixed it. A `| tail` without `pipefail` reported success (the §9 Stop-hook gotcha in another place).
- Files written on Windows came out with CRLF; normalised to LF to match `.gitattributes` (`eol=lf`).

## Sign-off
Reviewed and merged by the engineer in PR #41; I understood the change and accept ownership of it. — Mohammed Saalim, sign-off recorded 2026-10-11
