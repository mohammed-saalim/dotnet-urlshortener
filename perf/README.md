# Performance scripts

`redirect-load.js` is the k6 load test for the redirect path (issue #18). Results and the cache decision are in [`docs/TESTING.md`](../docs/TESTING.md#performance).

## Run

Start SQL Server and the API as in [`docs/SETUP.md`](../docs/SETUP.md), but build in Release so the numbers are not Debug numbers:

```bash
dotnet build -c Release
dotnet run -c Release --no-build --project src/UrlShortener.Api   # http://localhost:5078, Development (startup migration)
```

Then, from the repo root in a second terminal (k6 runs from Docker; nothing to install):

```bash
docker run --rm -i grafana/k6:2.3.0 run - < perf/redirect-load.js
```

- **Target:** `BASE_URL`, default `http://host.docker.internal:5078`. From a Docker Desktop container that name reaches the host's `localhost`; it worked unchanged on Docker Desktop for Windows with Kestrel bound to `localhost` only. Elsewhere (Linux engine without Docker Desktop) pass `--add-host=host.docker.internal:host-gateway` or `-e BASE_URL=http://<host>:5078`.
- **Profile:** 20 looping VUs for 30 s, no think time (a closed-loop saturation test: latency at that load is mostly queueing, so read requests/s together with the percentiles). Override on the command line, e.g. `run --vus 1 --duration 15s -` for unloaded latency.
- **Thresholds are printed, not enforced:** the empty threshold list only makes k6 show the redirect-only sub-metric (`{ name:redirect }`, setup's POST excluded). The run never fails on latency.
- **Rate limit:** `setup()` makes one `POST /api/links`, well under the create limit (10 per 60 s per client IP by default, `RateLimiting__PermitLimit`). Many runs in quick succession (for example a warm-up plus a measured run, more than 10 a minute) would hit it, and `setup()` fails with `create returned 429`.
- **Clicks:** `setup()` logs `code=<code>`; after the run `GET /api/links/<code>/stats` should report `totalClicks` equal to k6's `iterations` (clicks are written asynchronously, so give the writer a second).
- The stdin form (`run - < file`) avoids a `-v` volume mount, whose Windows path Git Bash would rewrite.
