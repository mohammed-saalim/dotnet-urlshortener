# AI log 012 — Measure the redirect path, then decide on a cache

| Field | Value |
|---|---|
| Issue | #18 |
| Branch | `feat/12-perf-measure` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | low |

## Intent
Measure redirect latency and throughput with k6 before deciding on a cache, and add an in-process cache only if the numbers justify it (issue #18).

## Constraints given to the AI
- Measure first; no cache on assumption. If the numbers say "no cache", say so and stop. Keep it minimal.
- k6 from the `grafana/k6` Docker image only; no local install.
- Throwaway `docker compose` SQL Server with a generated password passed by environment, never printed or stored; `docker compose down -v` afterwards. Connection string through `ConnectionStrings__UrlShortener`, not user-secrets. Abort if port 1433 or 5078 is in use.
- No new NuGet package unless a cache is justified, and then ask first whether `IMemoryCache` is enough.
- Do not touch the files of the parallel #9 work. No commit, push or PR; do not read `.env`, `secrets.json` or `.azure/`.

## Acceptance criteria (runnable)
- k6 script committed; p50/p95 and RPS recorded in `docs/TESTING.md` → `perf/redirect-load.js`, `docs/TESTING.md` "Performance"
- Cache added only with a measured improvement and an invalidation test → not added; reasoning below and in TESTING.md
- Existing 86 tests green → 47 unit + 39 integration below

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #18 per the procedure (k6 script with `setup()` creating one link and a 302 + `Cache-Control` check; run the Release API against a throwaway compose SQL Server; capture RPS, p50/p95/p99, error rate, dropped-click warnings and the `Clicks` count; decide; document in TESTING.md and this log).
2. Reviewer (advisor model) before the run: flagged EF Core command logging at `Information` as a possible confound (measure with it on and off), suggested a `/health/live` floor, counting clicks through the stats endpoint as well as `SELECT COUNT(*)`, and pinning the k6 image.
3. Reviewer after the first run: pointed out that 20 looping VUs is a saturation test whose latency is queueing (Little's law), and asked for unloaded 1-VU runs, a repeat, and a CPU split before deciding.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `perf/redirect-load.js` | generated | 20 VUs × 30 s, `redirects: 0`, checks 302 + `private, no-store`, requests tagged `name:redirect`; an empty threshold list prints the redirect-only sub-metric without enforcing anything; `setup()` logs the code so clicks can be checked via the stats endpoint |
| `perf/README.md` | generated | Exact command (`grafana/k6:2.3.0`, stdin form), `BASE_URL` / `host.docker.internal`, the rate-limit caveat, the clicks check |
| `docs/TESTING.md` | edited | "Performance": machine, profile, numbers, reading, decision, what would change it, limits |
| `CLAUDE.md` §9 | edited | Dotted configuration keys cannot be `export`ed in bash (hit while running the logging-off variant) |
| scratchpad `perf-run.sh`, `perf-run2.sh`, `live-floor.js` | generated, not committed | Orchestration (compose up, app start, k6 runs, click counts, teardown) and the `/health/live` floor script. Throwaway, so not in the repo |
| (proposal) `IMemoryCache` L1 on the redirect | rejected (not justified) | See Decision |
| (proposal) HybridCache package | rejected | No cache; and for one replica `IMemoryCache` from the shared framework would be enough |
| (proposal) lower EF Core logging in `appsettings.json` | not done (out of scope) | Measured to make no latency difference; raised as a question for the engineer (log volume on Azure) |
| (proposal) a CLAUDE.md gotcha that killing `dotnet run` orphans the app | rejected | Not verified in this task (the scripts stopped the PID owning port 5078) |

## Password handling
The SA password was generated inside each orchestration script (`"Aa1!" + 24 random Base62 characters` from `/dev/urandom`), exported only in that process's environment for `docker compose` and in `ConnectionStrings__UrlShortener` for the API, and used in `sqlcmd` through the container's own `$MSSQL_SA_PASSWORD` (single-quoted, so it was never on the host command line). It was never printed, logged or written to a file, and the volume was removed with `down -v` at the end of each script.

## Checks run

Pre-flight (no containers, ports free):
```
$ docker ps
CONTAINER ID   IMAGE     COMMAND   CREATED   STATUS    PORTS     NAMES
$ netstat -ano | grep -E ":1433 |:5078 "
(no output)
$ docker run --rm grafana/k6 version
k6 v2.3.0 (commit/e088784614, go1.27.1, linux/amd64)
```

Pass 1 (`perf-run.sh`): connectivity, then "as shipped" and "EF Core logging at `Warning`" (`env 'Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning' dotnet run ...`), each with 5 s warm-ups:
```
== compose up
 Container urlshortener-perf-sqlserver-1 Healthy
== connectivity check
app ready
    checks_succeeded...: 100.00% 1 out of 1
    http_req_failed................: 0.00% 0 out of 1
== as-shipped
clicks before=3270 after=37918 delta=34648 code=UR9q8hI
stats: {"code":"UR9q8hI","totalClicks":34648,...
app warnings:
warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]
EF command log lines: 39134
== ef-warning
clicks before=40859 after=71453 delta=30594 code=l5SrGh9
stats: {"code":"l5SrGh9","totalClicks":30594,...
app warnings:
warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]
EF command log lines: 0
== compose down
 Volume urlshortener-perf_sqlserver-data Removed
 Network urlshortener-perf_default Removed
```
The only warning is the HTTPS redirection middleware's "failed to determine the https port" (http-only profile): no redirect happened, every response was the expected 302. `before` is read after the warm-ups and `after` after the measured redirect run and the `/health/live` floor run (which records no clicks), each once the count stopped changing, so `delta` is the measured run's redirects only.

k6, redirect as shipped (run 1):
```
    checks_succeeded...: 100.00% 69296 out of 69296
    ✓ status is 302
    ✓ cache-control is private, no-store
    http_req_duration..............: avg=17.07ms min=4.56ms med=14.68ms p(90)=24.46ms p(95)=31.61ms p(99)=65.23ms max=222.51ms
      { name:redirect }............: avg=17.07ms min=4.56ms med=14.68ms p(90)=24.46ms p(95)=31.61ms p(99)=65.23ms max=222.51ms
    http_req_failed................: 0.00%  0 out of 34649
    http_reqs......................: 34649  1142.546599/s
    iterations.....................: 34648  1142.513624/s
```
k6, redirect with EF Core logging at `Warning`:
```
    checks_succeeded...: 100.00% 61188 out of 61188
      { name:redirect }............: avg=19.32ms min=4.88ms med=17ms    p(90)=30.6ms  p(95)=36.17ms p(99)=52.49ms max=156.25ms
    http_req_failed................: 0.00%  0 out of 30595
    http_reqs......................: 30595  1013.648039/s
    iterations.....................: 30594  1013.614908/s
```
k6, `/health/live` floor, EF Core logging at `Warning`:
```
    http_req_duration..............: avg=4.76ms min=851.11µs med=4.26ms p(90)=7.08ms p(95)=8.37ms p(99)=12.52ms max=126.55ms
    http_req_failed................: 0.00%  0 out of 121541
    http_reqs......................: 121541 4034.20789/s
```
The as-shipped floor run (1,506 rps, p50 8.79 ms) is discarded: it started right after the redirect run while the console logger was still writing its backlog of 39,134 command lines.

Pass 2 (`perf-run2.sh`, as shipped, fresh database): 1-VU runs, a repeat of the 20-VU run with a CPU sample 15 s in, then the floor after a 10 s pause:
```
== compose up
 Container urlshortener-perf-sqlserver-1 Healthy
app ready
# /health/live, 1 VU, 15 s
    http_req_duration..............: avg=1.13ms min=739.64µs med=1.04ms p(90)=1.37ms p(95)=1.56ms p(99)=2.11ms max=86.4ms
    http_req_failed................: 0.00%  0 out of 11881
    http_reqs......................: 11881  787.571986/s
# redirect, 1 VU, 15 s
      { name:redirect }............: avg=4.89ms min=3.13ms med=4.54ms p(90)=6.09ms p(95)=6.91ms p(99)=10.98ms max=42.75ms
    http_req_failed................: 0.00%  0 out of 2920
    iterations.....................: 2919   194.281803/s
# redirect, 20 VUs, 30 s (run 2)
    checks_succeeded...: 100.00% 65144 out of 65144
      { name:redirect }............: avg=18.13ms min=4.85ms med=16.29ms p(90)=26.62ms p(95)=31.12ms p(99)=46.54ms max=211.16ms
    http_req_failed................: 0.00%  0 out of 32573
    iterations.....................: 32572  1080.33399/s
# /health/live, 20 VUs, 30 s
    http_req_duration..............: avg=6ms    min=1.05ms med=4.86ms p(90)=9.31ms p(95)=12.31ms p(99)=24.4ms  max=97.03ms
    http_req_failed................: 0.00%  0 out of 96966
    http_reqs......................: 96966  3223.31672/s
sqlserver container CPU=164.76% MEM=833.9MiB / 15.51GiB
app process CPU=167% of one core
clicks before=6622 after=39194 delta=32572
dropped/discarded warnings: 0
== compose down
 Volume urlshortener-perf_sqlserver-data Removed
 Network urlshortener-perf_default Removed
```
Clicks stored equal redirects served in every measured run (34,648, 30,594, 32,572); no clicks dropped, no batch discarded.

Gates:
```
$ dotnet build -c Release --nologo -v q
    0 Warning(s)
    0 Error(s)
$ dotnet format --verify-no-changes --no-restore
format exit 0
$ dotnet test -c Release --nologo
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47 - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    39, Skipped:     0, Total:    39 - UrlShortener.IntegrationTests.dll (net10.0)
$ docker ps -a --filter name=urlshortener-perf
CONTAINER ID   IMAGE     COMMAND   CREATED   STATUS    PORTS     NAMES
$ docker volume ls --filter name=urlshortener-perf
DRIVER    VOLUME NAME
```

## Decision
**No cache.** Unloaded, a redirect is 4.54 ms p50 / 6.91 ms p95 against a 1.04 ms floor, so the lookup costs about 3.5 ms. At saturation the lookup does take about three quarters of the capacity (about 1,100 vs 3,200 to 4,000 rps), but 1,100 redirects/s on a shared laptop is far above what a single-replica prototype will serve, and the test's single hot code is the cache's best case, not a known traffic shape. A cache would add staleness to handle on delete, on Day 3 expiry and across replicas, for headroom nobody needs yet. Revisit with B6's measured Azure SQL round trip from Container Apps, a second replica, or real traffic with hot-code skew; the cache shape is written down in TESTING.md.

## Decisions the engineer should question
- **The decision rule.** The task's own rule ("p95 single-digit and the DB not the bottleneck") is met only for unloaded latency; under saturation p95 is 31 to 36 ms and the DB path is the main cost. I read "do we need a cache" as "is the redirect slow for the load we expect", not "could it be faster at saturation".
- **Every SQL command is logged at `Information` in every environment** (`Default: Information` in `appsettings.json`; 39,134 lines in one 30 s run). It made no measurable latency difference here, but on Container Apps it is Log Analytics volume and cost. A `Microsoft.EntityFrameworkCore.Database.Command: Warning` entry is a one-line change, tracked as issue #25.
- **Co-located generator.** k6, the API and SQL Server shared 8 threads; absolute numbers are machine numbers. The relative ones (lookup vs floor) are what the decision uses.
- **Short runs, one repeat.** Run-to-run variation was about 10 % in throughput; the logging on/off difference was inside it.
- **ADR-0004's assumed 1,000 redirects/s** is now a measured about 1,000 to 1,150 on this machine, with the writer keeping up and no drops.

## Gotchas encountered
- Dotted configuration keys (`Logging__LogLevel__Microsoft.EntityFrameworkCore`) cannot be `export`ed in bash; added to CLAUDE.md §9 with the `env` form.
- `host.docker.internal` reached the API on `localhost:5078` from the k6 container without changes on Docker Desktop for Windows (documented in `perf/README.md`, not a gotcha).

## Coordinator review
Fable 5.1 (coordinator session), after the subagent's hand-back.

| Checked | Result |
|---|---|
| Diff scope | Docs and `perf/` only: `CLAUDE.md` +1 gotcha, `docs/TESTING.md` Performance section, `perf/redirect-load.js`, `perf/README.md`, this log. No source, test or package change. |
| k6 script | `redirects: 0` so the 302 is measured, not followed; redirect requests tagged for their own sub-metric; setup creates one link (one POST, under the create limit). Read and accepted as written. |
| Decision | Accepted: no cache. The rule in the issue ("p95 single-digit and the DB not the bottleneck") is met unloaded and not at saturation; the log says so instead of hiding it, and the revisit triggers (B6 Azure SQL round trip, second replica, hot-code skew) are concrete. |
| Finding to carry forward | Every SQL command logged at `Information`: opened as issue #25 (not fixed here, out of scope). |
| Gates re-run by the coordinator | `dotnet build -c Release`: 0 warnings, 0 errors. `dotnet format --verify-no-changes`: exit 0. Tests not re-run: no code changed, and `main` at 94da136 was verified at 47 unit + 39 integration before this branch was created. |
| Containers | `docker ps` / `docker volume ls` show nothing left from the runs (subagent output above). |

## Sign-off
Reviewed and merged by the engineer in PR #26; I understood the change and accept ownership of it. — Mohammed Saalim, sign-off recorded 2026-10-11
