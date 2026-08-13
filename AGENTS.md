# Repository Guidelines

10xNotes is a .NET 10 Blazor Web App — a Polish-language note-generation MVP. `CLAUDE.md` is a symlink to this file, so Claude Code, Codex, and Copilot all read the same rules. See `@context/foundation/prd.md` for product scope and `@context/foundation/health-check.md` for current gaps.

## Hard rules

- Never write to `context/archive/` — archived changes are immutable. If a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."
- Use namespace `_10xnotes` (leading underscore, because the folder name starts with a digit). Never write `namespace 10xnotes`.
- Do not hand-edit `.claude/.10x-cli-manifest.json` or the `<!-- BEGIN/END @przeprogramowani/10x-cli -->` block below — both are managed by `10x-cli`.

## Project Structure & Module Organization

The web app lives at the repo root (`10xnotes.csproj`), with tests in `tests/10xNotes.Tests/`; `10xnotes.sln` ties them together. Because the web project sits at the root, its globs exclude `tests/**` via `DefaultItemExcludes` — keep that in mind when adding top-level folders. Razor components live in `Components/` — `Pages/` for routable pages, `Layout/` for shell components, with `App.razor` / `Routes.razor` as entry points. Data access is under `Data/` (`AppDbContext`, entities in `Data/Entities/`), EF migrations under `Migrations/`. Static assets (Bootstrap 5.3) sit in `wwwroot/`. Foundation docs are under `context/foundation/`.

## Build, Test & Development Commands

- `dotnet run` — start the app (profiles: http://localhost:5125, https://localhost:7111).
- `dotnet build` — compile; the fastest check that a change is sound.
- `dotnet test` — run the xUnit suite; this is the primary way to verify a change.
- `dotnet restore` / `dotnet tool restore` — restore packages and the pinned `dotnet-ef` tool.
- `dotnet ef migrations add <Name>` / `dotnet ef database update` — schema changes (requires `dotnet tool restore` first).

The app needs `ConnectionStrings__Postgres` (Supabase **session pooler**, port 5432 — not the IPv6-only direct endpoint, not the prepared-statement-hostile `:6543` pooler). Set it via `dotnet user-secrets` locally; see `README.md`.

## Data & persistence rules

- Application code obtains data through `UserScopedDbContextFactory`, **never** by injecting a `DbContext`. The bare `AppDbContext` registration exists only for infrastructure that must resolve it (DataProtection's key store, the startup migration service, the EF health check); a context obtained that way has no current user, so reads return nothing and writes throw — which presents as "the database is empty". `tests/10xNotes.Tests/DataAccessBoundaryTests.cs` fails if any type in the app assembly takes a `DbContext` **or an `IDbContextFactory<>`** — the factory is the more likely mistake, since it is the pattern Blazor Server docs recommend. `UserScopedDbContextFactory` is the single exempted type. `deploy.yml` runs the suite before triggering a deployment, so a violation blocks the deploy rather than shipping.
- Entities that belong to a user implement `IOwnedByUser`; `AppDbContext` then scopes every query by owner automatically and stamps `OwnerId` on insert. Do not hand-write per-query owner filters, and treat `IgnoreQueryFilters()` as a deliberate, reviewed exception.
- Migrations self-apply at startup via `DatabaseMigrationHostedService`. A failure is logged `Critical` and degrades `/health/ready` — it must never crash the process.
- `/health` is **liveness only** and must stay database-free: the container HEALTHCHECK and `deploy.yml` both assert on its literal `Healthy` body, and a failing probe makes Coolify de-route the app. Database-backed checks belong on `/health/ready` with the `ready` tag.
- Every new table in `public` must `ENABLE ROW LEVEL SECURITY` in its migration. Supabase exposes `public` through the Data API, and the anon key is public; the app's `postgres` role bypasses RLS, so deny-all costs nothing.
- A Coolify rollback does **not** reverse migrations — keep them backward-compatible.

## Coding Style & Naming Conventions

4-space indentation; `Nullable` and `ImplicitUsings` are enabled (`@10xnotes.csproj`). Render mode is **Interactive Server** (`Program.cs` wires `AddInteractiveServerComponents` / `AddInteractiveServerRenderMode`) — components run server-side over a SignalR circuit, **not** WebAssembly. Co-locate scoped styles and scripts next to their component as `<Component>.razor.css` / `<Component>.razor.js` (see `@Components/Layout/NavMenu.razor.css`). UI copy is Polish (per PRD). No `.editorconfig` or analyzers are configured yet.

## Commit & Pull Request Guidelines

Commit subjects follow Conventional Commits scoped by change id: `<type>(<change-id>): <subject>` — e.g. `feat(persistence-baseline): data layer wiring (p1)`, `test(email-password-auth): cover the GoTrue and identity seams (p4)`. Types in use: `feat`, `fix`, `test`, `chore`, `docs`. Append the phase marker (`(p1)`, `(p2)`…) when the commit lands one phase of a plan. Subjects stay short, imperative, and lower-case after the prefix. Commits that are not part of a change (toolchain updates, one-off repo fixes) use a bare type — `chore:` or `docs:` — and should not be folded into a phase commit. Branch off `main` as `<type>/<kebab-desc>` (e.g. `bootstrap/scaffold-and-health-check`, `chore/cleanup-m1l3-artifacts`). Open PRs against `main` (remote: `mati-ck/10xDevs3`).

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 2, Lesson 4

Prepare for a harder implementation stream with the **research-backed planning chain**:

```
internal research (/10x-research) + external research (exa.ai, Context7) -> /10x-plan -> /10x-implement -> success
```

The lesson focus is distinguishing internal from external research and using evidence to back planning decisions.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Internal research (lesson focus)** | |
| `/10x-research <change-id>` | You need evidence from the existing codebase — patterns, conventions, integration points, or existing implementations. Runs parallel sub-agents over the repo and writes structured findings to `research.md`. |
| **External research (lesson focus)** | |
| exa.ai | You need AI-native web search for library comparisons, best practices, or ecosystem context that the codebase cannot answer. |
| Context7 (`resolve-library-id` → `get-library-docs`) | You need live, current documentation for a specific library or framework. Resolves a library ID first, then fetches relevant doc pages. |
| **Framing spare wheel** | |
| `/10x-frame <change-id>` | The plan won't converge, the plan doesn't deliver expected results, or persistent drift keeps breaking the implementation. Use as an escape hatch on a separate problem (demonstrated on Space Explorers example), not as pre-research ritual. |
| **Planning and execution** | |
| `/10x-plan <change-id>` / `/10x-implement <change-id> phase <n>` | Use the same planning and execution chain from Lesson 2, now with upstream research evidence feeding the plan. |

### Research discipline

- Internal research (`/10x-research`) answers "what does our codebase already do?" — patterns, schemas, conventions, integration points.
- External research (exa.ai, Context7) answers "what should we do?" — library capabilities, API docs, ecosystem best practices.
- Combine both as evidence-backed input to `/10x-plan`. A plan without research evidence on a non-trivial stream is a guess.
- Agent-friendly docs (`llms.txt`, markdown-for-agents, `/md` endpoints) are a quality signal for library selection — libraries that publish agent-readable docs integrate faster.

### `/10x-frame` as spare wheel

Three triggers for reaching for `/10x-frame`:
1. The plan won't converge — research keeps opening more questions instead of narrowing to a contract.
2. The plan doesn't deliver — implementation repeatedly fails to meet success criteria.
3. Persistent drift — the implementation keeps diverging from the plan in ways that suggest the problem was mis-framed.

Demonstrated on a Space Explorers example, not the SRS path. It is an escape hatch, not a mandatory step.

### Paths used by this lesson

- `context/changes/<change-id>/research.md` - internal research output
- `context/changes/<change-id>/frame.md` - framing output when needed
- `context/changes/<change-id>/plan.md` - evidence-backed implementation contract
- `context/foundation/lessons.md` - recurring rules and pitfalls

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
