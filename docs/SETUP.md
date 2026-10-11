# Setup

Clone, run, and exercise the API in about ten minutes. Commands are for a bash-style shell (Git Bash on Windows works).

## Prerequisites

| Tool | Version | Why |
|---|---|---|
| .NET SDK | 10.0.101 (pinned in [`global.json`](../global.json), `rollForward: latestPatch`) | Build and run. CI uses the same pin so local and CI analyzers match |
| Docker Desktop (daemon running) | any recent | Local SQL Server via `docker compose`; integration tests start their own container (Testcontainers) |
| `curl` | any | The walkthrough below |
| `gh` | optional | Only for checking out PRs or issues |

There is no other dependency: no Azure subscription is needed to run locally. See [ADR-0001](adr/0001-stack-and-database.md) for why Docker is required.

## 1. Clone and start SQL Server

```bash
git clone https://github.com/mohammed-saalim/dotnet-urlshortener.git
cd dotnet-urlshortener

export MSSQL_SA_PASSWORD='<choose a strong password>'   # or put MSSQL_SA_PASSWORD=... in a gitignored .env (see .env.example)
docker compose up -d --wait
```

Compose refuses to start without `MSSQL_SA_PASSWORD` (there is deliberately no default). The password is fixed when the data volume is first created; to change it later run `docker compose down -v` first. `--wait` returns once the container's health check passes.

## 2. Give the API its connection string

The connection string is never committed. Store it with user-secrets (use the same password as above):

```bash
dotnet user-secrets set --project src/UrlShortener.Api "ConnectionStrings:UrlShortener" \
  "Server=localhost,1433;Database=UrlShortener;User Id=sa;Password=<the password above>;TrustServerCertificate=True"
```

`TrustServerCertificate=True` is needed only because the dev container uses a self-signed certificate.

## 3. Run the API

```bash
dotnet run --project src/UrlShortener.Api
```

The first launch profile (`http`, see [`launchSettings.json`](../src/UrlShortener.Api/Properties/launchSettings.json)) listens on **http://localhost:5078** in the Development environment. In Development only, the app applies EF Core migrations at startup, so the database is ready without any extra step.

- Scalar API reference: http://localhost:5078/scalar (Development only)
- OpenAPI document: http://localhost:5078/openapi/v1.json

## 4. Try it with curl

Run these in a second terminal.

```bash
# Create a short link (201 + Location header + JSON body with "code" and "shortUrl")
curl -i -X POST http://localhost:5078/api/links \
  -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/docs?ref=walkthrough"}'

# Keep the generated code for the next calls
CODE=$(curl -s -X POST http://localhost:5078/api/links -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/docs"}' | sed -E 's/.*"code":"([^"]+)".*/\1/')
echo "$CODE"

# Get its metadata (200)
curl -i http://localhost:5078/api/links/$CODE

# The redirect itself, not followed (302, Location, Cache-Control: private, no-store)
curl -i http://localhost:5078/$CODE

# Delete it (204, soft delete)
curl -i -X DELETE http://localhost:5078/api/links/$CODE

# The code is now gone (404, application/problem+json)
curl -i http://localhost:5078/$CODE

# Health: liveness never touches the database; readiness does
curl -i http://localhost:5078/health/live
curl -i http://localhost:5078/health/ready
```

Optional extras: a custom alias and an expiry (alias is 4 to 10 Base62 characters):

```bash
curl -i -X POST http://localhost:5078/api/links -H "Content-Type: application/json" \
  -d '{"url":"https://example.com","customAlias":"MyAlias1","expiresAt":"2030-01-01T00:00:00Z"}'
```

Expected outputs are pasted in [AI log 005](ai-log/005-api-surface.md) ("curl walkthrough"). Expiry is stored but **not enforced yet**; that is a Day 3 decision (see [`docs/plan.md`](plan.md)).

## Run the tests

```bash
dotnet test
```

This runs the unit tests and the integration tests. Integration tests start a real SQL Server container per test class through Testcontainers, so Docker must be running. The first run pulls `mcr.microsoft.com/mssql/server:2022-latest` (about 1.5 GB), which can take several minutes; later runs reuse the image. You do not need `docker compose up` for tests.

## Quality gates

These are the same checks CI runs ([`ci.yml`](../.github/workflows/ci.yml)):

```bash
dotnet build -c Release                                   # warnings are errors
dotnet format --verify-no-changes
dotnet list package --vulnerable --include-transitive
```

Build in Release as well as Debug: the CI log showed analyzer errors the Debug build did not (the root cause turned out to be an SDK mismatch, now prevented by `global.json`; see [`CLAUDE.md`](../CLAUDE.md) section 9).

## Stop

```bash
docker compose down        # keep the data volume
docker compose down -v     # also delete it (needed to change the SA password)
```

## Deploy to Azure

Optional; nothing above needs it. Decisions and their reasons are in [ADR-0006](adr/0006-azure-hosting-and-migrations.md).

**What gets created** (one resource group, `rg-<environment name>`): a Container Apps environment (consumption) with one container app, an Azure SQL logical server with one **free-offer** serverless database, a Container Registry (Basic), a Log Analytics workspace with a workspace-based Application Insights resource, a user-assigned managed identity, an email action group, a metric alert on the database's free allowance and a monthly budget. No password or key is created or stored anywhere: SQL is Entra-only and the app signs in with its managed identity.

### Prerequisites

| Tool | Why |
|---|---|
| Azure Developer CLI (`azd`) | Provisions `infra/` and deploys the image ([install](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)) |
| Docker Desktop (running) | `azd` builds `src/UrlShortener.Api/Dockerfile` locally and pushes it to the registry |
| An Azure subscription | Owner or Contributor + User Access Administrator on it (the template creates a role assignment) |

To build in the registry instead of locally (no Docker needed), set `remoteBuild: true` under `docker:` in [`azure.yaml`](../azure.yaml). Not tested here; ACR Tasks can be unavailable on some subscription types, and `azd` then falls back to a local build.

### Deploy

```bash
azd auth login
azd up
```

`azd up` asks for:

1. an **environment name** (for example `urlshortener`): names the resource group and seeds every resource name;
2. the **subscription** and the **location** (any region with Container Apps and Azure SQL; the free-offer database fixes the region for later free databases in the subscription);
3. **`alertEmail`**: where the cost alerts go. It has no default and is stored only in the gitignored `.azure/<env>/` folder.

It then provisions (first run: several minutes), builds and pushes the image, and points the app at it. The app's address is printed and saved as `API_BASE_URL`:

```bash
API=$(azd env get-value API_BASE_URL)
curl -i $API/health/live
curl -i -X POST $API/api/links -H "Content-Type: application/json" -d '{"url":"https://example.com/docs"}'
CODE=$(curl -s -X POST $API/api/links -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/docs"}' | sed -E 's/.*"code":"([^"]+)".*/\1/')
curl -i $API/$CODE                    # 302
curl -i $API/api/links/$CODE/stats    # click counted a moment later
```

`azd provision --preview` shows what would change without changing anything. After code changes, `azd deploy` rebuilds and redeploys only the image.

### Cold start

The app scales to zero after a few idle minutes, and the database auto-pauses after 15 idle minutes. The first request after idle therefore starts a container, runs the startup migration check, and resumes the database: it can take **tens of seconds**. `EnableRetryOnFailure` (6 retries, up to 30 s apart) covers the database resume; occasionally the very first request after a long idle may still fail once. Retry it.

### Logs

In the portal: the container app, then **Monitoring > Log stream** (live console output) or **Monitoring > Logs** (Log Analytics, table `ContainerAppConsoleLogs_CL`; system events such as probe failures in `ContainerAppSystemLogs_CL`). `azd monitor --logs` opens the Application Insights logs (requests, dependencies, traces; see Telemetry below). Plain `azd monitor` means `--overview`, which needs a portal dashboard that this template does not create, so it fails. EF Core SQL command logging is off (`Warning`, issue #25) to keep log volume and cost down.

### Telemetry

Operational telemetry (issue #28) goes to the Application Insights resource through the Azure Monitor OpenTelemetry distro. The click analytics behind `/stats` stay in the database (ADR-0004). The app exports only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, which Bicep does on the container app from a secret. Local runs and tests export nothing. In the portal, open the Application Insights resource (`appi-...`), or run `azd monitor --logs` / `azd monitor --live`:

- **Performance** and **Failures**: requests (named after the route template, so the redirect is one row whatever the code), their SQL dependencies (no query text), exceptions.
- **Metrics** → metric namespace **azure.applicationinsights**: `urlshortener.analytics.dropped_clicks` (clicks lost because the queue was full; normally 0). It is exported as delta, so each point is the new drops in that interval, not the running total: use aggregation **Sum** over the time range for the total since a restart. Adding the built-in `Microsoft.AspNetCore.RateLimiting` meter exports all five of its instruments (the request counter plus queue and lease gauges and histograms); only the counter is used here. Metrics Explorer stores custom metrics without dimensions by default, so it shows `aspnetcore.rate_limiting.requests` as all create attempts, accepted and rejected together. For the rejections (the 429s), use **Logs**:

  ```kusto
  customMetrics
  | where name == "aspnetcore.rate_limiting.requests"
  | where tostring(customDimensions["aspnetcore.rate_limiting.result"]) != "acquired"
  | summarize rejected = sum(valueSum) by bin(timestamp, 5m)
  ```

- Application logs (`ILogger`, same levels as the console) also go to the `traces` table, so a log line can be ingested twice: once as console output, once as telemetry. Request traces are sampled at up to 5 per second (the distro's default); logs written inside a request that was sampled out are dropped too, logs outside a request (startup, the click writer) are always sent; metrics are not sampled. App Insights data is stored in the existing Log Analytics workspace, so the 0.5 GB/day cap covers it, and `azd down` deletes it with the resource group.

### Cost

- **Idle: near zero.** No replica runs (scale to zero, inside the Container Apps monthly free grant), the database is paused (free offer: 100,000 vCore-seconds and 32 GB per month), and Log Analytics ingests almost nothing. The **one fixed cost is the Container Registry Basic tier** (a few US dollars a month; exact price not fetched).
- **Left running or under load:** container vCPU/memory beyond the free grant, Log Analytics ingestion beyond its free allowance (capped at 0.5 GB/day), registry storage. **Database compute cannot bill**: when the free allowance runs out, the database pauses until the next month (`freeLimitExhaustionBehavior: AutoPause`); a container that starts in that state exits at the startup migration and is restarted by the platform, so the address answers with platform errors until the allowance resets (ADR-0006).
- **Alerts** (email to `alertEmail`): the free allowance falls below 20 %; actual cost passes 80 % of the monthly budget (`monthlyBudget`, default 10). Alerts notify only; they do not stop anything.

### Tear down

```bash
azd down --force --purge
```

Deletes the resource group and everything in it (`--force`: no confirmation prompt; `--purge`: permanently deletes resources that would otherwise stay soft-deleted, such as key vaults; none are created here, it is there so the command stays correct if one is added). **Tear down about one week after the reviewer finishes**; set a calendar reminder when you deploy.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `required variable MSSQL_SA_PASSWORD is missing a value` from compose | The variable is mandatory by design. Export it or put it in `.env` |
| SQL Server container exits or health check never passes | SQL Server enforces a password complexity rule (see `.env.example`). If you changed the password after the first start, run `docker compose down -v` so the volume is recreated |
| `dotnet run` throws `ConnectionStrings:UrlShortener is not configured. Set it with dotnet user-secrets set` | Step 2 was skipped or run against a different project. Use `--project src/UrlShortener.Api` |
| `docker compose exec ... /opt/mssql-tools18/...` fails with a mangled path (Git Bash) | Git Bash rewrites Unix-style paths. Prefix the command with `MSYS_NO_PATHCONV=1` |
| `docker` or `azd` "not found" right after installing | Newly installed CLIs are not on PATH in an already-open shell. Restart the terminal |
| Build errors that CI reports but you do not see (analyzer rules) | Different SDK. Install the version in `global.json`; do not rely on a newer SDK |
