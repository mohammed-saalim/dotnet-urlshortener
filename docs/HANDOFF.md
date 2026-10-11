# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 2 (brownfield) complete in code. Merged: B1–B6 (#20, #22, #23, #24, #26, #29) and #9 (#27). **B7 (#28, telemetry) is PR-ready on `feat/28-telemetry`** (high-impact: Bicep adds Application Insights + a Container Apps secret). **Nothing is deployed yet:** no azd environment exists; the first `azd up` deploys B6 and B7 together.
- **Coordinator model:** switched from Fable 5.1 to Opus 5.5 by the engineer on 2026-10-10 (before B7).
- **Tests:** `main` 98 (47 unit + 51 integration); B7 adds 3 integration (54).
- **B7 decisions:** `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0, opt-in by `APPLICATIONINSIGHTS_CONNECTION_STRING` (tests pin it empty); custom meter `UrlShortener` with `urlshortener.analytics.dropped_clicks`; rate-limit rejections from the built-in `aspnetcore.rate_limiting.requests`; workspace-based App Insights under the 0.5 GB/day workspace cap; connection string as a Container Apps secret; `url.query` redaction re-enabled; `/health/*` not traced; Entra-only ingestion is the documented production option, not built.
- **ADR numbering:** ADR-0006 = Azure hosting + migrations (+ telemetry section). Day 3 expiry/safety ADR is **ADR-0007**. ADR-0005 still "proposed" (sign-off on 10 per 60 s not noted).
- **Local DB for `dotnet run`:** `MSSQL_SA_PASSWORD` (env or `.env`) + `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` (AI log 002). Development sets `PublicUrl:BaseUrl` and `Database:MigrateOnStartup` in `appsettings.Development.json`.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Open: #21 shared test container (deferred), #28 (this PR). Labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow (engineer's rules):** minimal commits (no trailers), Walkthrough in every PR, one fresh subagent per issue; coordinator reviews, runs gates and both reviews on high-impact work, commits, opens the PR; engineer merges.

## Decisions made so far
`docs/plan.md` "Decisions", ADRs 0001–0006, AI logs 001–013 (each with a coordinator review table).

## Next step
1. Engineer merges B7 (#28), then deploys once: `azd auth login` → `azd env new urlshortener` → `azd provision --preview` (the inline image lookup did fail with a circular dependency on the second `azd up`; moved to `infra/api-image.bicep` in #31) → `azd up` → paste the curl evidence (create, redirect, stats) and the B7 portal checks (AI log 014) into a follow-up comment. Set the teardown reminder (~1 week after the reviewer finishes): `azd down --force --purge`.
2. Once live: measure the Azure SQL round trip from the container (B5's first cache trigger; `perf/redirect-load.js` with `BASE_URL=$API_BASE_URL`, mind the create rate limit).
3. Day 3: A1 clarification doc + ADR-0007 (expiry → 410 vs 404, "safe"), A2 implementation, A3 perf gate (reuse `perf/redirect-load.js`), A4 ARCHITECTURE.md, ENGINEERING_SUMMARY.md, final CLAUDE.md pass.
4. Housekeeping: `.claude/worktrees/agent-aced098cab8bdaeeb` is a locked leftover directory to delete by hand.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
| 2026-10-10 | Fable 5.1 (coordinator) / Opus 5.5 (subagents) | Day 1 G1–G6 and Day 2 B1–B4 merged; B5 and #9 built in parallel worktrees and merged; B6 built, PR opened pending the engineer's `azd up`. |
| 2026-10-11 | Opus 5.5 (coordinator + subagents) | B6 merged (not deployed); B7 telemetry built, reviewed, PR opened. |
