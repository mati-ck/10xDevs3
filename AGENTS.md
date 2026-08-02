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

## 10xDevs AI Toolkit - Module 2, Lesson 3

Review AI-generated code before merge with the **implementation review chain**:

```
/10x-implement -> /10x-impl-review -> triage -> (/10x-lesson | fix | skip | disagree)
```

`/10x-impl-review` is the lesson focus. Review is a quality gate, not an instruction to fix every finding.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Code review (lesson focus)** | |
| `/10x-impl-review <change-id>` | You have implemented code and want a structured review before merge. The skill checks plan adherence, scope discipline, safety and quality, architecture, pattern consistency, and success criteria, then presents findings for triage. |
| **Recurring lesson outcome** | |
| `/10x-lesson` | A finding reveals a recurring project rule or agent failure pattern. Record it in `context/foundation/lessons.md` instead of treating it as a one-off note. |

### Triage discipline

- Severity says how bad the finding is. Impact says how much the decision matters now.
- Valid outcomes: fix now, fix differently, skip, accept as risk, record as recurring rule (`/10x-lesson`), disagree.
- Fix critical findings. Do not burn hours on low-impact observations just because the agent found them.
- Conscious skipping of low-impact findings is a valid review outcome, not negligence.
- If you disagree with a finding, record why. Wrong agent reasoning is also signal.

### Review boundaries

- This lesson reviews implemented code. It does not create the plan, execute new phases, or teach CI review.
- Testing strategy and quality gates are introduced in Module 3.
- Do not use `/10x-contract` as a triage outcome in this lesson.

### Paths used by this lesson

- `context/changes/<change-id>/plan.md` - expected implementation contract
- `context/changes/<change-id>/reviews/` - review output
- `context/foundation/lessons.md` - recurring lessons

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
