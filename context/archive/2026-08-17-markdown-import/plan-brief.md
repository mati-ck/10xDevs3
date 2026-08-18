# Import pliku Markdown jako materiał źródłowy (S-01a) — Plan Brief

> Full plan: `context/changes/markdown-import/plan.md`

## What & Why

Roadmap slice **S-01a**, the first third of the north star. A signed-in user uploads a `.md` file and it is saved to their account: durable across logout, invisible to everyone else. This is the project's first domain entity, and it exists so that `S-01b` (AI generation) and `S-01c` (review and save) have a real, persisted `SourceMaterial` to build against instead of a stub.

## Starting Point

F-01 and F-02 are `done`, and between them the hard parts are already solved: `IOwnedByUser` + `AppDbContext` give automatic per-user query filters, owner stamping on insert, and write-side guards; `Profile` is a working entity template; the migration convention for a cross-schema `auth.users` FK plus policy-less RLS is established; and `Program.cs` protects every route by default. What is missing is any domain entity beyond `Profile`, and any file-upload path at all.

## Desired End State

A signed-in user picks a `.md` file under 128 KB from **Materiały → Importuj**, adjusts the auto-filled title, and lands on `/materials/{id}` showing the saved text. It is still there after logging out and back in. A second account visiting the same URL gets a not-found message, not the content.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Title source | Filename minus extension, pre-filled and editable **on the import form only** | Keeps import to one screen and one write path, staying clear of `S-04` (`edit-source-material`), which is blocked on a PRD open question. |
| Size limit | **128 KB**, rejected with a Polish message | Bounded memory and a document that fits any LLM context window whole, so `S-01b` never inherits a chunking problem. |
| Validation | Extension gate + strict UTF-8 decode + non-empty | Catches the failure that actually hurts — a binary or mis-encoded file stored as mojibake that only surfaces as nonsense output in `S-01b`. |
| Post-import UX | Redirect to a per-material detail page | Directly proves the roadmap outcome, and it is the exact surface `S-01b` extends with the "Generuj" button and note pane. |
| Content display | Raw text in a `<pre>` | It is the *source* material and the PRD guardrail is that it stays unchanged; avoids a Markdown dependency and the XSS surface of rendering user HTML. |
| Duplicates | One file per import; duplicates allowed | Re-importing after a local edit is legitimate, and dedup would block it for no gain. |
| Testing | Pure validation seam + owner-scoping cases | Matches the existing xUnit/SQLite suite and tests the branches that can be wrong, without adding bUnit to a slice with no component logic. |

## Scope

**In scope:** `SourceMaterial` entity; migration with `auth.users` FK and RLS; a pure import validator with full unit coverage; `/materials/import` upload page; `/materials/{id}` detail page; nav entry (and removal of the template's Counter/Weather links).

**Out of scope:** the note entity, AI, and generation (`S-01b`/`S-01c`); pasted text (`S-02`); a materials list (`S-03`); editing content or title after import (`S-04`, blocked); delete (`S-06`, blocked); Markdown-to-HTML rendering; deduplication; multi-file upload; transcoding non-UTF-8 files.

## Architecture / Approach

Three layers, built bottom-up. `Data/Entities/SourceMaterial.cs` implements `IOwnedByUser` and inherits privacy enforcement by convention — no hand-written owner filters. `SourceMaterials/MarkdownImportValidator.cs` holds every import rule as pure, I/O-free logic, which is what makes the no-bUnit test strategy work. The two Razor pages under `Components/Pages/Materials/` hold no rules: they bind the form, call the validator, and write through `UserScopedDbContextFactory`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain entity and migration | `SourceMaterial` + table with RLS + owner-scoping tests | Forgetting `ENABLE ROW LEVEL SECURITY` in the same migration — a named rule in `lessons.md`, already violated once on `__EFMigrationsHistory`. |
| 2. Import validation seam | Pure validator + result type + full unit coverage | Lenient UTF-8 decoding silently producing mojibake instead of failing. |
| 3. Import and detail pages | `/materials/import`, `/materials/{id}`, nav entry | `InputFile` needs `@rendermode InteractiveServer` — it is silently inert under the static SSR the existing auth pages use. |

**Prerequisites:** F-01 and F-02 shipped (both `done`); `ConnectionStrings__Postgres` configured; `dotnet tool restore` for the pinned `dotnet-ef`.
**Estimated effort:** ~1–2 sessions across 3 phases.

## Open Risks & Assumptions

- **The `IDbContextFactory<>` trap.** The Microsoft docs pattern for EF-backed uploads is exactly what `DataAccessBoundaryTests` fails the build on. Anyone copying from the docs breaks the build — and since `deploy.yml` runs the suite first, it blocks the deploy rather than shipping.
- **128 KB may prove tight** for the very long materials the product exists to digest. It is a single constant in the validator, so raising it is cheap — but doing so re-opens the context-window question for `S-01b`.
- **Rejecting rather than transcoding** non-UTF-8 files means a Windows-1250-encoded Polish text file is refused. Assumed acceptable: modern editors default to UTF-8.
- **The detail page is reachable only by URL** until `S-03` ships. A user who imports, navigates away, and does not bookmark the URL has no way back to their material. Accepted as the cost of not pre-empting a later slice.

## Success Criteria (Summary)

- A user imports a `.md` file and immediately sees it saved, with Polish characters intact.
- The material survives logout and is invisible to any other account — proven by test, not just by inspection.
- Every rejection case (wrong type, too large, unreadable, empty) produces a clear Polish message and writes nothing.
