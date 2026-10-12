# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** all three scenarios done and **deployed**. Merged: Day 1 G1–G6 (#7, #8, #10–#13), Day 2 B1–B7 and fixes (#20, #22–#24, #26, #27, #29, #30, #32, #34, #35), Day 3 A1 (#37), A2 (#39), A4 (#41), then the brief cross-check (#42), sign-offs (#43) and the support workbook (#45, AI log 021), and two docs follow-ups (#46; #47, the live URL and the support scenario in the README). 25 PRs. No open PR after #47.
- **Live** in centralus (azd env `urlshortener`), `main` with #39 deployed: create, redirect 302 + `private, no-store`, stats, readiness (AI log 015), telemetry (AI log 014), every ADR-0007 rule and both 410 paths (AI log 019). Every trace kept since #33 (AI log 016). Support workbook "URL shortener support" under the App Insights resource (#45); if its panels say "could not connect to server", the browser has a blocker extension (CLAUDE.md §9).
- **Coordinator model:** Opus 5.5 since 2026-10-10 (before B7); Fable 5.1 before that.
- **Tests:** 199 (137 unit + 62 integration), `dotnet test -c Release`, 2026-10-11 (AI log 020).
- **Deploy facts:** subscription refuses new SQL servers in eastus2 (centralus works); free-offer DB accepts only the default 60-min auto-pause delay; azd's `AllowAllWindowsAzureIps` reserved-word warning is a false positive; the image lookup lives in `infra/api-image.bicep` (CLAUDE.md §9).
- **ADRs:** 0001–0004 accepted (0003 superseded in part by 0007); 0005, 0006, 0007 accepted on the engineer's sign-off (2026-10-11).
- **Local DB for `dotnet run`:** `MSSQL_SA_PASSWORD` (env or `.env`) + `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` (AI log 002). Development sets `PublicUrl:BaseUrl` and `Database:MigrateOnStartup` in `appsettings.Development.json`.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Open issues: #21 (shared test container, deferred, left open on purpose as evidence of the board). Labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow (engineer's rules):** minimal commits (no trailers), Walkthrough in every PR, one fresh subagent per issue (small known-cause fixes done by the coordinator, said in the PR); small items may skip the issue (engineer, 2026-10-11); coordinator reviews, runs gates and both reviews on high-impact work, commits, opens the PR; engineer merges.

## Decisions made so far
`docs/plan.md` "Decisions", ADRs 0001–0007, AI logs 001–021 (each with a coordinator review table or a checks section). Summary of all of it: `docs/ENGINEERING_SUMMARY.md`.

## Next step
1. Done: build, docs, sign-offs and the support workbook. Nothing is in progress.
2. **Decided (engineer, 2026-10-11):** the portal screenshot removed in #42 stays in git history (no rewrite). Local branches, including the `worktree-agent-*` ones, are kept as evidence; don't delete them.
3. **Teardown:** `azd down --force --purge` about one week after the reviewer finishes; the engineer will say when.
4. #21 stays deferred unless the one-container-per-class CI flake recurs.
5. Screenshots in `docs/screenshots/` are the engineer's; stage files explicitly (an uncropped one was swept in once, #42).

## Open questions for the engineer
- None open. Teardown waits for the engineer's reminder.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
| 2026-10-10 | Fable 5.1 (coordinator) / Opus 5.5 (subagents) | Day 1 G1–G6 and Day 2 B1–B4 merged; B5 and #9 built in parallel worktrees and merged; B6 built, PR opened pending the engineer's `azd up`. |
| 2026-10-11 | Opus 5.5 (coordinator + subagents) | B6 merged (not deployed); B7 telemetry built, reviewed, PR opened. |
| 2026-10-11 | Opus 5.5 (coordinator) | First deploy: three failures fixed in #31 (region, auto-pause delay, circular image lookup); live evidence and B7 checks recorded; #33 trace sampling PR opened. |
| 2026-10-11 | Opus 5.5 (coordinator + subagents) | After #31 (row above): #33 verified live (3 of 3 quoted trace ids found) and merged; Azure perf check (#35: SQL lookup p50 1.9 ms, no cache); A1 questions and ADR-0007 (#36); A2 (#38) built TDD, security review found the trailing-dot IP bypass with 181 tests green, fixed test-first, deployed and verified live; A4 (#40) final docs. |
| 2026-10-11 | Opus 5.5 (coordinator + subagents) | Brief cross-check (#42); sign-offs (#43); `demo/` learning pack (gitignored); support workbook #44 → #45: built by a subagent, both reviews, engineer's portal check (blocker extension found; `url`/`duration` added to the `traceId` panel; duration legend fixed from Sum to Max with the portal's own value after a wrong first guess). |
