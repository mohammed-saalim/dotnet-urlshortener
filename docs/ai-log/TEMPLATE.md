# AI log NNN — <task slug>

| Field | Value |
|---|---|
| Issue | #NN |
| Branch | `feat/NN-slug` |
| Date | YYYY-MM-DD |
| Tool / model | Claude Code / `<model id>` |
| Risk level | low / high-impact |

## Intent
<!-- From the issue, in one sentence. -->

## Constraints given to the AI
<!-- e.g. no new packages; TDD for X; match existing style; no schema changes -->

## Acceptance criteria (runnable)
<!-- Copy from the issue. Each must map to a check below. -->

## Prompts (summarised, secrets redacted)
1.
2.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `path/to/file.cs` | generated | |
| `path/to/other.cs` | edited | what the engineer changed and why |
| (proposal) | rejected | why it was turned down |

## Checks run
<!-- Commands, exit codes, counts. Paste real output. -->
```
$ dotnet test
Passed! - Failed: 0, Passed: N, Skipped: 0
```

## Gotchas encountered
<!-- Anything added to CLAUDE.md §9 in this task -->

## Sign-off
Reviewed the full diff, understood it, accept ownership. — <name>, YYYY-MM-DD
