# Repository Guidelines

10xNotes is a .NET 10 Blazor Web App — a Polish-language note-generation MVP. `CLAUDE.md` is a symlink to this file, so Claude Code, Codex, and Copilot all read the same rules. See `@context/foundation/prd.md` for product scope and `@context/foundation/health-check.md` for current gaps.

## Hard rules

- Never write to `context/archive/` — archived changes are immutable. If a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."
- Use namespace `_10xnotes` (leading underscore, because the folder name starts with a digit). Never write `namespace 10xnotes`.
- Do not hand-edit `.claude/.10x-cli-manifest.json` or the `<!-- BEGIN/END @przeprogramowani/10x-cli -->` block below — both are managed by `10x-cli`.

## Project Structure & Module Organization

Single project at the repo root (`10xnotes.csproj`); there is no `.sln`. Razor components live in `Components/` — `Pages/` for routable pages, `Layout/` for shell components, with `App.razor` / `Routes.razor` as entry points. Static assets (Bootstrap 5.3) sit in `wwwroot/`. Foundation docs are under `context/foundation/`.

## Build, Test & Development Commands

- `dotnet run` — start the app (profiles: http://localhost:5125, https://localhost:7111).
- `dotnet build` — compile; this is the primary way to verify a change.
- `dotnet restore` — restore NuGet packages.

No test project exists yet, so `dotnet build` plus a manual run is the only verification path until one is added.

## Coding Style & Naming Conventions

4-space indentation; `Nullable` and `ImplicitUsings` are enabled (`@10xnotes.csproj`). Render mode is **Interactive Server** (`Program.cs` wires `AddInteractiveServerComponents` / `AddInteractiveServerRenderMode`) — components run server-side over a SignalR circuit, **not** WebAssembly. Co-locate scoped styles and scripts next to their component as `<Component>.razor.css` / `<Component>.razor.js` (see `@Components/Layout/NavMenu.razor.css`). UI copy is Polish (per PRD). No `.editorconfig` or analyzers are configured yet.

## Commit & Pull Request Guidelines

Commit subjects are short, imperative, sentence-case ("Add agent-readiness health check report") — no Conventional Commits prefixes. Branch off `main` as `<type>/<kebab-desc>` (e.g. `bootstrap/scaffold-and-health-check`, `chore/cleanup-m1l3-artifacts`). Open PRs against `main` (remote: `mati-ck/10xDevs3`).

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 2, Lesson 2

Turn one roadmap item into the first implementation cycle with the **change planning chain**:

```
/10x-roadmap -> /10x-new -> /10x-plan -> /10x-plan-review -> /10x-implement
```

`/10x-new`, `/10x-plan`, `/10x-plan-review`, and `/10x-implement` are the lesson focus. `/10x-frame` and `/10x-research` are not required rituals here; they are escalation paths introduced in the next lesson.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Change setup (lesson focus)** | |
| `/10x-new <change-id>` | You selected a roadmap item and need a stable change folder. Creates `context/changes/<change-id>/change.md` so planning, implementation, progress, commits, and later review all share one identity. Use AFTER roadmap selection, BEFORE `/10x-plan`. |
| **Planning (lesson focus)** | |
| `/10x-plan <change-id>` | You have a change folder and need a reviewable implementation plan. Reads roadmap context, foundation docs, codebase evidence, and any existing change notes; writes `plan.md` and `plan-brief.md` with phases, file contracts, success criteria, and `## Progress`. |
| **Plan readiness (lesson focus)** | |
| `/10x-plan-review <change-id>` | You have `plan.md` and need a light pre-code readiness check. Use it to catch missing end state, weak contracts, malformed progress, scope drift, or blind spots before code changes begin. |
| **Implementation (lesson focus)** | |
| `/10x-implement <change-id> phase <n>` | You have an approved plan and want to execute one phase with verification, manual gate, commit ritual, and SHA write-back to `## Progress`. |
| **Lifecycle closure** | |
| `/10x-archive <change-id>` | A change is merged or intentionally closed. Move it out of active `context/changes/` into archive state. |

### How the chain hands off

- `/10x-new` creates the durable change identity.
- `/10x-plan` turns that identity into an implementation contract.
- `/10x-plan-review` checks the plan before the agent mutates code.
- `/10x-implement` executes one planned phase, verifies, asks for manual confirmation when needed, commits, and records progress.

### Lesson boundaries

- Plan is the default router after roadmap selection. Start with `/10x-plan` unless the problem is unclear or external evidence is blocking.
- Do not run `/10x-frame + /10x-research` as ceremony for every change.
- Do not turn this lesson into a full end-to-end product build. A checkpoint with a planned and partially or fully implemented stream is valid.
- Code review of the implemented diff belongs to Lesson 3 via `/10x-impl-review`.
- Lifecycle closure via `/10x-archive` after a change is merged or intentionally closed.

### Paths used by this lesson

- `context/foundation/roadmap.md` - upstream roadmap
- `context/changes/<change-id>/change.md` - change identity
- `context/changes/<change-id>/plan.md` - implementation contract
- `context/changes/<change-id>/plan-brief.md` - compressed handoff
- `context/foundation/lessons.md` - recurring rules and pitfalls
- `docs/reference/contract-surfaces.md` - load-bearing names registry

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
