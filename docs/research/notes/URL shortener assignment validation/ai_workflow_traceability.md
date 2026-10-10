# AI-Assisted Engineering Workflow and Traceability (URL shortener, 2-3 day assignment)

All sources observed 2026-10-10 unless noted. Labels: VERIFIED / PARTIALLY VERIFIED / REFUTED / UNVERIFIED.

## 1. Anthropic Claude Code best practices (explore -> plan -> implement -> commit; runnable checks; hooks vs CLAUDE.md; security-reviewer subagent)

### Takeaway
All four prior claims are VERIFIED, quoted from the official best-practices page. The current page adds stronger patterns the earlier pass missed: Stop-hook gating, `/goal`, an adversarial fresh-context review subagent, "show evidence rather than asserting success", and writing a spec to SPEC.md after an interview.

### Cited Findings
- VERIFIED: four-phase workflow. "The recommended workflow has four phases": Explore (plan mode, `Shift+Tab` or `claude --permission-mode plan`), Plan ("Create a plan"; `Ctrl+G` to edit the plan), Implement ("write tests for the callback handler, run the test suite and fix any failures"), Commit ("commit with a descriptive message and open a PR"). Caveat quoted: "If you could describe the diff in one sentence, skip the plan." — [Claude Code best practices](https://code.claude.com/docs/en/best-practices)
- VERIFIED: runnable check. "Give Claude a check it can run: tests, a build, a screenshot to compare." and "Without a check it can run, 'looks done' is the only signal available, and you become the verification loop." — [best practices](https://code.claude.com/docs/en/best-practices)
- NEW: the page lists four gate strengths: in one prompt; across a session via a `/goal` condition; "As a deterministic gate: a Stop hook runs your check as a script and blocks the turn from ending until it passes"; "By a second opinion: a verification subagent... so the agent doing the work isn't the one grading it." — [best practices](https://code.claude.com/docs/en/best-practices)
- NEW: "Have Claude show evidence rather than asserting success: the test output, the command it ran and what it returned, or a screenshot of the result." This maps directly onto a "checks run" field in a per-task log. — [best practices](https://code.claude.com/docs/en/best-practices)
- VERIFIED: hooks vs CLAUDE.md. "Use hooks for actions that must happen every time with zero exceptions." / "Unlike CLAUDE.md instructions which are advisory, hooks are deterministic and guarantee the action happens." Examples: "Write a hook that runs eslint after every file edit", "Write a hook that blocks writes to the migrations folder." — [best practices](https://code.claude.com/docs/en/best-practices). The hooks reference says `PreToolUse` runs "Before a tool call executes. Can block it" — [Hooks reference](https://code.claude.com/docs/en/hooks)
- VERIFIED: security-reviewer subagent. The page gives `.claude/agents/security-reviewer.md` with frontmatter `name: security-reviewer`, `tools: Read, Grep, Glob, Bash`, `model: opus`, and a body covering injection, authn/authz flaws, "Secrets or credentials in code", and insecure data handling, "Provide specific line references and suggested fixes." — [best practices](https://code.claude.com/docs/en/best-practices)
- NEW: "Add an adversarial review step: Before treating a task as done, have a subagent review the diff in a fresh context and report gaps." Sample prompt: "review the rate limiter diff against PLAN.md. Check that every requirement is implemented, the listed edge cases have tests, and nothing outside the task's scope changed." It warns that reviewers will always find something, so "flag only gaps that affect correctness or the stated requirements". The bundled `/code-review` skill is mentioned. — [best practices](https://code.claude.com/docs/en/best-practices)
- NEW: Writer/Reviewer pattern across two sessions: "A fresh context improves code review since Claude won't be biased toward code it just wrote." — [best practices](https://code.claude.com/docs/en/best-practices)
- NEW: interview-then-spec. "Keep interviewing until we've covered everything, then write a complete spec to SPEC.md... start a fresh session to execute it." Useful specs "name the files and interfaces involved, state what is out of scope, and end with an end-to-end verification step". — [best practices](https://code.claude.com/docs/en/best-practices)
- NEW: CLAUDE.md hygiene. "For each line, ask: 'Would removing this cause Claude to make mistakes?'" and "Bloated CLAUDE.md files cause Claude to ignore your actual instructions!" Its failure-pattern fix: "If Claude already does something correctly without the instruction, delete it or convert it to a hook." — [best practices](https://code.claude.com/docs/en/best-practices)
- NEW: Claude Code has a built-in `/security-review` command ("run an on-demand security pass over the changes on your current branch") and a "Security guidance plugin". — [Claude Code security](https://code.claude.com/docs/en/security)
- NEW: commit attribution can be configured. Settings `attribution.commit` ("Change or hide the trailer Claude Code adds to commits") and `attribution.pr`; `includeCoAuthoredBy` is "Deprecated; use `attribution`". — [Settings reference](https://code.claude.com/docs/en/settings-reference)

### Inferences
- For the assignment, the strongest evidence of discipline is a gate the reviewer can see in the repo: a checked-in `.claude/settings.json` with a PostToolUse or Stop hook that runs `dotnet build` and `dotnet test`, plus deny rules. A sentence in CLAUDE.md is weaker, because the docs themselves call CLAUDE.md advisory.
- A "high-impact change" sign-off can use plan mode (the human approves the plan) plus a fresh-context review subagent before the commit. The human writes the final approval line, not the AI.
- The page was observed with no visible "last updated" date. It references Claude Code v2.1.283 (auto mode as the default starting permission mode), so it is current-version content.

### Gaps
- No explicit page date. The older anthropic.com/engineering "Claude Code best practices" blog post was not re-fetched; the docs page appears to be its successor.

## 2. GitHub Spec Kit

### Takeaway
VERIFIED: MIT license, very active (v1.1.3 released 2026-10-09, ~140.7k stars). The phases have changed since the earlier pass: the core loop is now constitution -> specify -> plan -> tasks -> implement -> converge, with clarify, checklist and analyze as optional add-ons, plus new bug and idea-assessment workflows. For a 2-3 day task, borrow its artifact shapes rather than adopt the tooling.

### Cited Findings
- VERIFIED: MIT, 140,715 stars, last push 2026-10-10T01:32Z, latest release `v1.1.3` published 2026-10-09T19:30Z — [GitHub API repo](https://api.github.com/repos/github/spec-kit), [latest release](https://api.github.com/repos/github/spec-kit/releases/latest)
- VERIFIED (with an update): README says "Constitution once per project; specify → plan → tasks → implement → converge per feature." Commands: `/speckit-constitution`, `/speckit-specify`, `/speckit-plan`, `/speckit-tasks`, `/speckit-implement`, `/speckit-converge`, and "Repeat implement → converge until convergence reports Converged." "Add clarification, checklists, and consistency analysis when you need extra..." (optional). — [spec-kit README](https://github.com/github/spec-kit)
- PARTIALLY REFUTED (prior naming): commands now use hyphens (`/speckit-specify`), not the older dotted `/speckit.specify` form, and `converge` is a new core phase. A prior list that stopped at "implement" is out of date. — [spec-kit README](https://github.com/github/spec-kit)
- NEW workflows: bug fixing (`/speckit-bug-assess`, `/speckit-bug-fix`, `/speckit-bug-test`) and idea assessment (`/speckit-assess-intake`, `-research`, `-define`, `-shape`, `-decide`), which ends in "go, clarify, or stop", and "A go decision can be handed to /speckit-specify". — [spec-kit README](https://github.com/github/spec-kit)
- VERIFIED: greenfield and brownfield. `specify init my-project --integration copilot` for new projects, plus an "existing-project guide" — [README](https://github.com/github/spec-kit), [existing projects guide](https://github.github.io/spec-kit/guides/existing-projects.html). Install is `uv tool install specify-cli`; requires Python 3.11+ and uv; creates `.specify/` (memory, bugs, assessments) — [README](https://github.com/github/spec-kit)

### Inferences
- Scenario mapping: greenfield maps to constitution + specify + plan + tasks; brownfield maps to the existing-project guide and the bug-* workflow; an ambiguous requirement maps to clarify or the assess-* "go/clarify/stop" flow.
- For a 2-3 day .NET take-home, installing a Python/uv toolchain and its generated scaffolding adds review noise. The better move is to copy the shapes by hand: `constitution.md` (principles and quality gates), `spec.md` per feature (user stories, acceptance criteria, out of scope), `plan.md`, and `tasks.md` (checklist that links to log entries). Then mention Spec Kit as the inspiration. This matches Anthropic's own SPEC.md/PLAN.md pattern.

### Gaps
- I did not read the existing-projects guide or the template file contents in full. Reported stars come from the API at the time observed.

## 3. Commit-trailer and AI disclosure conventions

### Takeaway
The Linux kernel and Fedora both use `Assisted-by:`. The kernel says AI agents MUST NOT add `Signed-off-by`. The kernel format changed in August 2026 (docs-7.3): the model name was removed, so the current format is `Assisted-by: LLM [TOOL1] [TOOL2]`. LLVM also recommends `Assisted-by:`. QEMU and Gentoo ban AI-generated content. Git rejects anything that looks AI-generated. curl and Ghostty require disclosure.

### Cited Findings
- VERIFIED (kernel, current master): "AI agents MUST NOT add Signed-off-by tags. Only humans can legally certify the Developer Certificate of Origin (DCO)." The human must review all AI-generated code, ensure licensing, add their own Signed-off-by, and take "full responsibility". Format: `Assisted-by: LLM [TOOL1] [TOOL2]`, where the tools are optional specialized analysis tools (coccinelle, sparse, smatch, clang-tidy); "Basic development tools (git, gcc, make, editors) should not be listed." Example: `Assisted-by: LLM coccinelle sparse` — [kernel coding-assistants.rst (raw master)](https://raw.githubusercontent.com/torvalds/linux/master/Documentation/process/coding-assistants.rst), [rendered](https://docs.kernel.org/process/coding-assistants.html)
- VERIFIED (change history): the docs-7.3 merge (2026-08-20) includes "A (hopefully) consensus change to our LLM-attribution requirements, removing the specific model name from the Assisted-by tag", together with "coding-assistants: simplify attribution" and "checkpatch.pl: adapt to new Assisted-by: format". A prior claim of a format like `Assisted-by: AGENT_NAME:MODEL_VERSION` is therefore OUTDATED (REFUTED for current kernel). — [GitHub commits API for the file](https://api.github.com/repos/torvalds/linux/commits?path=Documentation/process/coding-assistants.rst)
- NEW (kernel): a companion `generated-content.rst` covers tool-generated content in general. Its scope: "when a meaningful amount of content... was not written by a person in the Signed-off-by chain". Trivial tooling such as spelling fixes, identifier completion and clang-format is out of scope. "If in doubt, choose transparency". A section added in August 2026 makes AI bug-finding follow mandatory steps: note the commit ID, build a reproducer, write the fix, then build and verify. — [generated-content.rst](https://raw.githubusercontent.com/torvalds/linux/master/Documentation/process/generated-content.rst), [coding-assistants.rst](https://raw.githubusercontent.com/torvalds/linux/master/Documentation/process/coding-assistants.rst)
- PARTIALLY VERIFIED (Fedora): docs.fedoraproject.org and forge.fedoraproject.org are behind an Anubis bot wall, so I could not read the final text directly. The proposal text on Fedora Discussion says: "The contributor is always the author and is fully accountable"; "use a commit message trailer like Assisted-by: <name of code assistant>"; reviewers "may use AI tools to assist... but not to wholly automate the review process... AI should not make the final determination on whether a contribution is accepted"; and AI "must not be used to score or evaluate submissions" for CoC, funding, talks or leadership. — [Fedora Discussion proposal (raw post 1)](https://discussion.fedoraproject.org/t/council-policy-proposal-policy-on-ai-assisted-contributions/165092). Search snippets of the published policy say disclosure is required "when the significant part of the contribution is taken from a tool without changes" and give examples "Assisted-by: generic LLM chatbot" / "Assisted-by: ChatGPTv5" — [Fedora policy page](https://docs.fedoraproject.org/bn/council/policy/ai-contribution-policy/) (snippet only); adopted October 2025 per [GamingOnLinux](https://gamingonlinux.com/2025/10/fedora-linux-project-agrees-to-allow-ai-assisted-contributions-with-a-new-policy)
- UNVERIFIED: whether Fedora's text says anything about Signed-off-by. I found no such statement; Fedora generally does not use the DCO the way the kernel does.
- VERIFIED (LLVM): "Contributors must read and review all LLM-generated code or text before they ask other project members to review it". The policy recommends "commit message trailers like 'Assisted-by:'" and bans "extractive contributions", where review effort exceeds the value to the project. AI is forbidden for "good first issue" problems, and automated agents acting without human approval (e.g. `@claude` agent) are banned. Footer date was reported as 2026-10-10, possibly the build date. — [LLVM AI Tool Use Policy](https://llvm.org/docs/AIToolPolicy.html)
- VERIFIED (QEMU): "Current QEMU project policy is to DECLINE any contributions which are believed to include or derive from AI generated content". It names Copilot, ChatGPT, Claude and Code Llama. Use for research, static analysis or debugging is allowed if the output is not included. — [QEMU code provenance](https://www.qemu.org/docs/master/devel/code-provenance.html)
- PARTIALLY VERIFIED (Gentoo): the Council voted on 2024-04-14 to forbid content created with the assistance of NLP AI tools, citing copyright, quality and ethics. This is from secondary coverage; the primary wiki was not fetched. — [Gentoo wiki Council/AI policy](https://wiki.gentoo.org/wiki/Project:Council/AI_policy), [LWN](https://lwn.net/Articles/970072/)
- VERIFIED (Git): "we will reject anything that looks AI generated, that sounds overly formal or bloated, that looks like AI slop... or that senders don't understand or cannot explain". The stated reason is that it is unclear whether the DCO can be satisfied for significant AI-generated content. Using AI for guidance, debugging and self-review is encouraged. No trailer is defined. — [git SubmittingPatches](https://raw.githubusercontent.com/git/git/master/Documentation/SubmittingPatches)
- VERIFIED (curl): for security reports, "you **must** make sure to reveal this fact in your report" and "We ban users immediately who submit made up fake reports". For PRs: "We can accept code written with the help of AI... but the code must still follow coding standards, be written clearly, be documented, feature test cases". Rule of thumb: "if someone can spot that the contribution was made with the help of AI, you have more work to do." No trailer format. — [curl CONTRIBUTE.md](https://raw.githubusercontent.com/curl/curl/master/docs/CONTRIBUTE.md)
- NEW (Ghostty): "All AI usage in any form must be disclosed. You must state the tool you used (e.g. Claude Code, Cursor, Amp) along with the extent that the work was AI-assisted." Also: "The human-in-the-loop must fully understand all code." — [Ghostty AI_POLICY.md](https://raw.githubusercontent.com/ghostty-org/ghostty/main/AI_POLICY.md)
- PARTIALLY VERIFIED (GitHub `Co-authored-by:`): GitHub's documented trailer is `Co-authored-by: NAME <EMAIL>`, which credits co-authors in the UI. Claude Code adds a `Co-Authored-By: Claude ... <noreply@anthropic.com>` trailer by default, configurable through the `attribution.commit` setting — [Settings reference](https://code.claude.com/docs/en/settings-reference). I did not re-fetch the GitHub doc page this pass — [GitHub docs: multiple authors](https://docs.github.com/en/pull-requests/committing-changes-to-your-project/creating-and-editing-commits/creating-a-commit-with-multiple-authors)

### Inferences
- Recommended commit convention for the assignment: a human-written commit with `Assisted-by: Claude Code (<model>)`, plus an optional `AI-Log: docs/ai-log/NNN.md` pointer (a custom trailer, invented here and not a standard). Keep `Signed-off-by`, if used, for the human only, following the kernel rule. Including the model name is fine for a take-home because traceability is the point, even though the kernel recently dropped it. Either set `attribution.commit` to match, or leave Claude's default `Co-Authored-By` and explain the choice. `Co-authored-by` implies authorship and accountability that an AI cannot hold (the Fedora and kernel principle), so `Assisted-by` is the more defensible choice.

### Gaps
- I could not read the final Fedora policy text because of the Anubis wall. The Gentoo primary page was not fetched.

## 4. Per-task AI log templates and standards

### Takeaway
I found no single industry standard for a per-task AI prompt log. The best basis is a composite: MADR 4.0's structure (context, drivers, options, outcome, "Confirmation"), the Claude Code guidance to show evidence, and disclosure rules (tool + extent, as in Ghostty and Fedora). NIST AI RMF and OWASP give governance principles, not templates.

### Cited Findings
- VERIFIED: MADR 4.0.0 (released 2024-09-17) template sections: optional front matter `status`, `date`, `decision-makers`, `consulted`, `informed`; then "Context and Problem Statement", "Decision Drivers", "Considered Options", "Decision Outcome", "Consequences", "Confirmation", "Pros and Cons of the Options", "More Information". — [MADR template](https://raw.githubusercontent.com/adr/madr/develop/template/adr-template.md), [MADR release](https://api.github.com/repos/adr/madr/releases/latest)
- VERIFIED: Anthropic recommends recording evidence: "the test output, the command it ran and what it returned". — [best practices](https://code.claude.com/docs/en/best-practices)
- VERIFIED: disclosure granularity in practice is "the tool you used... along with the extent that the work was AI-assisted" — [Ghostty](https://raw.githubusercontent.com/ghostty-org/ghostty/main/AI_POLICY.md); LLVM says transparency helps more than "tracking which specific sections were generated" — [LLVM](https://llvm.org/docs/AIToolPolicy.html)
- PARTIALLY VERIFIED: NIST AI 600-1 (Generative AI Profile of the AI RMF, July 2024) exists and covers GenAI risks such as confabulation and information security. I did not fetch it this pass (the URL returned HTML), so I have no exact clause to cite — [NIST AI 600-1](https://nvlpubs.nist.gov/nistpubs/ai/NIST.AI.600-1.pdf)
- UNVERIFIED: a standardized "AI usage log" or "prompt log" template in a major OSS repo. None was found in this pass.

### Inferences
- Proposed per-task log (`docs/ai-log/NNN-<slug>.md`, MADR-like): Task ID/link; Date; Tool + model; Intent; Constraints (incl. "no new packages without approval", "no secrets"); Acceptance criteria (runnable); Prompt(s) (verbatim or summarized, redacted); Outcome per artifact: Generated / Edited (what changed and why) / Rejected (rationale); Checks run (commands + exit codes/test counts); Risk level (low/high-impact); Human sign-off (name, date, "reviewed diff, understood, accept"). "Confirmation" in MADR maps to "Checks run".
- A PR template with checkboxes ("AI used? tool/extent", "I have reviewed and can explain every line", "New dependencies verified on nuget.org", "No secrets in prompts") is consistent with the Ghostty, LLVM and Fedora principles.

### Gaps
- No primary-source PR template with AI-disclosure checkboxes was captured.

## 5. Archify (github.com/tt-a1i/archify)

### Takeaway
VERIFIED: it is a real, MIT-licensed, very popular (81.6k stars) agent skill that renders JSON-IR diagrams into standalone interactive HTML. It ships Node scripts (`bin/archify.mjs` etc.) that the agent runs, and `finalize` performs a network update check by default. For a reviewer-facing, diffable repo, Mermaid in Markdown plus ADRs is the better source of truth. Archify is optional at most, for a polished extra.

### Cited Findings
- VERIFIED: description "Turn any idea, plan, or codebase into a beautiful interactive diagram. An agent skill for Claude Code, Codex, and more." MIT. 81,614 stars. Created 2026-04-15, last push 2026-10-10. Latest release v3.0.1 (2026-09-28). — [GitHub API](https://api.github.com/repos/tt-a1i/archify), [release](https://api.github.com/repos/tt-a1i/archify/releases/latest)
- VERIFIED: install is `npx skills add tt-a1i/archify -g`. It installs into `~/.claude/skills/` or the equivalent for other agents. `npx skills` itself downloads and runs the `skills` npm package (archify pins `skills: 1.7.0` as a devDependency). — [README](https://github.com/tt-a1i/archify), [package.json](https://raw.githubusercontent.com/tt-a1i/archify/main/archify/package.json)
- VERIFIED: the installed skill (`archify/`, ~345 files) contains executable Node scripts: `bin/archify.mjs`, `finalize.mjs`, `preview.mjs`, `visual-check.mjs`, `open-artifact.mjs`, `delivery-update.mjs`, plus `migrations/`, `recipes/`, `delta/`. SKILL.md tells the agent to run `node bin/archify.mjs finalize <type> <candidate.json> <output.html> ... --repo-root <repo-root>`. It requires Node >= 18 and writes outputs to `.archify/<type>-<slug>-<timestamp>/`. SKILL.md also says "No install is required inside the skill package" (no runtime deps). — [SKILL.md](https://raw.githubusercontent.com/tt-a1i/archify/main/archify/SKILL.md), [repo tree](https://api.github.com/repos/tt-a1i/archify/git/trees/main?recursive=1)
- VERIFIED: network. "`finalize` includes a bounded update check in its delivery receipt", which per the README makes GET requests for newer releases. Disable it with `ARCHIFY_UPDATE_CHECK_DISABLED=1`. It "never downloads updates automatically", and the core HTML output is self-contained/offline. — [README](https://github.com/tt-a1i/archify), [SKILL.md](https://raw.githubusercontent.com/tt-a1i/archify/main/archify/SKILL.md)
- VERIFIED: it accepts pasted Mermaid flowchart, sequence and state input and can "inspect repository evidence". It is based on Cocoon-AI/architecture-diagram-generator (MIT). — [SKILL.md](https://raw.githubusercontent.com/tt-a1i/archify/main/archify/SKILL.md)

### Inferences
- Safety: there is no evidence it is malicious, but it is third-party executable code that the agent runs with your permissions, plus a phone-home update check. The repo also includes `archify.zip` at the root, which makes auditing harder. Installing it is a supply-chain decision, which conflicts with the "secure AI usage" story unless you pin the version, read the scripts and disable the update check.
- Recommendation: make Mermaid in Markdown (in README and `docs/architecture.md`) plus MADR ADRs the source of truth, since they are text-diffable and rendered natively by GitHub. If you want a visual, generate an Archify HTML once from the Mermaid source, commit it as a derived artifact, and log that in the AI log. Do not make it a dependency.
- The 81.6k stars after ~6 months is unusually fast growth. That is not proof of anything, but stars are not a safety signal.

### Gaps
- I did not audit the full script contents for other network calls (e.g. `visual-check.mjs` may launch a headless browser).

## 6. Mermaid in Markdown + ADR folder as diffable source of truth

### Takeaway
VERIFIED: GitHub natively renders ```mermaid fenced blocks in Markdown files, issues, PRs, discussions and wikis. MADR 4.0.0 is the current template. adr-tools and log4brains are dormant (last pushes April and December 2024), so a plain `docs/adr/NNNN-title.md` folder with no tooling is the low-risk choice.

### Cited Findings
- VERIFIED: "Diagram rendering is available in GitHub Issues, GitHub Discussions, pull requests, wikis, and Markdown files", using a "fenced code block with the `mermaid` language identifier". Run `info` in a mermaid block to see GitHub's Mermaid version; third-party Mermaid plugins may cause errors. — [GitHub docs: creating diagrams](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/creating-diagrams)
- VERIFIED: MADR latest release 4.0.0 (2024-09-17) — [MADR releases](https://api.github.com/repos/adr/madr/releases/latest), [MADR template](https://raw.githubusercontent.com/adr/madr/develop/template/adr-template.md)
- VERIFIED: npryce/adr-tools has ~5.7k stars, last push 2024-04-25, not archived. thomvaill/log4brains has ~1.6k stars, last push 2024-12-17, not archived. — [adr-tools API](https://api.github.com/repos/npryce/adr-tools), [log4brains API](https://api.github.com/repos/thomvaill/log4brains)

### Inferences
- Both tools are low-activity. For a 2-3 day assignment, hand-written MADR files are enough. A short `docs/adr/README.md` index, or simply the numbered files, does the job without an extra toolchain.

### Gaps
- adr.github.io was not re-fetched this pass.

## 7. Secure AI usage (secrets, hallucinated dependencies, AI code review)

### Takeaway
Concrete, citable controls exist. Claude Code `Read` deny rules (e.g. `Read(./.env)`) block the file tools from reading secrets. Sandboxing and the permission modes limit blast radius. `/security-review` and a security-reviewer subagent give AI review, which must never be the final arbiter. OWASP LLM09:2025 explicitly covers hallucinated packages, and the USENIX Security 2025 study found that 19.7% of recommended packages did not exist.

### Cited Findings
- VERIFIED: "To block Claude's file tools from reading a file or directory, add a Read deny rule for its path, such as `Read(./.env)` or `Read(./secrets/**)`"; "If your project has a `.claudeignore` file, it has no effect, so move its entries into Read deny rules." On Windows, use `//c/**/.env` or `//**/.env`. — [Claude Code permissions](https://code.claude.com/docs/en/permissions)
- VERIFIED: sandboxing gives "filesystem and network isolation"; "You're responsible for reviewing proposed code and commands for safety before approval"; for untrusted content: "Avoid piping untrusted content directly to Claude", "Verify proposed changes to critical files"; MCP: "Anthropic... does not security-audit or manage any MCP server"; Team practice: "Share approved permission configurations through version control". — [Claude Code security](https://code.claude.com/docs/en/security)
- VERIFIED: OWASP LLM09:2025 Misinformation. "The model suggests insecure or non-existent code libraries, which can introduce vulnerabilities when integrated into software systems". Attackers register frequently hallucinated names. Mitigations include cross-checking with trusted sources, secure coding practices and training. — [OWASP GenAI LLM09:2025](https://genai.owasp.org/llmrisk/llm092025-misinformation/)
- PARTIALLY VERIFIED: Spracklen et al., USENIX Security 2025, tested 16 LLMs on 576k code samples: 19.7% of recommended packages did not exist (205,474 unique names); 43% of hallucinated names reappeared across repeated prompts; open-source models hallucinated 21.7% vs GPT-4 at 5.2%. These figures come from secondary summaries; the paper itself was not fetched. — [xygeni summary](https://xygeni.io/blog/slopsquatting-attack-prevention/), [nesbitt.io](https://nesbitt.io/2025/12/10/slopsquatting-meets-dependency-confusion)
- VERIFIED: human-final-arbiter principle. Fedora: "AI should not make the final determination on whether a contribution is accepted" — [Fedora proposal](https://discussion.fedoraproject.org/t/council-policy-proposal-policy-on-ai-assisted-contributions/165092). LLVM bans agents acting without human approval — [LLVM](https://llvm.org/docs/AIToolPolicy.html). Anthropic's adversarial reviewer is a second opinion, not the decision — [best practices](https://code.claude.com/docs/en/best-practices)
- PARTIALLY VERIFIED: OWASP LLM02:2025 Sensitive Information Disclosure is the relevant category for secrets in prompts (not fetched this pass) — [OWASP GenAI Top 10](https://genai.owasp.org/llm-top-10/)

### Inferences
- Concrete assignment controls: (1) checked-in `.claude/settings.json` with `permissions.deny` for `Read(**/.env)`, `Read(**/appsettings.*.Development.json)` / user-secrets paths, and `Bash(curl *)` if desired; (2) use `dotnet user-secrets` or env vars, never paste connection strings into prompts, and note "redacted" in the log; (3) a dependency rule: every new NuGet package is verified on nuget.org (owner, download count, repo link), pinned, and logged with rationale. Optionally add `dotnet list package --vulnerable` as a gate; (4) run `/security-review` or the security-reviewer subagent on high-impact diffs (redirect validation/open redirect, input validation, rate limiting, persistence), followed by a human sign-off line in the AI log and PR.
- For a URL shortener, the high-impact areas are: open redirect/SSRF via target URL validation, short-code collision and enumeration, injection in persistence, and abuse/rate limiting.

### Gaps
- GitHub Copilot equivalents (content exclusion, duplicate-code/public-code filter) were not researched this pass. The OWASP "Top 10 for LLM" page and NIST AI 600-1 were not fetched for exact clauses.
