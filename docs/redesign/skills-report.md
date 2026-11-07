# Skills and tools audit

Node 24.19.0, npm 11.17.0, Python 3.14.7, Git, and the repository-local .NET SDK 8.0.424 were available. Current Skills CLI metadata specifies Node >=22.20. A pinned `skills@1.7.6` package was inspected before running list commands, with telemetry disabled. Its filtered list reported no skills; this did not reflect the separately accessible `~/.codex/skills` manifests.

| Candidate | Scope / source | Action and WPF relevance |
|---|---|---|
| UI UX Pro Max | Global, `nextlevelbuilder/ui-ux-pro-max-skill` | Reused; valid manifest and WPF-specific accessibility search. Web implementation suggestions were ignored. |
| Data visualization | Global, local host skill | Reused for encodings, units and accessible alternatives. |
| Anthropic frontend-design | Project, `anthropics/skills/skills/frontend-design` | Installed after reading its manifest; used for restrained art direction. Available to skill discovery in a future turn. |
| Vercel web-design-guidelines | Global, Vercel agent skills | Already present; web-specific audit workflow not run on WPF. |
| Taste | `Leonxlnx/taste-skill`, manifest `design-taste-frontend` | Skipped: current manifest is a website/HTML asset and browser workflow whose prescriptions conflict with this native product and minimal motion. |
| Impeccable | `pbakaus/impeccable` | Not installed: current integration requires a project hook approval and a self-contained executable that can download on first use. Its installer/engine was not executed; review was performed directly. |
| UI Skills | `ibelick/ui-skills` | Listed nine candidates; root routes through a web-oriented CLI. Skipped as redundant for native WPF with the existing skills. |
| Hallmark / emilkowalski skills | `Nutlope/hallmark`, `emilkowalski/skills` | Not installed; website aesthetics and motion do not justify another workflow for a low-motion WPF app. |
| Magic MCP | `21st-dev/magic-mcp` | Skipped: MCP component-generation service, not a native WPF design skill. |
| shadcn/ui / react-bits / Magic UI / motion-primitives | Respective named GitHub repos | Skipped: React/web component or motion libraries, not WPF runtime dependencies. |
| Frontend design toolkits | `acumenix/claude-code-frontend-design-toolkit`, `JaimeJunr/claude-code-frontend-skills` | Skipped: web/agent frontend collections overlap existing guidance and were not necessary for this WPF pass. |

`AGENTS.md` records future task routing. No hook was approved, security settings changed, or web UI runtime added.
