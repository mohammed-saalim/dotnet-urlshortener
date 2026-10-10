## Closes
<!-- #issue -->

## What changed
<!-- Short, factual. -->

## AI log
<!-- docs/ai-log/NNN-slug.md — every artifact marked generated / edited / rejected with rationale -->

## Checks run (paste output, not claims)
```
dotnet build
dotnet test
dotnet format --verify-no-changes
```

## Risk level
- [ ] low
- [ ] high-impact (schema / URL validation / rate-limit policy / infra / agent settings)

## High-impact only
- [ ] `/security-review` run; findings addressed or explicitly accepted below
- [ ] Fresh-context `/code-review` run
- [ ] **Engineer sign-off:** I reviewed the full diff, understand it, and accept ownership. — <!-- name, date -->

## Notes for the interview
<!-- Trade-offs taken, alternatives rejected, anything a reviewer should question. -->

## Walkthrough (read this before the diff)
### Files, in reading order
1. `path` — what it does and why it exists
2.

### See it working locally
```
gh pr checkout N
docker compose up -d
dotnet run --project src/UrlShortener.Api
curl ...
```
