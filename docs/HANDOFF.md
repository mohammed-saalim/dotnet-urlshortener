# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 1 — G1 done, PR #7 open and awaiting the engineer's review/merge. Next is G2 (#2).
- **Branch:** `feat/01-solution-scaffold` (PR #7). `main` has the docs scaffold only.
- **Last checks run:** build 0/0, format exit 0, no vulnerable packages, Read-deny verified; CI on PR #7 pending at time of writing.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues #1–#6 = Day 1 tasks G1–G6, milestones Day 1/2/3, labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow changes made by the engineer during G1:** minimal commits (no trailers), a Walkthrough section in every PR, one fresh subagent per issue (CLAUDE.md §6.4, §6.5, §6.12).

## Decisions made so far
See `docs/plan.md` "Decisions" and `docs/research/reports/URL shortener assignment validation.md`. ADRs 0001-0003 are written in task G6.

## Next step
1. Engineer reviews PR #7 (especially `.claude/settings.json`, `Directory.Build.props`) and merges.
2. `git checkout main && git pull`, then start G2 (#2) on `feat/02-persistence`: delegate to a fresh subagent per CLAUDE.md §6.12, with the collation test written first.
3. Subagent must not commit; coordinator reviews, runs gates, commits, opens PR with Walkthrough.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
