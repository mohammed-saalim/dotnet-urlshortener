// Redirect load test (issue #18). Run with the grafana/k6 Docker image; see perf/README.md.
import http from 'k6/http';
import { check, fail } from 'k6';

// From inside a Docker Desktop container the host's localhost is host.docker.internal.
const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5078';

export const options = {
  vus: 20,
  duration: '30s',
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  // Reported, not enforced: an empty threshold list makes k6 print the redirect-only sub-metric (setup's POST
  // excluded) without failing the run.
  thresholds: {
    'http_req_duration{name:redirect}': [],
  },
};

// One link for the whole run: a single hot code, the case an in-process cache would help most.
export function setup() {
  const res = http.post(`${BASE_URL}/api/links`, JSON.stringify({ url: 'https://example.com/k6-target' }), {
    headers: { 'Content-Type': 'application/json' },
  });
  if (res.status !== 201) {
    fail(`create returned ${res.status}, expected 201`);
  }
  const code = res.json('code');
  console.log(`code=${code}`); // so clicks can be checked afterwards via GET /api/links/{code}/stats
  return { code };
}

export default function (data) {
  const res = http.get(`${BASE_URL}/${data.code}`, { redirects: 0, tags: { name: 'redirect' } });
  check(res, {
    'status is 302': (r) => r.status === 302,
    'cache-control is private, no-store': (r) => r.headers['Cache-Control'] === 'private, no-store',
  });
}
