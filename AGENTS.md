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

## 10xDevs AI Toolkit - Module 2, Lesson 1

Move from sprint-zero setup to project orchestration with the **roadmap chain**:

```
(Module 1 foundation docs) -> /10x-roadmap -> backlog-ready roadmap items
```

`/10x-roadmap` is the lesson focus. `/10x-new` is intentionally introduced in Module 2, Lesson 2, when a selected roadmap item becomes an implementation change folder.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Roadmap (lesson focus)** | |
| `/10x-roadmap` | You have `context/foundation/prd.md` and a scaffolded project baseline, and you need a vertical-first MVP roadmap. The skill reads the PRD, inspects the code baseline, uses available foundation docs such as `tech-stack.md`, `infrastructure.md`, and `deploy-plan.md`, then writes `context/foundation/roadmap.md`. Use it BEFORE creating per-change folders or implementation plans. |
| **Re-run upstream if needed** | |
| `/10x-shape` / `/10x-prd` / `/10x-tech-stack-selector` / `/10x-bootstrapper` / `/10x-agents-md` / `/10x-infra-research` | Bundled from Module 1 so foundation contracts can be fixed before roadmap sequencing. If roadmap generation exposes a PRD gap, repair the PRD before pretending the backlog is ready. |

### How the chain hands off

- `/10x-roadmap` bridges product and implementation. It does not choose frameworks, design schemas, or write a per-change implementation plan.
- The output is `context/foundation/roadmap.md`: ordered milestones, vertical slices, bounded foundations, dependencies, unknowns, risk, and backlog handoff fields.
- Roadmap items should receive stable human-readable identifiers in backlog tools. The actual `context/changes/<change-id>/` folder is created in Lesson 2 with `/10x-new`.

### Roadmap boundaries

- Default to vertical slices: user-visible outcomes that cross UI, data, business logic, and integrations.
- Horizontal work is allowed only as a bounded enabler that names the downstream vertical milestone it unlocks.
- Avoid orphan horizontal work such as "build the whole database", "build all API endpoints", or "design the whole UI" before the first user-visible flow.
- Roadmap is not a calendar estimate. Do not invent dates, story points, or sprint velocity unless the user explicitly asks for a separate planning artifact.

### Foundation paths used by this lesson

- `context/foundation/prd.md` - input
- `context/foundation/tech-stack.md` - optional input
- `context/foundation/infrastructure.md` - optional input
- `context/deployment/deploy-plan.md` - optional input
- `context/foundation/roadmap.md` - output
- `context/foundation/lessons.md` - recurring rules and pitfalls
- `docs/reference/contract-surfaces.md` - load-bearing names registry

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
