# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 2 (brownfield), last task. Merged: B1–B5 (#20, #22, #23, #24, #26) and #9 (#27). **B6 (#19) is PR-ready on `infra/13-azure`** (high-impact: infra + production migration path): `azure.yaml`, `infra/main.bicep` + `resources.bicep`, Dockerfile, `Database:MigrateOnStartup` switch, ADR-0006 (proposed), SETUP "Deploy to Azure", CI `bicep` job, #25 fixed (EF command logging at Warning). **Nothing is deployed yet:** the engineer runs `azd auth login`, `azd env new <name>`, `azd provision --preview` (checks the image-upsert expression ARM has not seen yet), then `azd up`, and pastes the live curl evidence into the PR before sign-off.
- **Tests:** `main` 97 (47 unit + 50 integration); B6 adds one (`ProductionMigratesAtStartupWhenSwitchedOn`).
- **Decisions open for the engineer in the B6 PR:** app managed identity is the SQL Entra admin (`db_owner`; least-privilege alternative in ADR-0006); startup migration with `maxReplicas: 1`; no `Microsoft.Data.SqlClient.Extensions.Azure` (SqlClient 6.1.6 still carries the Entra provider; package pairs with 7.x; CLAUDE.md §9 corrected); 0.0.0.0 SQL firewall rule with Entra-only auth; `AllowedHosts` kept restrictive with the liveness probe sending `Host: <fqdn>`.
- **ADR numbering:** ADR-0006 = Azure hosting + migrations. The Day 3 expiry/safety ADR is **ADR-0007** (renumbered in CLAUDE.md §7, ADR-0003, plan.md and the historical AI logs 004, 006, 011).
- **ADR-0005** still "proposed" (engineer has not noted sign-off on 10 per 60 s); PR #27 merged without the three decisions being answered; as-built choices carried into B6.
- **Issue #28 (B7, telemetry via OpenTelemetry → App Insights)** is scoped and waits for B6 to be deployed.
- **Local DB for `dotnet run`:** `MSSQL_SA_PASSWORD` (env or `.env`) + `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` (AI log 002). Development sets `PublicUrl:BaseUrl` and `Database:MigrateOnStartup` in `appsettings.Development.json`.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues: #1–#6 Day 1, #14–#19 Day 2, #9 base URL, #21 shared test container (deferred), #25 EF logging (closes with B6), #28 telemetry.
- **Workflow (engineer's rules):** minimal commits (no trailers), Walkthrough in every PR, one fresh subagent per issue (parallel ones in `isolation: "worktree"`); coordinator reviews, runs gates and both reviews on high-impact work, commits, opens the PR; engineer merges.

## Decisions made so far
`docs/plan.md` "Decisions", ADRs 0001–0006, AI logs 001–013 (each with a coordinator review table).

## Next step
1. Engineer: `azd auth login` → `azd env new <name>` → `azd provision --preview` → `azd up` → paste the curl evidence (create, redirect, stats, no probe 400s in the system log) into the B6 PR, sign off, merge. If the preview rejects the inline image-upsert expression, move the `existing` lookup into a small `infra/api.bicep` module (noted in AI log 013). Set the teardown reminder (~1 week after the reviewer finishes): `azd down --force --purge`.
2. Measure the Azure SQL round trip from the container once live (B5's first cache trigger; `perf/redirect-load.js` with `BASE_URL` pointed at `API_BASE_URL`, mind the create rate limit).
3. B7 (#28): OpenTelemetry → Application Insights (workspace-based, on the B6 Log Analytics workspace), two custom metrics (dropped clicks, rate-limit rejections), sampling, no PII; low risk.
4. Day 3: A1 clarification doc + ADR-0007 (expiry → 410 vs 404, "safe"), A2 implementation, A3 perf gate (reuse `perf/redirect-load.js`), A4 ARCHITECTURE.md, ENGINEERING_SUMMARY.md, final CLAUDE.md pass.
5. Housekeeping: `.claude/worktrees/agent-aced098cab8bdaeeb` is a locked leftover directory to delete by hand.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
| 2026-10-10 | Fable 5.1 (coordinator) / Opus 5.5 (subagents) | Day 1 G1–G6 and Day 2 B1–B4 merged; B5 and #9 built in parallel worktrees and merged; B6 built, PR opened pending the engineer's `azd up`. |
