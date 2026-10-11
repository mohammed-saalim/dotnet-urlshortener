# ADR-0006: Host on Azure Container Apps + Azure SQL free offer via azd, migrate at startup

- Status: proposed (infra and the production migration path are high-impact; needs the engineer's sign-off in the PR)
- Date: 2026-10-10
- Deciders: Mohammed Saalim
- AI assistance: Claude Code drafted options and Bicep; decision and wording are the engineer's

## Context and problem statement
The prototype must run in Azure for the reviewer at near-zero idle cost, with no secret anywhere, a one-line deploy (`azd up`) and a one-line teardown (`azd down`). ADR-0001 chose SQL Server everywhere and left two items open: **how production applies EF Core migrations**, and `UseSqlServer` vs `UseAzureSql`. Issue #9 left a checklist: the probe `Host` header under host filtering, the number of `X-Forwarded-For` entries the ingress adds, `AllowedHosts`. Which hosting shape, identity model and migration path?

## Decision drivers
- Idle cost near zero; a hard stop on the one meter that can run away (database compute).
- No password, key or connection-string secret in the repo, the parameters or the app settings.
- `/health/live` must not touch the database (CLAUDE.md §9: probes would keep serverless SQL online).
- Single command up and down; the engineer runs both (`azd auth login`, `azd up`, `azd down`).
- Torn down about one week after the reviewer finishes. Prototype trade-offs are acceptable if written down.

## Considered options
Hosting: (A) Container Apps consumption + Azure SQL free offer, (B) App Service + Azure SQL, (C) Container Apps with a database in a sidecar container.
Migrations: (1) at startup behind a switch, (2) idempotent SQL script run by a pipeline step, (3) EF migration bundle run as a Container Apps job.
SQL identity: (i) the app's managed identity is the server's Entra admin, (ii) the engineer is the Entra admin and creates a contained user for the app.
Registry: (a) Azure Container Registry Basic, (b) GitHub Container Registry.

## Decision outcome
Chosen: **A + 1 + i + a**.

**Layout.** `azure.yaml` (one service `api`, `host: containerapp`, Dockerfile build with the repo root as context), `infra/main.bicep` (subscription scope: resource group + one module), `infra/resources.bicep` (everything else). One module because a reader can follow one file top to bottom; splitting per resource adds wiring, not clarity.

**Container Apps.** Consumption workload profile environment, Log Analytics for its logs (30 days, 0.5 GB/day cap). One app: external HTTPS ingress (`allowInsecure: false`, so plain HTTP is redirected at the edge), target port 8080 (the `aspnet:10.0` image default, `ASPNETCORE_HTTP_PORTS=8080`, verified with `docker image inspect`), 0.25 vCPU / 0.5 GiB, **`minReplicas: 0`, `maxReplicas: 1`**.
- Scale to zero: no compute bill while idle; the first request after idle pays a cold start (container start + migration check + serverless SQL resume; tens of seconds).
- One replica, because the rate limiter is in memory (ADR-0005: N replicas = N × the limit) and the startup migration assumes one instance.
- **Liveness probe only**, `GET /health/live`, which never opens a connection. No readiness or startup probe on `/health/ready`: a probe every few seconds that queries the database keeps it from auto-pausing and burns the free allowance. When ingress is on and a probe type is not defined, Container Apps adds its default **TCP** startup/readiness probes on the target port ([health probes](https://learn.microsoft.com/azure/container-apps/health-probes#default-configuration)); a TCP connect does not reach the database, so that is fine.
- **Host filtering and the probe.** `appsettings.Production.json` sets `AllowedHosts: localhost` (fail closed, #9); Bicep sets `AllowedHosts` to the app's FQDN. The probe spec has `httpGet.httpHeaders`, and the REST spec says of `httpGet.host`: "You probably want to set "Host" in httpHeaders instead" ([CommonDefinitions.json 2025-07-01](https://github.com/Azure/azure-rest-api-specs/blob/main/specification/app/resource-manager/Microsoft.App/ContainerApps/stable/2025-07-01/CommonDefinitions.json)). So the liveness probe sends `Host: <fqdn>` and host filtering stays on. The FQDN is computed before the app exists as `${appName}.${environment.properties.defaultDomain}`.
- **Forwarded headers.** `PublicUrl__TrustForwardedHeaders=true`. The ingress sets `X-Forwarded-Proto` (overwriting a client value) and, for `X-Forwarded-For`, "If specified in initial request, it is appended to. Only the rightmost IP is provided by Azure Container Apps" ([ingress HTTP headers](https://learn.microsoft.com/azure/container-apps/ingress-overview#http-headers)). That matches `ForwardLimit = 1` from #9: the app uses exactly the entry the ingress appended. `PublicUrl__BaseUrl=https://<fqdn>`.

**Azure SQL (free offer).** Logical server with **Entra-only authentication** (`azureADOnlyAuthentication: true`, no SQL admin password). Database `GP_S_Gen5`, capacity 1, `minCapacity 0.5`, `autoPauseDelay 15`, `useFreeLimit: true`, **`freeLimitExhaustionBehavior: 'AutoPause'`**, 32 GB max size, `requestedBackupStorageRedundancy: 'Local'`.
- 0.5-1 vCores (`GP_S_Gen5_1`) is the smallest serverless size and 15 minutes the shortest General Purpose auto-pause delay ([resource limits](https://learn.microsoft.com/azure/azure-sql/database/resource-limits-vcore-single-databases?view=azuresql#general-purpose---serverless-compute---standard-series-gen5), [serverless defaults](https://learn.microsoft.com/azure/azure-sql/database/serverless-tier-overview?view=azuresql#default-settings)); both stretch 100,000 vCore-seconds furthest. The free offer allows up to 4 vCores, 32 GB, LRS backups only ([free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer?view=azuresql#offer-limitations)).
- `AutoPause` on exhaustion is the **hard cost stop**: the database pauses until the next calendar month instead of billing. The alternative (`BillOverUsage`) cannot be reverted to `AutoPause` later (same page).
- Collation stays at the server default: the `Code` column carries `Latin1_General_100_CS_AS` itself (migration `InitialCreate`, ADR-0002), so no database-level collation is needed.
- **Network:** public endpoint + the `AllowAllWindowsAzureIps` (0.0.0.0) rule, because Container Apps consumption has no fixed outbound IP ("Outbound IPs might change over time", [networking](https://learn.microsoft.com/azure/container-apps/networking#ports-and-ip-addresses)). The rule admits Azure-hosted clients **in other customers' subscriptions too** ([firewall](https://learn.microsoft.com/azure/azure-sql/database/firewall-configure?view=azuresql#connections-from-inside-azure)); Entra-only authentication is what keeps them out. A private endpoint needs a VNet and costs money; out of scope.

**Identity: one user-assigned managed identity** is the container app's identity, the registry pull identity (`AcrPull`) and the SQL server's **Entra administrator** (`administrators` with `principalType: 'Application'`, `sid` = the identity's principal id). Any Entra security object, "including ... service principals, and managed identities", can be the admin ([Entra admin](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-overview?view=azuresql#microsoft-entra-administrator)). This removes the manual step "sign in as an Entra admin with sqlcmd and `CREATE USER [app] FROM EXTERNAL PROVIDER`", which `azd up` cannot do. **Trade-off:** the app is `db_owner` on every database of the server, i.e. a compromised app can drop the schema. Accepted for a prototype that holds only demo links and lives a week. **Production alternative:** an Entra group (or the engineer) as admin; a contained user for the app with `db_datareader` + `db_datawriter` (+ `db_ddladmin` only while it still migrates at startup), created by a post-provision step.

**Connection string** (an env var, not a secret): `Server=tcp:<server>.database.windows.net,1433;Initial Catalog=urlshortener;Authentication=Active Directory Default;User Id=<identity client id>;Encrypt=True;`. `User Id` is required with a user-assigned identity: without it the credential looks for a system-assigned one ([research notes §5](../research/notes/URL%20shortener%20assignment%20validation/dotnet_azure_claims.md)). The client id is an identifier, not a credential.
- **No `Microsoft.Data.SqlClient.Extensions.Azure` package.** EF Core 10.0.12 brings `Microsoft.Data.SqlClient` **6.1.6**, whose nuspec still depends on `Azure.Identity` 1.17.1 and whose assembly contains the public type `Microsoft.Data.SqlClient.ActiveDirectoryAuthenticationProvider` (AI log 013). The Entra providers moved out in **7.0** ([7.0.0 release notes](https://github.com/dotnet/SqlClient/blob/main/release-notes/7.0/7.0.0.md): "The core `Microsoft.Data.SqlClient` package no longer depends on `Azure.Core`, `Azure.Identity`..."), and every published `Extensions.Azure` version depends on `Extensions.Abstractions`, which only SqlClient 7.x uses. Adding it to a 6.1.6 app would be a mismatched, unused package. The ASP.NET Core 11 breaking-change page says "Starting in Microsoft.Data.SqlClient 6.0" ([page](https://learn.microsoft.com/aspnet/core/breaking-changes/11/sqlclient-azure-extensions-required?view=aspnetcore-10.0)), which contradicts the package metadata and the SqlClient release notes; this ADR follows the primary sources. **Trigger:** the day SqlClient 7.x arrives (EF Core 11 / .NET 11, or a direct pin), add the package.
- `Active Directory Default` rather than `Active Directory Managed Identity`: the brief's choice; the docs prefer a specific mode in production because `DefaultAzureCredential` tries credentials in sequence. In the container the environment and workload-identity credentials are absent and fail fast, so the cost is small. Switching is a one-word change if the cold start shows it.
- `UseSqlServer` stays (ADR-0001 follow-up): `EnableRetryOnFailure` already covers transient faults and the serverless resume; `UseAzureSql` would change no behaviour this prototype depends on.

**Migrations: at startup behind `Database:MigrateOnStartup`** (default `false`; `appsettings.Development.json` and Bicep set `true`). `MigrateAsync` runs before `app.Run()`, so no request is served on an old schema. Acceptable here because: one replica; the schema is owned by the app's identity; the deployment is torn down in a week. EF Core 9+ also takes a database-wide lock (`sp_getapplock` on SQL Server) around runtime migrations ([migration locking](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#migration-locking)); seen in the container log: "Acquiring an exclusive lock for migration application". EF's own guidance still prefers a deployment step "when review, least-privilege credentials, coordinated rollout, or high availability is important" ([apply at runtime](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#apply-migrations-at-runtime)).
**Rollout window.** `maxReplicas: 1` bounds steady state, not a deployment: in `activeRevisionsMode: 'Single'` the old revision keeps serving until the new one is ready ([zero-downtime deployment](https://learn.microsoft.com/azure/container-apps/revisions#zero-downtime-deployment)), so the new container migrates while the old image still answers requests, and for those seconds two rate-limit windows exist. Rule that follows: **migrations shipped with `azd deploy` must be backward-compatible with the previous image** (expand/contract: add before use, drop only after the next image has stopped using it). The two migrations so far only add.
**Paused database at startup.** `MigrateAsync` runs before the app listens, so when the free allowance is exhausted (`AutoPause`) or a resume outlives the retry budget, the process exits and the platform restarts it; the ingress then answers with platform errors, not the app's 500. Accepted: fail-fast keeps a wrong connection string loud, and the condition clears on its own next month or on resume. The alternative (log and continue, let `/health/ready` report it) was rejected as code for a condition the alerts already announce (code review in AI log 013).
**What would change it:** more than one replica (old replicas would run against a new schema mid-rollout), a DBA-owned schema or SQL review gate (→ idempotent script, option 2), least-privilege runtime identity or zero-downtime requirements (→ bundle as a job before traffic shifts, option 3, with expand/contract migrations).

**Registry: ACR Basic**, admin user disabled, pulled with the managed identity. ACR Basic is the one fixed monthly cost of this deployment (a few US dollars a month; price not fetched). GHCR rejected: a private GHCR image needs a registry username + PAT stored as a Container Apps secret, i.e. exactly the secret this design avoids (a public image would avoid it but publishes the image).

**Image on first provision.** The container app must exist before `azd deploy` has pushed an image, so the first revision runs Microsoft's public placeholder (`containerapps-helloworld`). `azd` sets `SERVICE_API_RESOURCE_EXISTS`; when true, Bicep reads the current image from the existing app so a later `azd provision` does not reset it to the placeholder (the upsert pattern from the [azd Container Apps docs](https://learn.microsoft.com/azure/developer/azure-developer-cli/container-apps-workflows#configure-image-based-deployments), inlined instead of the AVM module). The placeholder listens on 80, so if a replica of it starts its liveness probe on 8080 fails until `azd deploy` replaces it; with `minReplicas: 0` it usually does not start at all.

**Cost guardrails.** (a) Free-offer `AutoPause` (hard stop). (b) Metric alert on `free_amount_remaining` (namespace `Microsoft.Sql/servers/databases`, unit Count, grains from PT15M, [metrics reference](https://learn.microsoft.com/azure/azure-monitor/reference/supported-metrics/microsoft-sql-servers-databases-metrics)) below **20,000** vCore-seconds (20 %), hourly, to an action group that emails `alertEmail`. (c) `Microsoft.Consumption/budgets` at **resource-group scope** (supported: "Subscription or Resource Group scopes", [budgets reference](https://learn.microsoft.com/azure/templates/microsoft.consumption/budgets)), `monthlyBudget` default 10, email at 80 % of actual cost. (b) and (c) only notify. `alertEmail` has no default and is not in `main.parameters.json`, so `azd` prompts for it and keeps it in the gitignored `.azure/` folder.

### Consequences
- Good: idle cost is ACR Basic plus storage; compute is zero when idle (scale to zero, auto-pause) and capped by the free offer when used.
- Good: no secret exists anywhere: Entra-only SQL, managed-identity pull, passwordless connection string. The only key (Log Analytics shared key) is read by ARM at deployment time inside the template (`listKeys`) and never leaves Azure.
- Bad: **cold start** after idle can take tens of seconds, and while the database resumes the first connection can fail; `EnableRetryOnFailure` (default 6 retries, up to 30 s between them) usually absorbs it, but the first request after a long idle may still return 500 once. If the database cannot be reached at startup at all (allowance exhausted), the container restarts until it can (see "Paused database at startup").
- Bad: the app identity is the database admin (see Identity above).
- Bad: one replica, no zone redundancy; a crash means a restart, not failover. Fine for a prototype.
- Bad: the 0.0.0.0 firewall rule is broad; mitigated only by Entra-only auth.
- Bad: Log Analytics' 0.5 GB daily cap drops logs for the rest of the day once hit (cost over completeness).
- Bad: the budget's start date defaults to the first of the current month at deployment. Re-provisioning the same environment in a later month changes it; if ARM rejects that, delete the budget or pass `budgetStartDate`. Not expected within a one-week lifetime.
- Neutral: once a free-offer database exists in a subscription, the portal fixes its region for other free databases in that subscription ([free offer limitations](https://learn.microsoft.com/azure/azure-sql/database/free-offer?view=azuresql#offer-limitations)); pick the region once.

### Confirmation
- `bicep lint infra/main.bicep`: no diagnostics (bicep 0.47.16; types for every API version used are known, so an invalid property would warn: checked by injecting one, BCP037). CI job `bicep` runs `az bicep lint` and fails on any warning.
- `docker build` + container run as Production with the Bicep env vars against a throwaway SQL Server: migrations applied at startup, `/health/live` 200, create 201, redirect 302, stats 200, wrong `Host` 400, zero `Executed DbCommand` lines (AI log 013).
- `dotnet test`: 47 unit + 50 integration, unchanged; Development tests still migrate at startup through the new switch.
- Live: `azd up` by the engineer, then curl of create + redirect + stats against `API_BASE_URL` (pending; pasted in the PR).

## Pros and cons of the options
### A. Container Apps consumption + Azure SQL free offer
- Good: scale to zero, monthly free grant, the engine the tests use (ADR-0001).
- Bad: cold starts; no fixed outbound IP.
### B. App Service + Azure SQL
- Good: mature; Microsoft's `todo-csharp-sql` azd template.
- Bad: no scale to zero, so a plan bills while idle (price not fetched).
### C. Container Apps with SQL Server as a sidecar container
- Good: no Azure SQL at all.
- Bad: not the cloud engine ADR-0001 chose; data lost on restart without storage; SQL Server needs about 2 GB of memory.
### 1. Startup migration
- Good: zero extra moving parts; schema always matches the image.
- Bad: needs schema rights at runtime; unsafe with several replicas on a non-backward-compatible change; no review gate.
### 2. Idempotent SQL script in a pipeline step
- Good: reviewable SQL; runtime identity can be least-privilege.
- Bad: a pipeline with a database-capable identity; `sqlcmd` with an Entra token; more for `azd up` to orchestrate.
### 3. Migration bundle in a Container Apps job
- Good: automated, uses EF's lock, runs before traffic shifts.
- Bad: a second image/artifact and a job resource; ordering hooks in `azd`.
### i. App identity as Entra admin
- Good: `azd up` is the whole setup.
- Bad: over-privileged app.
### ii. Engineer as admin + contained user
- Good: least privilege.
- Bad: a manual or scripted `CREATE USER ... FROM EXTERNAL PROVIDER` after provisioning, needing an Entra-authenticated SQL session.
### a. ACR Basic
- Good: identity-based pull; same subscription; torn down with the group.
- Bad: a small fixed monthly charge.
### b. GHCR
- Good: free.
- Bad: private pull needs a PAT stored as a secret.

## More information
- Implementation and evidence: [AI log 013](../ai-log/013-azure-infra.md); usage: [`docs/SETUP.md`](../SETUP.md) "Deploy to Azure".
- Related: [ADR-0001](0001-stack-and-database.md) (open migration path, now decided here), [ADR-0005](0005-rate-limiting.md) (per-replica limiter, forwarded headers).
- Follow-up: issue #28 (operational telemetry), added below as "Telemetry (B7, #28)".

## Telemetry (B7, #28)
Added after the hosting decision above; same status (proposed, needs the engineer's sign-off). Implementation and evidence: [AI log 014](../ai-log/014-telemetry.md).

- **Distro:** `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 (Microsoft / azure-sdk). It brings ASP.NET Core and HttpClient tracing, a vendored SqlClient instrumentation, the `Microsoft.AspNetCore.Hosting` and `System.Net.Http` meters, and an `ILogger` exporter. Operational telemetry only; click analytics stay in the database (ADR-0004).
- **Opt-in by connection string:** `UseAzureMonitor()` is called only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Without it the SDK is not registered at all (the exporter would throw "A connection string was not found"), so local runs and tests export nothing and need no configuration.
- **Metrics:** the app's meter `UrlShortener` has one instrument, `urlshortener.analytics.dropped_clicks` (observable counter over `ClickQueue.Dropped`). Rate-limit rejections use ASP.NET Core's built-in `aspnetcore.rate_limiting.requests` (meter `Microsoft.AspNetCore.RateLimiting`, tags `aspnetcore.rate_limiting.policy` and `aspnetcore.rate_limiting.result`): no code, a standard name. The distro does not collect either meter by default, so both are added with `AddMeter`. The rate-limiting meter brings all five of its instruments (one counter, two up-down counters, two histograms); if only the counter is wanted, the distro README's `AddView(instrumentName, MetricStreamConfiguration.Drop)` drops the rest (not done). The exporter uses delta temporality, so charts show new drops per interval. Consequence: Metrics Explorer stores custom metrics without dimensions by default, so the rejections are read from `customMetrics` in Logs (query in SETUP.md).
- **Infrastructure:** one workspace-based `Microsoft.Insights/components` on the existing Log Analytics workspace. Its data is billed and capped there: the effective daily cap is the lower of the component's and the workspace's ([daily cap](https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap#application-insights)), and no component cap is set, so the 0.5 GB/day workspace cap governs. The connection string reaches the container as a Container Apps **secret** (`secretRef`), not a plain value: it is documented as not secret, but it lets anyone send telemetry into the resource. As a secret it stays out of the portal's environment-variable view.
- **Sampling:** not configured. Since distro 1.5.0 the default is rate-limited, 5 traces per second (changelog "Default Sampler Changed"), well under the cap for this traffic. Logs inside a sampled-out request are dropped with it (`EnableTraceBasedLogsSampler`, default true). Metrics are pre-aggregated and not sampled.
- **PII:** (a) Request telemetry carries the route, path and status. The path holds only the short code, which is already logged. Since 1.1.1 the distro turns OpenTelemetry's `url.query` redaction **off** unless the setting already exists, so Bicep sets `OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION=false`. The value of any query string a client appends to a short link is then stored as `Redacted`. No default request attribute holds the client IP, and Application Insights uses the connection IP only for geolocation, then stores `0.0.0.0` ([IP handling](https://learn.microsoft.com/azure/azure-monitor/app/ip-collection)). (b) The vendored SqlClient instrumentation records no `db.statement` for text commands (`SetDbStatementForText` defaults to false). EF Core sends only text commands, and parameter values are never part of the command text anyway. (c) Logs exported through `ILogger` are the same lines the console already gets, now also kept and queryable for the workspace retention (30 days). The app's own log calls never contain destination URLs or IPs (CLAUDE.md §7). Framework logs can carry an exception message: EF Core logs a failed `SaveChanges` at Error with the `SqlException`, and some SQL errors quote a value (2628 truncation). Click values are length-bounded before insert (referrer host 253, user agent 256, matching the columns) and the destination URL is validated to the column's 2048, so the value-quoting errors cannot occur for the data we store; the residual risk is accepted (code review in AI log 014).
- **Health probes are not traced:** `AspNetCoreTraceInstrumentationOptions.Filter` drops `/health/*` from request telemetry, so liveness probes do not fill the request table on an idle app (they still count in the request-duration metric).
- **Not built, deliberately:** dashboards or workbooks (the built-in Performance and Failures views are enough for a prototype; `azd monitor --overview` therefore still has nothing to open), alerts on the custom metrics, Live Metrics tuning, and **Entra-only ingestion**. The last is the production option: `DisableLocalAuth: true` on the component, the `Monitoring Metrics Publisher` role for the app's identity, and `options.Credential = new ManagedIdentityCredential(...)` in the distro. It needs `Azure.Identity` as a direct package reference (the distro does not depend on it), which this task's no-new-package rule excludes, and it makes the connection string harmless.
