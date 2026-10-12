# dotnet-urlshortener

A URL shortener built as an **AI-assisted, engineer-led** engineering exercise: the engineer owns every decision and approves every output; Claude Code accelerates implementation, tests, docs and review preparation inside clearly scoped tasks.

> Status: all three scenarios are done (greenfield, brownfield, ambiguous) and the service is deployed on Azure Container Apps with the Azure SQL free offer, verified live ([AI log 019](docs/ai-log/019-expiry-safety.md)). 137 unit + 62 integration tests. Start with [`docs/ENGINEERING_SUMMARY.md`](docs/ENGINEERING_SUMMARY.md); [`docs/HANDOFF.md`](docs/HANDOFF.md) has where work stands.

## Live deployment

**https://ca-api-fpcdm5h7cv7yi.delightfulwater-705b3797.centralus.azurecontainerapps.io**

Everything behind that URL is created by `azd up` from the Bicep in [`infra/`](infra/), with no portal clicks, and removed by `azd down --force --purge`. It stays up until about a week after the review. Try it:

```bash
API=https://ca-api-fpcdm5h7cv7yi.delightfulwater-705b3797.centralus.azurecontainerapps.io

curl -s -X POST $API/api/links -H "Content-Type: application/json" -d '{"url":"https://learn.microsoft.com/dotnet/"}'
# {"code":"IBw9POt","shortUrl":"https://ca-api-fpcdm5h7cv7yi.delightfulwater-705b3797.centralus.azurecontainerapps.io/IBw9POt","url":"https://learn.microsoft.com/dotnet/",...}

curl -i $API/IBw9POt                  # 302 Found, Location: https://learn.microsoft.com/dotnet/, Cache-Control: private, no-store
curl -s $API/api/links/IBw9POt/stats  # {"code":"IBw9POt","totalClicks":1,"byDay":[...],"topReferrers":[]}
```

(Run on 2026-10-12.) The app scales to zero and the database auto-pauses, so the first request after idle is slow: `/health/live` took 74 s that day, then 0.2 s. Creates are limited to 10 per minute per client. OpenAPI and the Scalar UI are Development-only, so they are not on this URL; the endpoints are in [`docs/SETUP.md`](docs/SETUP.md#4-try-it-with-curl).

What the Bicep creates ([`infra/resources.bicep`](infra/resources.bicep); choices in [ADR-0006](docs/adr/0006-azure-hosting-and-migrations.md)):

| Resource | Configuration |
|---|---|
| Container Apps environment + app | Consumption plan, 0 to 1 replica (scale to zero; one replica so the startup migration never races another instance) |
| Container Registry (Basic) | No admin user; the app pulls with its managed identity (`AcrPull`) |
| User-assigned managed identity | The app's only identity: image pulls and SQL access. No passwords or connection-string secrets anywhere |
| Azure SQL, free offer (serverless `GP_S_Gen5`) | Entra-only authentication (`azureADOnlyAuthentication`), auto-pause; the app connects passwordless with its identity |
| Log Analytics + Application Insights | OpenTelemetry from the app, every trace kept (so a quoted `traceId` is always findable) |
| Support workbook | Four support panels over Application Insights ([below](#production-support-from-a-traceid-to-the-cause)) |
| Cost guardrails | Email alert when the free SQL allowance drops below 20 %, a monthly budget alert at 80 %; the free offer's auto-pause is the hard stop |

## Production support: from a traceId to the cause

Every error response is [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) with a `traceId`. When someone reports "your short link is broken" or "it was slow", support asks for that id, opens the **URL shortener support** workbook (Application Insights → Monitoring → Workbooks; built in Bicep from [`infra/workbook.json`](infra/workbook.json)) and pastes it:

1. **Find a request by traceId:** the request and every step under it: status, which short link (`url`), SQL calls, log lines, exceptions, each with its duration. A real one: a deleted link answered **410** in 327 ms, of which the SQL lookup took 25 ms. The database was fast; the time went to app warm-up after scaling from zero (the next two requests took 82 ms and 5 ms).
2. **Redirect outcomes:** 302 / 404 / 410 over time.
3. **Rate limiting:** how often link creation was refused with 429.
4. **Health:** redirect p50/p95, 5xx responses and dropped analytics clicks.

How to open it and what each panel answers: [`docs/SETUP.md`](docs/SETUP.md#telemetry). Design, live checks and the bugs found by using it: [AI log 021](docs/ai-log/021-workbook.md).

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
| Live deployment and production support | [Live deployment](#live-deployment) (URL, the Bicep-built resources), [Production support](#production-support-from-a-traceid-to-the-cause) (traceId → workbook) |
| Process | GitHub issue → branch per issue → subagent → coordinator review and gates → PR with AI log and walkthrough → CI → engineer sign-off → engineer merges |

## Stack

.NET 10 · ASP.NET Core Minimal APIs · EF Core 10 · SQL Server (Testcontainers for tests, Docker locally, Azure SQL free offer in the cloud) · Azure Container Apps via `azd` + Bicep · OpenTelemetry to Application Insights · xUnit · k6 (perf).

## How AI was used

Every task starts as an issue with intent, constraints, acceptance criteria and risk level. A fresh Claude Code subagent works on the issue's branch; a coordinator session reviews the full diff and re-runs the gates. Each artifact is recorded in `docs/ai-log/NNN-slug.md` as generated, edited (why) or rejected (why), with the checks that were run. High-impact changes (schema, URL validation, rate-limit policy, infra, agent settings) also get a security review and a fresh-context code review, then an explicit engineer sign-off before merge. Commits are minimal (a subject and `Closes #N`, no trailers); traceability lives in the AI logs and the PRs. The rules Claude follows live in [`CLAUDE.md`](CLAUDE.md).

## Research and plan

- The design was checked against primary sources before any code: [research report](docs/research/reports/URL%20shortener%20assignment%20validation.md).
- [`docs/plan.md`](docs/plan.md): the approved task breakdown, API surface, data model and sequencing (historical; the ADRs record where it changed).
