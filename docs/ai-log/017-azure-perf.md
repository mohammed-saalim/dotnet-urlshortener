# AI log 017 — azure-perf

| Field | Value |
|---|---|
| Issue | none (small follow-up; the engineer waived issues for small items on 2026-10-11) |
| Branch | `docs/azure-perf` |
| Date | 2026-10-11 |
| Tool / model | Claude Code / `claude-opus-5-5` (coordinator) |
| Risk level | low (measurement and docs only) |

## Intent
Check B5's first cache trigger: the Azure SQL round trip from Container Apps.

## Constraints given to the AI
No saturation test against the single small replica; reuse `perf/redirect-load.js`; keep the create rate limit in mind.

## Method
1. Warm-up `GET /health/ready`.
2. k6 2.3.0 (Docker, from the laptop), `perf/redirect-load.js`, `--vus 1 --duration 30s`.
3. A throwaway `/health/live` script, 1 VU for 30 s, as the floor (the same as B5's `live-floor.js`; not committed).
4. Application Insights (`requests`, `dependencies`) for the run window 15:06:30–15:07:10 UTC, queried through the App Insights query API.
5. Container `UsageNanoCores` / `CpuPercentage` through ARM metrics.

## Output
```
warm-up GET /health/ready -> 200 in 22.130908 s        (database resuming from auto-pause)

# redirect, 1 VU, 30 s
http_req_duration{name:redirect}: avg=80.09ms p(50)=94.43ms p(95)=105.53ms p(99)=201.84ms
http_req_failed 0.00% (0 of 386); http_reqs 386 (11.81/s); iterations 385

# /health/live, 1 VU, 30 s
http_req_duration: avg=24.96ms p(50)=22.02ms p(95)=34.69ms p(99)=70.18ms
http_req_failed 0.00% (0 of 1228); http_reqs 1228 (39.67/s)

# server side, requests in the window
POST /api/links/                         n=1    1613.9 ms   (k6 setup; first create after resume)
GET /{code:regex(...)}                   n=385  p50 6.24  p95 80.81  p99 85.79 ms
# SQL dependencies inside those redirects
SQL                                      n=385  p50 1.94  p95 76.99  p99 81.26 ms
# redirect duration buckets
<10 ms: 203 | 10-40: 7 | 40-70: 44 | 70-100: 128 | >=100: 3
# SQL by bucket
in request:  <10 ms n=249 p50 1.58 | 10-70 n=48 p50 65.19 | >=70 n=89 p50 75.51
click writer: <10 ms n=364 p50 4.35 | 10-70 n=14 p50 16.27 | >=70 n=6 p50 91.73
# first 30 redirects (ms): 1077.7, 81.5, 4.4, 76.3, 3.8, 3.5, 3.7, 4.3, 41.4, 9.3, 12, 3.2, 4.7, 8.8, 3.4, 3.4,
#   21.6, 2.9, 3.7, 18.9, 8, 3.3, 4.4, 74.5, 3.2, 2.7, 3.2, 83.5, 82.2, 78.4   (slow ones about 100 ms apart)

# container CPU (1-minute buckets; the bucket labels lag the run)
UsageNanoCores max 230,415,613 (0.23 cores) avg 166,927,343 | CpuPercentage max 90 avg 64.5 | idle: max 3
container resources: cpu 0.25, memory 0.5Gi
```

## Reading
- Azure SQL round trip in-region: about 1.6–1.9 ms at p50. Not the cost.
- Tail: bimodal, slow requests about 100 ms apart with about 75 ms stalls, and CPU at 90 % of the 0.25 vCPU quota. Consistent with CFS quota throttling (25 ms per 100 ms period). An inference; throttle counters (`cpu.stat`) not read.
- Decision: no cache (TESTING.md, "On Azure"). Full sampling (#33) adds export work per request; at this traffic it is part of the CPU the quota covers, not measured separately.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `docs/TESTING.md` "On Azure" | generated | Table, reading, decision, next triggers. |
| `docs/HANDOFF.md` | edited | Step 2 done. |
| scratchpad `live-floor.js` | generated, not committed | Throwaway floor script, as in B5. |
| (proposal) raise to 0.5 vCPU | rejected for now | Doubles the per-second vCPU rate beyond the free grant for a tail that only shows under a closed-loop client; recorded as the lever if it matters. |

## Sign-off
Reviewed, understood, accept ownership. — <name>, YYYY-MM-DD
