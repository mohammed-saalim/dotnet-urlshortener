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

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `required variable MSSQL_SA_PASSWORD is missing a value` from compose | The variable is mandatory by design. Export it or put it in `.env` |
| SQL Server container exits or health check never passes | SQL Server enforces a password complexity rule (see `.env.example`). If you changed the password after the first start, run `docker compose down -v` so the volume is recreated |
| `dotnet run` throws `ConnectionStrings:UrlShortener is not configured. Set it with dotnet user-secrets set` | Step 2 was skipped or run against a different project. Use `--project src/UrlShortener.Api` |
| `docker compose exec ... /opt/mssql-tools18/...` fails with a mangled path (Git Bash) | Git Bash rewrites Unix-style paths. Prefix the command with `MSYS_NO_PATHCONV=1` |
| `docker` or `azd` "not found" right after installing | Newly installed CLIs are not on PATH in an already-open shell. Restart the terminal |
| Build errors that CI reports but you do not see (analyzer rules) | Different SDK. Install the version in `global.json`; do not rely on a newer SDK |
