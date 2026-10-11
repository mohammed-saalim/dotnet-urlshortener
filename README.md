# dotnet-urlshortener

A URL shortener built as an **AI-assisted, engineer-led** engineering exercise: the engineer owns every decision and approves every output; Claude Code accelerates implementation, tests, docs and review preparation inside clearly scoped tasks.

> Status: all three scenarios are done (greenfield, brownfield, ambiguous) and the service is deployed on Azure Container Apps with the Azure SQL free offer, verified live ([AI log 019](docs/ai-log/019-expiry-safety.md)). 137 unit + 62 integration tests. Start with [`docs/ENGINEERING_SUMMARY.md`](docs/ENGINEERING_SUMMARY.md); [`docs/HANDOFF.md`](docs/HANDOFF.md) has where work stands.

## What this repo demonstrates

| Deliverable | Where |
|---|---|
| Runnable prototype | [`src/UrlShortener.Api`](src/UrlShortener.Api) + `docker compose up`; see [`docs/SETUP.md`](docs/SETUP.md) |
| Architecture overview | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) (Mermaid); decisions in [`docs/adr/`](docs/adr/) |
| Three scenarios: greenfield, brownfield, ambiguous | [`docs/scenarios/`](docs/scenarios/): [greenfield](docs/scenarios/greenfield.md), [brownfield](docs/scenarios/brownfield.md), [ambiguous](docs/scenarios/ambiguous.md) |
| Setup instructions | [`docs/SETUP.md`](docs/SETUP.md), local and Azure |
| Testing approach, limitations, trade-offs | [`docs/TESTING.md`](docs/TESTING.md), [`docs/ENGINEERING_SUMMARY.md`](docs/ENGINEERING_SUMMARY.md) |
| Final engineering summary | [`docs/ENGINEERING_SUMMARY.md`](docs/ENGINEERING_SUMMARY.md) |
| AI traceability (generated / edited / rejected + rationale) | [`docs/ai-log/`](docs/ai-log/), one entry per task |
| Process | GitHub issue → branch per issue → subagent → coordinator review and gates → PR with AI log and walkthrough → CI → engineer sign-off → engineer merges |

## Stack

.NET 10 · ASP.NET Core Minimal APIs · EF Core 10 · SQL Server (Testcontainers for tests, Docker locally, Azure SQL free offer in the cloud) · Azure Container Apps via `azd` + Bicep · OpenTelemetry to Application Insights · xUnit · k6 (perf).

## How AI was used

Every task starts as an issue with intent, constraints, acceptance criteria and risk level. A fresh Claude Code subagent works on the issue's branch; a coordinator session reviews the full diff and re-runs the gates. Each artifact is recorded in `docs/ai-log/NNN-slug.md` as generated, edited (why) or rejected (why), with the checks that were run. High-impact changes (schema, URL validation, rate-limit policy, infra, agent settings) also get a security review and a fresh-context code review, then an explicit engineer sign-off before merge. Commits are minimal (a subject and `Closes #N`, no trailers); traceability lives in the AI logs and the PRs. The rules Claude follows live in [`CLAUDE.md`](CLAUDE.md).

## Research and plan

- The design was checked against primary sources before any code: [research report](docs/research/reports/URL%20shortener%20assignment%20validation.md).
- [`docs/plan.md`](docs/plan.md): the approved task breakdown, API surface, data model and sequencing (historical; the ADRs record where it changed).
