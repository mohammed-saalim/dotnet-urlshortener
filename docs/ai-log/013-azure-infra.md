# AI log 013 — Azure infrastructure (azd + Bicep), production migration path, EF command logging

| Field | Value |
|---|---|
| Issue | #19 (B6), #25 |
| Branch | `infra/13-azure` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | high-impact (infra/Bicep, identity and database access, production migrations) |

## Intent
Deploy-ready Azure infrastructure for `azd up` (Container Apps consumption + Azure SQL free offer) at near-zero idle cost, no secrets, a decided production migration path and a one-line teardown (#19); stop EF Core logging every SQL command at `Information` (#25).

## Constraints given to the AI
- Implement the coordinator's 13 decisions (layout, Dockerfile, Container Apps shape, env vars, Azure SQL free offer + Entra-only + identity as admin, startup migration switch, ACR Basic, cost guardrails, teardown docs, #25, ADR-0006 + renumbering, CI Bicep step, no `.claude/settings.json` edit); argue in the ADR where it disagrees.
- No `azd up` / `azd provision` / `azd deploy`; `azd provision --preview` allowed with a throwaway environment. Never read `.env`, `secrets.json`, `.azure/` or credentials; no subscription id, tenant id, email or password in the repo or report.
- Minimal: no extra modules or parameters; no Application Insights (issue #28); tests unchanged unless one breaks.
- No commit, push or PR.

## Acceptance criteria (runnable)
- `azd up` succeeds from a clean environment; live curl of create + redirect pasted → **pending, engineer** (see "Engineer's run" below)
- `az bicep build` / `azd provision --preview` clean → `bicep lint` clean below; preview blocked (no subscription in the azd environment without printing it), see Checks
- Teardown documented; engineer sign-off → `docs/SETUP.md` "Deploy to Azure"; sign-off below
- #25: `Microsoft.EntityFrameworkCore.Database.Command` at `Warning`; a redirect logs no `Executed DbCommand` → container log count 0 below
- Existing 47 unit + 50 integration tests unchanged and green → below

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #19 and #25 per the 13 decisions above; verify each platform claim in official docs and cite it; smoke-test the image against a throwaway compose SQL Server; fill this log as you go; report departures.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `azure.yaml` | generated | One service `api`, `language: dotnet`, `host: containerapp`, `docker.path ./Dockerfile`, `docker.context ../..` (the schema says context overrides the default `.`, which is the project folder; the build needs the root props files). `remoteBuild` left off (brief default), documented as an option. `azd package api` confirmed it builds our Dockerfile (same layers, `USER 1654`, same entrypoint) |
| `infra/main.bicep` | generated | Subscription scope: resource group `rg-<env>` + one module. Params: `environmentName`, `location`, `alertEmail` (no default, not in the parameters file, so azd prompts and stores it in `.azure/`), `monthlyBudget = 10`, `apiExists`, `budgetStartDate = utcNow('yyyy-MM-01')` (utcNow is only legal as a param default). Outputs `AZURE_CONTAINER_REGISTRY_ENDPOINT` (azd needs it to push), `AZURE_RESOURCE_GROUP`, `API_BASE_URL` |
| `infra/main.parameters.json` | generated | `${AZURE_ENV_NAME}`, `${AZURE_LOCATION}`, `${SERVICE_API_RESOURCE_EXISTS=false}` |
| `infra/resources.bicep` | generated | Identity, Log Analytics (30 d, 0.5 GB/day), ACR Basic (admin off) + AcrPull, Consumption-profile environment, SQL server (Entra-only, identity as admin, `AllowAllWindowsAzureIps`), free-offer database, container app (0.25/0.5Gi, 0..1 replicas, liveness only with `Host` header, env vars), action group, `free_amount_remaining` alert, RG budget. One module, not more: the reader follows one file top to bottom |
| `apiExists` + `existing` image lookup | generated (not in brief) | Without it every `azd provision` resets the image to the placeholder; this is azd's documented upsert pattern, inlined (about six lines) instead of the AVM module |
| `src/UrlShortener.Api/Dockerfile` | generated | Two stages. **SDK pinned to `sdk:10.0.101`** (not `10.0` as briefed): `global.json` pins 10.0.101 because analyzers drift between SDK patches and warnings are errors (CLAUDE.md §9); a floating SDK tag would reintroduce that. Runtime `aspnet:10.0` floats for security patches. Copies props + csproj, restores, then copies the project. `USER $APP_UID` (1654, the image's `app` user; numeric so a platform can verify non-root) |
| `.dockerignore` | generated | Excludes `**/bin`, `**/obj` (a Windows `project.assets.json` would break the Linux restore), `.git`, `.azure`, `.env*`, `secrets.json`, docs/tests/perf/infra, tool folders |
| `src/UrlShortener.Api/Data/DatabaseOptions.cs` | generated | `Database:MigrateOnStartup`, default false. No validation (a bool) |
| `src/UrlShortener.Api/Program.cs` | edited | Options binding; the `IsDevelopment()` block now only maps OpenAPI/Scalar; migration + connection-string guard moved under `MigrateOnStartup` |
| `src/UrlShortener.Api/appsettings.Development.json` | edited | `Database:MigrateOnStartup: true` (keeps `dotnet run` and the Development integration tests migrating) |
| `src/UrlShortener.Api/appsettings.json` | edited | #25: `Microsoft.EntityFrameworkCore.Database.Command: Warning`. Development's `Logging` section does not set this category, so it applies there too |
| `Directory.Packages.props`, csproj: `Microsoft.Data.SqlClient.Extensions.Azure` | **rejected** | Pre-approved in the brief, not added. Evidence below: EF Core 10.0.12 → SqlClient 6.1.6, which still contains the Entra providers; the package belongs to the 7.x line. CLAUDE.md §9 gotcha corrected from "6+" to "7+" |
| Connection string `User Id=<client id>` | edited (brief template incomplete) | The brief's string had no `User Id`; with only a user-assigned identity, `DefaultAzureCredential` without a client id looks for a system-assigned identity and fails. Client id comes from `identity.properties.clientId` |
| `docs/adr/0006-azure-hosting-and-migrations.md` | generated | MADR 4.0, status proposed |
| ADR renumbering 0006 → 0007 | edited | Number only in `CLAUDE.md` §7, ADR-0003 line 26, `HANDOFF.md` line 21, `plan.md` line 141, AI log 004 line 114. **AI log 004 line 82 also says ADR-0006** for the expiry decision; left unchanged because the brief named line 114 only. Coordinator to decide |
| `docs/SETUP.md` | edited | New "Deploy to Azure": prerequisites, `azd auth login` / `azd up` prompts, curl, cold start, logs, cost idle vs running, teardown |
| `.github/workflows/ci.yml` | edited | New job `bicep`: `az bicep lint`, then grep for `: Warning ` / `: Error ` (lint exits 0 on warnings, checked) |
| `CLAUDE.md` §9 | edited | SqlClient gotcha corrected; three new: lint exit code, `azd monitor` needs App Insights, preview needs `AZURE_SUBSCRIPTION_ID` |
| (proposal) `azd monitor` for logs in SETUP | rejected | Brief suggested it; `azd monitor --help` shows `--live/--logs/--overview` all open Application Insights, which B6 does not create |
| (proposal) readiness/startup probe on `/health/ready` | rejected | Would keep the serverless DB online (CLAUDE.md §9). ACA adds default TCP startup/readiness probes, which do not touch the DB |
| (proposal) `AllowedHosts=*` in Bicep | rejected | Not needed: the probe spec supports `httpHeaders`, so the probe sends `Host: <fqdn>` and host filtering stays on |

## Docs verified (official sources)
| Claim | Source |
|---|---|
| Probe `httpGet.httpHeaders`; "You probably want to set "Host" in httpHeaders instead"; `initialDelaySeconds` 1-60 | [health probes](https://learn.microsoft.com/azure/container-apps/health-probes); REST spec `Microsoft.App/ContainerApps/stable/2025-07-01/CommonDefinitions.json` (`ContainerAppProbe.httpGet`, fetched raw from GitHub) |
| Default TCP startup/readiness probes added when ingress is on and none defined | [health probes, default configuration](https://learn.microsoft.com/azure/container-apps/health-probes#default-configuration) |
| `X-Forwarded-Proto` overwritten; `X-Forwarded-For` "appended to. Only the rightmost IP is provided by Azure Container Apps" | [ingress HTTP headers](https://learn.microsoft.com/azure/container-apps/ingress-overview#http-headers) |
| ASP.NET Core 8+ images listen on 8080 | [.NET on Container Apps](https://learn.microsoft.com/azure/container-apps/dotnet-overview#define-a-target-port); `docker image inspect`: `ASPNETCORE_HTTP_PORTS=8080`, `APP_UID=1654` |
| Free offer: 100k vCore-s, 32 GB, up to 4 vCores, LRS backups, AutoPause vs bill, cannot revert from bill, region fixed for later free DBs | [free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer?view=azuresql) |
| `freeLimitExhaustionBehavior` values `AutoPause` / `BillOverUsage` | [@azure/arm-sql FreeLimitExhaustionBehavior](https://learn.microsoft.com/javascript/api/@azure/arm-sql/freelimitexhaustionbehavior?view=azure-node-latest) |
| `GP_S_Gen5_1` = 0.5-1 vCores; GP auto-pause 15-10,080 min | [vCore resource limits](https://learn.microsoft.com/azure/azure-sql/database/resource-limits-vcore-single-databases?view=azuresql#general-purpose---serverless-compute---standard-series-gen5), [serverless defaults](https://learn.microsoft.com/azure/azure-sql/database/serverless-tier-overview?view=azuresql#default-settings) |
| Metric `free_amount_remaining`, Count, Avg/Max/Min, grains PT15M..P1D | [SQL database metrics](https://learn.microsoft.com/azure/azure-monitor/reference/supported-metrics/microsoft-sql-servers-databases-metrics) |
| Managed identities can be the server's Entra admin | [Entra admin](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-overview?view=azuresql#microsoft-entra-administrator); AVM `sql/server` README (`principalType` `Application`, `sid` = object id) |
| 0.0.0.0 rule admits other customers' subscriptions | [SQL firewall](https://learn.microsoft.com/azure/azure-sql/database/firewall-configure?view=azuresql#connections-from-inside-azure) |
| ACA outbound IPs "might change over time" | [ACA networking](https://learn.microsoft.com/azure/container-apps/networking#ports-and-ip-addresses) |
| Budgets: contact emails/groups at "Subscription or Resource Group scopes"; start date first of month | [Microsoft.Consumption/budgets](https://learn.microsoft.com/azure/templates/microsoft.consumption/budgets), [CostManagement budgets](https://learn.microsoft.com/azure/templates/microsoft.costmanagement/budgets#property-values) |
| azd upsert (`SERVICE_{NAME}_RESOURCE_EXISTS`), `azd-service-name` tag | [azd Container Apps workflows](https://learn.microsoft.com/azure/developer/azure-developer-cli/container-apps-workflows) |
| `azure.yaml` `docker.path` / `context` / `remoteBuild` | azure.yaml JSON schema (`schemas/v1.0/azure.yaml.json` in Azure/azure-dev) |
| EF Core 9+ migration lock; runtime migration trade-offs | [applying migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying) |
| SqlClient Entra providers moved out in 7.0 | [SqlClient 7.0.0 release notes](https://github.com/dotnet/SqlClient/blob/main/release-notes/7.0/7.0.0.md); contradicted by [ASP.NET Core 11 breaking change](https://learn.microsoft.com/aspnet/core/breaking-changes/11/sqlclient-azure-extensions-required?view=aspnetcore-10.0) ("Starting in ... 6.0") |

## Checks run

### SqlClient: which version, and are the Entra providers inside?
```
$ grep '"Microsoft.Data.SqlClient/' src/UrlShortener.Api/obj/project.assets.json
      "Microsoft.Data.SqlClient/6.1.6": { "dependencies": { "Azure.Core": "1.50.0", "Azure.Identity": "1.17.1", ... } }

$ nuspec dependencies (nuget.org flat container)
== 6.1.6: Azure.Core 1.50.0, Azure.Identity 1.17.1, Microsoft.Identity.Client 4.84.2, ...
== 7.0.0: Microsoft.Data.SqlClient.Extensions.Abstractions 1.0.0, ... (no Azure.*)

$ Microsoft.Data.SqlClient.Extensions.Azure on nuget.org: versions 1.0.0, 7.0.2, 7.0.3, 7.1.0, 7.1.1 (latest stable),
  owners Microsoft + nugetsqltools, verified, 8.5M downloads, project https://aka.ms/sqlclientproject;
  every version depends on Microsoft.Data.SqlClient.Extensions.Abstractions (1.0.0 or 7.1.1), the 7.x line

$ dotnet fsi check.fsx <nuget cache>/microsoft.data.sqlclient/6.1.6/runtimes/unix/lib/net9.0/Microsoft.Data.SqlClient.dll
Microsoft.Data.SqlClient.ActiveDirectoryAuthenticationProvider public=true
assembly Microsoft.Data.SqlClient 6.0.0.0
references Azure.Core 1.50.0.0
references Azure.Identity 1.17.1.0
```
(`check.fsx` reads the assembly's metadata with `System.Reflection.Metadata`; scratch file, not committed.)

### Build, format, tests, vulnerable packages
```
$ dotnet build -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet test -c Release --no-build
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 311 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    50, Skipped:     0, Total:    50, Duration: 38 s - UrlShortener.IntegrationTests.dll (net10.0)

$ dotnet format --verify-no-changes
FORMAT_EXIT=0

$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.
```

### Docker image
```
$ docker build -f src/UrlShortener.Api/Dockerfile -t urlshortener-api:b6 .
#2 [internal] load metadata for mcr.microsoft.com/dotnet/sdk:10.0.101
#14 [build 7/7] RUN dotnet publish ... -c Release --no-restore -o /app/publish -p:UseAppHost=false
#14 13.19   UrlShortener.Api -> /app/publish/
#16 naming to docker.io/library/urlshortener-api:b6 done
EXIT=0

$ docker image inspect urlshortener-api:b6 --format '{{json .Config.Env}} user={{.Config.User}}'
[...,"APP_UID=1654","ASPNETCORE_HTTP_PORTS=8080","DOTNET_RUNNING_IN_CONTAINER=true","DOTNET_VERSION=10.0.12","ASPNET_VERSION=10.0.12"] user=1654
$ docker run --rm --entrypoint id urlshortener-api:b6
uid=1654(app) gid=1654(app) groups=1654(app)
```

### Container smoke test (Production, the Bicep env vars, throwaway SQL Server)
Script in the scratchpad: checks ports 1433/8080 are free; `MSSQL_SA_PASSWORD` generated in the script (fixed complexity prefix + `openssl rand -hex 16`), passed only through the environment, never printed; **separate compose project `b6smoke`** so the engineer's `sqlserver-data` volume is never touched; API container on `b6smoke_default` with `ASPNETCORE_ENVIRONMENT=Production`, `Database__MigrateOnStartup=true`, `PublicUrl__BaseUrl=http://localhost:8080`, `PublicUrl__TrustForwardedHeaders=true`, `AllowedHosts=localhost`, connection string from the environment; `docker compose -p b6smoke down -v` on exit (verified: no `b6smoke` container or volume left).
```
$ curl -i http://localhost:8080/health/live
HTTP/1.1 200 OK
Content-Type: text/plain
Cache-Control: no-store, no-cache

Healthy
$ curl -i -X POST http://localhost:8080/api/links -H "Content-Type: application/json" -d '{"url":"https://example.com/docs?ref=b6"}'
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/links/PT8p8Dh

{"code":"PT8p8Dh","shortUrl":"http://localhost:8080/PT8p8Dh","url":"https://example.com/docs?ref=b6","createdAt":"2026-10-11T04:01:06.6215047+00:00","expiresAt":null}

$ curl -i http://localhost:8080/PT8p8Dh
HTTP/1.1 302 Found
Content-Length: 0
Cache-Control: private, no-store
Location: https://example.com/docs?ref=b6

$ curl -i http://localhost:8080/api/links/PT8p8Dh/stats
HTTP/1.1 200 OK
{"code":"PT8p8Dh","totalClicks":1,"byDay":[...,{"date":"2026-10-11","clicks":1}],"topReferrers":[]}
$ curl -i -H "Host: evil.example" http://localhost:8080/health/live   # host filtering
HTTP/1.1 400 Bad Request
$ curl -i http://localhost:8080/health/ready
HTTP/1.1 200 OK

--- container log: Executed DbCommand lines (expect 0)
0
--- container log: migration lines
info: Microsoft.EntityFrameworkCore.Migrations[20411]
      Acquiring an exclusive lock for migration application. ...
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20261010213557_InitialCreate'.
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20261011001449_AddClicks'.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:8080
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]
      Failed to determine the https port for redirect.
info: UrlShortener.Api.Links.CreateLinkEndpoint[962548513]
      Created short link PT8p8Dh for host example.com
```
What this shows: the switch migrates outside Development; host filtering passes the configured host and rejects others (so the probe needs `Host: <fqdn>`); `/health/live` is not redirected by `UseHttpsRedirection` when no HTTPS port is known (the warning above; on Azure the ingress terminates TLS and `X-Forwarded-Proto: https` is honoured); #25 holds (0 `Executed DbCommand` after a create, a redirect, a stats read and a click insert). #25 was checked in the container (Production), not under `dotnet run`; the category is set in the base `appsettings.json` and not overridden by Development.

### azd
```
$ azd version
azd version 1.35.1 (commit 0651d77b...) (stable)
$ azd auth login --check-status
Logged in to Azure as <redacted>
$ azd env new b6preview --location eastus2 --no-prompt
New environment 'b6preview' was set as default
$ azd env config set infra.parameters.alertEmail nobody@example.invalid   # preview only
$ azd provision --preview --no-prompt
Downloading Bicep
  (✓) Done: Downloading Bicep
ERROR: initializing provisioning manager: prompt required
Missing required inputs:
• subscription
    Provide one of:
      Environment: AZURE_SUBSCRIPTION_ID
```
Not pursued: azd has no default subscription configured, and setting one would mean reading and passing a subscription id. **The what-if preview is therefore not run; the engineer runs `azd provision --preview` first** (see below). The throwaway `b6preview` environment was removed afterwards (`.azure/` is gitignored and was never read).
```
$ azd package api --no-prompt
Packaging service api (Tagging container image)
  (✓) Done: Packaging service api
  - Container: dotnet-urlshortener/api-b6preview:azd-deploy-1791691512
SUCCESS: Your application was packaged for Azure in 2 seconds.
$ docker history dotnet-urlshortener-api:latest --format '{{.CreatedBy}}' | head -3
ENTRYPOINT ["dotnet" "UrlShortener.Api.dll"]
USER 1654
COPY /app/publish . # buildkit
```

### Bicep (binary azd downloaded to `~/.azd/bin`)
```
$ bicep --version
Bicep CLI version 0.47.16 (3f73e1a234)
$ bicep build infra/main.bicep --stdout > /dev/null ; echo $?
0
$ bicep lint infra/main.bicep ; echo $?
0                                   # no diagnostics printed
# negative control: a copy with an invented property on the database
$ bicep lint <copy>/main.bicep
...resources.bicep(126,5) : Warning BCP037: The property "notARealProperty" is not allowed on objects of type "DatabaseProperties". ...
$ echo $?    # lint exit code with that warning
0
# CI's grep on the same output: exit 1 for the copy, 0 for infra/
```
So `useFreeLimit`, `freeLimitExhaustionBehavior`, the probe `httpHeaders` and every other property are known to the type system for the API versions used (an unknown one warns, as the control shows).

The image upsert reads the app it also deploys, so the compiled ARM was checked for a self-dependency:
```
$ bicep build infra/resources.bicep --stdout   # the container app resource, summarised
deployed  dependsOn = [roleAssignment(AcrPull), managedEnvironments/cae-..., userAssignedIdentities/id-..., registries/cr..., servers/sql-...]
image = [if(parameters('apiExists'), reference(resourceId('Microsoft.App/containerApps', variables('appName')), '2025-01-01').template.containers[0].image, 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest')]
```
No `dependsOn` on itself; the lookup is a `reference()` by resource id with an explicit API version inside `if()`, so only the chosen branch is evaluated. Whether ARM accepts this on a real deployment is **not verified** (the what-if preview did not run). It is the main reason to run `azd provision --preview` before `azd up`; if ARM reports a circular dependency, move the container app into one extra module (`infra/api.bicep`) and pass the image in, as the azd docs' AVM pattern does.

## B6 checklist (from HANDOFF / #9 / B5)
| Item | Status |
|---|---|
| `PublicUrl__BaseUrl` | done: `https://<fqdn>` in Bicep |
| `AllowedHosts` | done: the FQDN in Bicep; `appsettings.Production.json` stays `localhost` (fail closed) |
| `PublicUrl__TrustForwardedHeaders=true` | done |
| Which `Host` the probes send | done: the probe sets `Host: <fqdn>` via `httpHeaders` (spec-verified); not observed live yet |
| Ingress appends exactly one `X-Forwarded-For` entry | done (docs): "appended to. Only the rightmost IP is provided by Azure Container Apps" → `ForwardLimit = 1` correct; not observed live yet |
| #25 EF command logging | done: `Warning`, 0 lines in the container log |
| Measure the Azure SQL round trip from the container (B5's first cache trigger) | **not done**: needs the live deployment |
| Production migration path | done: ADR-0006 |
| Telemetry add-on | out of scope (issue #28) |

## Decisions the engineer should question
1. **App identity is the SQL Entra admin** (`db_owner` everywhere on the server). Accepted for a one-week prototype; ADR-0006 gives the least-privilege alternative.
2. **Startup migration in Production.** Safe only with `maxReplicas: 1`; the triggers to move to a bundle job or a script are in the ADR.
3. **No `Extensions.Azure` package** despite the brief and the old §9 gotcha. If you want it anyway "for .NET 11", it should come with SqlClient 7 in the same change, not before.
4. **SDK image pinned to 10.0.101** instead of `sdk:10.0`; runtime image floats.
5. **0.0.0.0 firewall rule** with Entra-only auth as the only gate.
6. **`Active Directory Default`** rather than `Active Directory Managed Identity` (docs prefer a specific mode in production for first-connection latency).
7. **Placeholder image** (`containerapps-helloworld`, port 80) on the first provision: if a replica of it starts before `azd deploy`, its liveness probe on 8080 fails until the real image lands. Unverified live; `minReplicas: 0` should mean it never starts.
8. Budget `startDate` defaults to the first of the deployment month; re-provisioning in a later month may be rejected (not expected within a week).

## Engineer's run (evidence to paste in the PR)
```bash
azd auth login
azd env new <name>                     # e.g. urlshortener; choose subscription + location
azd provision --preview                # what-if; prompts for alertEmail
azd up
API=$(azd env get-value API_BASE_URL)
curl -i $API/health/live
curl -i -X POST $API/api/links -H "Content-Type: application/json" -d '{"url":"https://example.com/docs"}'
curl -i $API/<code>                    # 302 + Cache-Control: private, no-store
sleep 3; curl -i $API/api/links/<code>/stats
```
Paste: the `azd up` summary (resource names only), the four curl outputs, and the app's Log stream lines for the first request (migration lines, no `Executed DbCommand`, no probe 400s in the system log).

## Gotchas encountered
Added to CLAUDE.md §9 in this change: SqlClient 7+ (corrected from 6+); `bicep lint` exits 0 on warnings; `azd monitor` only opens Application Insights; `azd provision --preview --no-prompt` needs `AZURE_SUBSCRIPTION_ID`.

## Coordinator review
Fable 5.1 (coordinator session), after the subagent's hand-back. Gates re-run by the coordinator before the reviews: build 0/0, format exit 0, `bicep lint` (0.47.16) no diagnostics, no vulnerable packages, 47 unit + 50 integration passed.

**Brief departures accepted:** no `Microsoft.Data.SqlClient.Extensions.Azure` (the subagent's package-metadata evidence beats the Learn page; §9 gotcha corrected rather than deleted), `User Id=<client id>` in the connection string (required for a user-assigned identity), SDK image pinned to `10.0.101` (same reason as `global.json`), inline image upsert (`apiExists`), `azd monitor` dropped from SETUP. Coordinator added the ADR-0001 "resolved by ADR-0006" note and renumbered AI log 004 line 82.

**`/security-review` (fresh Opus agent, committed diff): no findings.** Examined SQL network exposure (0.0.0.0 rule + Entra-only, no token path), identity scope (AcrPull on the registry only; SQL admin accepted by the ADR), secrets in template/parameters/env (client id, FQDN and workspace id are identifiers; `listKeys()` stays inside ARM; nothing output), forwarded-headers trust (only reachable through the ingress), the image (non-root, no secrets copied), CI (offline lint, no untrusted input). It noted that `DELETE /api/links/{code}` and `/stats` are unauthenticated and become internet-reachable with this deploy: pre-existing by design (no auth is a recorded assumption in `docs/plan.md`), surfaced in the PR.

**Fresh-context `/code-review`: 7 findings → 6 fixed, 1 deferred.**

| # | Finding | Outcome |
|---|---|---|
| 1 | `maxReplicas: 1` does not cover a deployment: in `Single` revision mode the old revision serves until the new one is ready, so the new container migrates while the old image still runs (and two rate-limit windows exist for a few seconds). The ADR listed this as a trigger but the config does not prevent it. | **Fixed (docs):** ADR-0006 "Rollout window" states the rule that follows: migrations shipped with `azd deploy` must be backward-compatible with the previous image (expand/contract). The two migrations so far only add. |
| 2 | With the free allowance exhausted, `MigrateAsync` before `app.Run()` makes the container exit and restart; `SETUP.md` said "the app answers 500", which is wrong. | **Fixed (docs):** SETUP and ADR-0006 now describe the restart behaviour. Fail-fast kept on purpose (a wrong connection string stays loud); log-and-continue rejected as code for a condition the alerts already announce. |
| 3 | `budgetStartDate` defaults to `utcNow('yyyy-MM-01')`, so a re-provision in a later month changes the budget's start date; unverified whether ARM accepts the update. | **Deferred:** already in the ADR consequences; one-week lifetime; the fix (a stored env value) adds a manual step for a case not expected to occur. |
| 4 | CI `bicep` job lints with whatever Bicep `az` ships that day and fails on any warning, so a new linter rule could turn CI red on an unchanged branch (the `global.json` lesson). | **Fixed:** `az bicep install --version v0.47.16` before lint, matching the local run. |
| 5 | Renumbering applied to AI log 004 but not 006 and 011. | **Fixed:** number-only edits in both, with "(renumbered in B6)" so the history stays readable. |
| 6 | `DatabaseOptions` registered without `ValidateDataAnnotations().ValidateOnStart()` (CLAUDE.md §7). | **Fixed:** added; one line. |
| 7 | No test for the production path (`Production` + `MigrateOnStartup=true`); the only evidence was the manual container run. | **Fixed:** `LinkPersistenceTests.ProductionMigratesAtStartupWhenSwitchedOn` starts the app as Production against a database that does not exist yet on the class's container and asserts no pending migrations and `/health/ready` 200. In the existing schema test class, so no extra SQL Server container. |

Tests after the fixes: 47 unit + 51 integration (one added).

## Sign-off
Reviewed the full diff, understood it, accept ownership. — <name>, YYYY-MM-DD
