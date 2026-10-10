# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 1 — G1 (#7) and G2 (#8) merged. G3 done by a delegated agent, reviewed (2 findings fixed, 3 deferred to #5, 1 became #9), PR open. Next is G4 (#4).
- **Branch:** `feat/03-create-link`. `main` = G2.
- **Last checks run:** build 0/0, format exit 0, 47 unit + 10 integration tests, no vulnerable packages, `/security-review` (none) + `/code-review` (6 → triaged).
- **New issue #9 (Day 2, high-impact):** public base URL / AllowedHosts / forwarded headers before the Azure deploy.
- **Local DB for `dotnet run`:** engineer must set `MSSQL_SA_PASSWORD` (env or `.env`) and `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` — commands in AI log 002.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues #1–#6 = Day 1 tasks G1–G6, milestones Day 1/2/3, labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow changes made by the engineer during G1:** minimal commits (no trailers), a Walkthrough section in every PR, one fresh subagent per issue (CLAUDE.md §6.4, §6.5, §6.12).

## Decisions made so far
See `docs/plan.md` "Decisions" and `docs/research/reports/URL shortener assignment validation.md`. ADRs 0001-0003 are written in task G6.

## Next step
1. Engineer reviews the G3 PR (URL validation = high-impact), signs off, merges.
2. `git checkout main && git pull`, branch `feat/04-redirect`, delegate G4 (#4). Heads-up for G4: stored URLs are guaranteed printable ASCII (validator), so they are safe to emit in `Location`; `ApiFactory` in the integration tests already injects the connection string and a fixed `TimeProvider`.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
