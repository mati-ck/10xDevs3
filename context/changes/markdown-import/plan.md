# Import pliku Markdown jako materiał źródłowy (S-01a) — Implementation Plan

## Overview

Deliver roadmap slice **S-01a**: a signed-in user picks a `.md` file, the app validates it, stores its text in Postgres bound to that user's account, and lands them on a detail page that shows the saved material. The material survives logout and is invisible to every other user.

This is the project's **first domain entity**. Everything AI-related (`S-01b`) and the note entity itself (`S-01c`) are explicitly out of scope — this slice exists so those two can be built against a real, persisted `SourceMaterial`.

## Current State Analysis

F-01 (`persistence-baseline`) and F-02 (`email-password-auth`) are both `done`, and between them they have already settled everything this slice would otherwise have to invent:

- **Ownership is automatic.** Any entity implementing `IOwnedByUser` (`Data/IOwnedByUser.cs:13`) gets a global query filter, owner stamping on insert, `OwnerId` frozen after insert, and `OwnerId` as a concurrency token — all applied by convention in `AppDbContext.ApplyOwnerFilter` (`Data/AppDbContext.cs:70`). Per-query owner filters are forbidden by `CLAUDE.md`.
- **`Profile` is the entity template** (`Data/Entities/Profile.cs`): surrogate `Id` with a `gen_random_uuid()` default, a separate `OwnerId` FK into `auth.users`, and `CreatedAt` defaulted to `now()`.
- **Migrations follow a fixed two-part shape** (`Migrations/20260727173434_InitialPersistenceBaseline.cs:45`): the EF-generated `CreateTable`, then a hand-written `migrationBuilder.Sql` block for the two things EF cannot express — the cross-schema FK to `auth.users` and `ENABLE ROW LEVEL SECURITY`. `context/foundation/lessons.md` makes RLS-in-the-same-migration a hard rule.
- **Routes are deny-by-default.** `Program.cs:86` installs a `FallbackPolicy` requiring an authenticated user, so a new page is protected without any attribute. Only `[AllowAnonymous]` pages are public.
- **The test suite is xUnit + SQLite in-memory** (`tests/10xNotes.Tests/OwnerScopingTests.cs`), with no bUnit and no component-test harness.

What is missing: there is no domain entity beyond `Profile`, no file-upload path anywhere in the app, no `Services/`-style folder for domain logic (auth logic lives in `Auth/`), and `Components/Pages/` still carries the template's `Counter` / `Weather` pages.

## Desired End State

A signed-in user can navigate to **Materiały → Importuj**, pick a `.md` file under 128 KB, adjust the auto-filled title, and click **Importuj**. They are redirected to `/materials/{id}`, which shows the title, the import timestamp, and the file's exact text. Logging out and back in, the same URL still shows it. A second account signed in at the same time sees nothing at that URL.

Verified by: `dotnet build` clean, `dotnet test` green (including new `SourceMaterial` owner-scoping and validator cases), and a manual pass through the flow plus the four rejection cases.

### Key Discoveries

- **`IDbContextFactory<>` is a build-breaking trap here.** The Microsoft docs pattern for saving uploads with EF Core is `@inject IDbContextFactory<T>` — and `tests/10xNotes.Tests/DataAccessBoundaryTests.cs:99` explicitly fails on both `DbContext` and `IDbContextFactory<>` in app code. `UserScopedDbContextFactory` is the only sanctioned seam, and `deploy.yml` runs the suite before deploying, so a violation blocks the deploy.
- **`InputFile` needs an interactive render mode.** In a Blazor Web App, `InputFile`'s change event does not fire under static SSR — confirmed against current ASP.NET Core docs. The existing auth pages are deliberately static SSR (`Components/Pages/Account/Register.razor`), so the import page departs from that precedent with an explicit `@rendermode InteractiveServer`.
- **`OpenReadStream` defaults to a 500 KB cap** and throws above it; `IBrowserFile.Size` is client-supplied and must not be trusted as the bound. Both facts are from the current file-upload docs.
- **`Profile` deliberately does not use Supabase's canonical `profiles.id = auth.users.id` shape** (`Data/Entities/Profile.cs:7`) precisely so that later entities like this one need no special-casing — `SourceMaterial` follows the same surrogate-key + `OwnerId` convention.
- **`OwnerScopingTests` supplies `Id` and `CreatedAt` explicitly** (`tests/10xNotes.Tests/OwnerScopingTests.cs:197`) because their defaults are Postgres functions SQLite cannot evaluate. New tests must do the same.

## What We're NOT Doing

- **No note entity, no AI, no generation** — that is `S-01b` / `S-01c`.
- **No pasted-text input** — that is `S-02` (`paste-text-generation`).
- **No browse/list view of materials** — that is `S-03` (`browse-notes-and-sources`). The detail page is reachable by URL and by the post-import redirect only.
- **No editing of a saved material's content or title after import** — that is `S-04` (`edit-source-material`), currently `blocked` on PRD Open Question 1. The title is editable *on the import form only*, before the row exists.
- **No delete** — that is `S-06` (`delete-source-material`), `blocked` on PRD Open Question 2.
- **No Markdown-to-HTML rendering.** The detail page shows raw text; no Markdig, no sanitizer.
- **No deduplication.** Importing the same file twice creates two rows.
- **No multi-file upload.** One file per import.
- **No transcoding of non-UTF-8 files.** They are rejected, not converted.

## Implementation Approach

Three phases, each independently verifiable:

1. **Data first.** `SourceMaterial` + migration + owner-scoping tests. At the end of this phase nothing is user-visible, but the table exists with RLS on and the privacy guarantee is executable.
2. **Logic second, in a pure seam.** All import rules (extension, size, decode, emptiness, title derivation) live in one type with no I/O and no EF dependency, fully unit-tested. This is what makes the chosen "validation seam plus owner scoping" test strategy possible without bUnit.
3. **UI last.** The import page and detail page consume phases 1 and 2. The Razor components hold no rules of their own — they bind the form, call the validator, save through `UserScopedDbContextFactory`, and map validator outcomes onto Polish messages.

## Critical Implementation Details

**Timing & lifecycle.** The 128 KB bound must be applied at **three** points, and skipping any one of them leaves a hole: `accept=".md,.markdown"` plus a size check on `IBrowserFile.Size` for fast client feedback, an explicit `maxAllowedSize` argument to `OpenReadStream` (its default is 500 KB, and it throws rather than truncating), and a post-decode check on the resulting string. The `OpenReadStream` argument is the load-bearing one — `IBrowserFile.Size` is client-supplied.

**State sequencing.** Title derivation runs on the `InputFileChangeEventArgs` callback, not on submit, so the user sees the auto-filled title and can correct it before saving. But it must only overwrite the title box when the user has not already typed into it, or picking a second file silently discards their edit.

## Phase 1: Domain entity and migration

### Overview

Introduce `SourceMaterial` as the first user-owned domain entity, create its table with the same FK-into-`auth`-schema and RLS treatment `profiles` got, and extend the executable privacy proof to cover it.

### Changes Required:

#### 1. The entity

**File**: `Data/Entities/SourceMaterial.cs`

**Intent**: Add the first domain entity — an imported Markdown document owned by exactly one user. Follows the `Profile` convention (surrogate key + separate `OwnerId`) so it inherits query filtering, owner stamping, and the write-side guards with no per-entity code.

**Contract**: `public sealed class SourceMaterial : IOwnedByUser` in namespace `_10xnotes.Data.Entities`, with:
- `Guid Id` — surrogate primary key, `gen_random_uuid()` default.
- `Guid OwnerId` — FK to `auth.users.id`; **not** unique here (unlike `Profile`), since a user owns many materials.
- `string Title` — required, max 200 chars.
- `string Content` — required, the decoded UTF-8 text; unbounded (Postgres `text`).
- `string OriginalFileName` — required, max 260 chars; the name as uploaded, kept so a renamed title does not lose provenance.
- `DateTimeOffset CreatedAt` — `now()` default.

#### 2. Context registration

**File**: `Data/AppDbContext.cs`

**Intent**: Expose the new entity and configure its column constraints and index alongside `Profile`. No query filter or owner logic is written here — implementing `IOwnedByUser` is sufficient, and the existing reflection loop at `Data/AppDbContext.cs:55` picks it up.

**Contract**: Add `public DbSet<SourceMaterial> SourceMaterials => Set<SourceMaterial>();` and a `modelBuilder.Entity<SourceMaterial>` block in `OnModelCreating` setting the key, the `gen_random_uuid()` / `now()` defaults, `HasMaxLength` on `Title` (200) and `OriginalFileName` (260), required on `Title` / `Content` / `OriginalFileName`, and a **non-unique** index on `OwnerId` (the list query in `S-03` and the detail lookup both filter by it).

#### 3. Migration

**File**: `Migrations/<timestamp>_AddSourceMaterial.cs` (generated via `dotnet ef migrations add AddSourceMaterial`)

**Intent**: Create the `source_materials` table and apply the two things EF cannot express, in the same migration — per `context/foundation/lessons.md`, RLS must not be a follow-up.

**Contract**: After the generated `CreateTable`, a hand-written `migrationBuilder.Sql` block mirroring `Migrations/20260727173434_InitialPersistenceBaseline.cs:45` — the FK `fk_source_materials_owner_id_auth_users` referencing `auth.users (id) ON DELETE CASCADE`, and `ALTER TABLE public.source_materials ENABLE ROW LEVEL SECURITY;` with **no policies** (the app's `postgres` role bypasses RLS; access control is the query filter's job). `Down` drops the constraint before dropping the table, matching the existing migration's shape. Carry over the explanatory comment style — the *why* of a policy-less RLS is not obvious to a future reader.

#### 4. Owner-scoping proof

**File**: `tests/10xNotes.Tests/OwnerScopingTests.cs`

**Intent**: Extend the existing executable privacy proof to the new entity, so the PRD guardrail ("material of one user is never visible to another") is enforced for source materials and not merely inherited by assumption.

**Contract**: Add `SourceMaterial` cases mirroring the existing `Profile` ones — a user sees only their own materials; a caller-supplied `OwnerId` is overwritten on insert; modifying and deleting another user's material are both refused; an unauthenticated context sees nothing and cannot write. Supply `Id` and `CreatedAt` explicitly in the test factory, per the note at `tests/10xNotes.Tests/OwnerScopingTests.cs:195`.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- Full suite passes, including the new `SourceMaterial` owner-scoping cases: `dotnet test`
- The data-access boundary test still passes (no `DbContext` / `IDbContextFactory<>` crept into app code): `dotnet test --filter DataAccessBoundaryTests`
- Migration is generated and the model snapshot is in sync — `dotnet ef migrations add` followed by `dotnet build` produces no pending-model-changes warning

#### Manual Verification:

- `dotnet run` boots and `/health/ready` reports healthy — the migration self-applied via `DatabaseMigrationHostedService` without degrading readiness
- The Supabase security advisor reports no new RLS finding for `public.source_materials`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Import validation seam

### Overview

Put every import rule in one pure, I/O-free type so the branches most likely to be wrong — the size bound, the encoding check, the title derivation — are unit-tested without a browser, a database, or a component harness.

### Changes Required:

#### 1. The validator

**File**: `SourceMaterials/MarkdownImportValidator.cs`

**Intent**: Own all import rules in one place, with no dependency on `IBrowserFile`, EF, or ASP.NET — so it is directly unit-testable and the Razor page holds no logic beyond binding and message mapping. New top-level folder because domain logic does not belong in `Data/` (which is the persistence layer) or `Auth/`.

**Contract**: A public static type in namespace `_10xnotes.SourceMaterials` exposing:
- `public const long MaxFileSizeBytes = 128 * 1024;` — the single source of truth for the cap, referenced by the page's pre-check and its `OpenReadStream` argument alike.
- `static bool IsAcceptedExtension(string fileName)` — `.md` / `.markdown`, case-insensitive.
- `static string DeriveTitle(string fileName)` — filename minus extension, trimmed, collapsed to ≤ 200 chars; falls back to a non-empty placeholder if the result is blank, since `Title` is required.
- `static MarkdownImportResult Validate(byte[] content, string fileName)` — runs the ordered checks and returns either the decoded text or a typed failure.

Decoding is the one non-obvious part and warrants a snippet, because the naive call silently produces mojibake instead of failing:

```csharp
// Throws on invalid UTF-8 rather than substituting U+FFFD. The default UTF8Encoding is
// lenient, so a mis-encoded or binary file would decode "successfully" into replacement
// characters and be stored as garbage — surfacing only in S-01b as nonsense output.
private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
```

Strip a UTF-8 BOM if present before storing; a leading `U+FEFF` in the content would show up in the detail view and later in the AI prompt.

#### 2. The result type

**File**: `SourceMaterials/MarkdownImportResult.cs`

**Intent**: Carry the outcome as a typed value rather than a bool plus an out-param, so the page maps failure reasons onto Polish copy exhaustively. Mirrors the `AuthResult` / `AuthFailureReason` shape already used in `Auth/AuthResult.cs`.

**Contract**: A result record with `Succeeded`, the decoded `Content` on success, and a `MarkdownImportFailure` enum on failure with members: `UnsupportedExtension`, `TooLarge`, `NotValidUtf8`, `Empty`. One member per user-visible message; no generic catch-all.

#### 3. Validator tests

**File**: `tests/10xNotes.Tests/MarkdownImportValidatorTests.cs`

**Intent**: Pin every branch of the validator, since these are the rules that decide what reaches the database and, downstream, the AI step.

**Contract**: Cases covering — accepted and rejected extensions including uppercase `.MD` and a `.txt`; a file exactly at 128 KB accepted and one byte over rejected; invalid UTF-8 bytes rejected as `NotValidUtf8`; whitespace-only and zero-byte content rejected as `Empty`; a BOM-prefixed file accepted with the BOM stripped; title derived from a plain filename, from one with several dots, from an over-200-char name (truncated), and from a name that is only an extension (placeholder fallback). Polish characters must survive the round-trip — assert on a string containing `ąęćłńóśźż`.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- Validator tests pass: `dotnet test --filter MarkdownImportValidatorTests`
- Full suite still green: `dotnet test`

#### Manual Verification:

- None — this phase has no user-visible surface.

**Implementation Note**: This phase is fully covered by automated verification; proceed to Phase 3 once the suite is green.

---

## Phase 3: Import and detail pages

### Overview

The user-visible slice: an upload form that enforces the rules from Phase 2 and writes through the sanctioned data seam, and a read-only page that proves the material is saved to the account.

### Changes Required:

#### 1. Import page

**File**: `Components/Pages/Materials/Import.razor`

**Intent**: Let a signed-in user pick a `.md` file, review the auto-filled title, and save it as a `SourceMaterial`. On success, redirect to the new material's detail page.

**Contract**: Route `/materials/import`, `@rendermode InteractiveServer` (required — `InputFile`'s change event does not fire under static SSR in a Blazor Web App), no `[AllowAnonymous]` so the `Program.cs:86` fallback policy protects it. Injects `UserScopedDbContextFactory` and `NavigationManager` — **never** `IDbContextFactory<>` or `AppDbContext`, both of which fail `DataAccessBoundaryTests`.

Structure: an `<InputFile accept=".md,.markdown" OnChange="..." />`, an `<InputText>` bound to the title, a submit button, and a Bootstrap `alert alert-danger` region matching the existing convention at `Components/Pages/Account/Register.razor:16`. UI copy is Polish throughout.

Flow on submit: read the stream with `file.OpenReadStream(MarkdownImportValidator.MaxFileSizeBytes)` into memory → `Validate` → on failure render the mapped message → on success create the entity, save via a context from `UserScopedDbContextFactory.CreateAsync()` (`using`-disposed by the caller, per its contract at `Data/UserScopedDbContextFactory.cs:24`), then `NavigateTo($"/materials/{id}")`.

Do **not** set `OwnerId`; `AppDbContext.StampOwners` assigns it and overwrites anything supplied (`Data/AppDbContext.cs:177`).

Failure messages, one per `MarkdownImportFailure` member, in Polish — e.g. wrong extension, over-128 KB, unreadable encoding, empty file. `OpenReadStream` throwing above its bound must be caught and mapped to the same over-size message as the pre-check, so a client that lies about `Size` produces a clean error rather than an unhandled exception.

#### 2. Detail page

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Show one saved material, proving to the user that it persisted under their account. This is also the surface `S-01b` will extend with a "Generuj" button and the note pane — so its layout should leave room for a second column rather than assume full width.

**Contract**: Route `/materials/{Id:guid}`, injects `UserScopedDbContextFactory`. Loads the material by id in `OnInitializedAsync`; because the global query filter is applied automatically, another user's id simply returns `null` — render a Polish "not found" message for that case, identical to the genuinely-missing case, so the page cannot be used to probe which ids exist.

Renders the title as `<h1>`, the import date, and the content **raw** inside a scrollable `<pre>`. No Markdown rendering, no `MarkupString` — the content is user-supplied and must never reach the DOM as markup.

#### 3. Navigation

**File**: `Components/Layout/NavMenu.razor`

**Intent**: Give the import flow an entry point for signed-in users.

**Contract**: Add a `NavLink` to `materials/import` inside the existing `<Authorized>` block (`Components/Layout/NavMenu.razor:12`), labelled in Polish, following the surrounding `nav-item px-3` + `bi` icon pattern. Remove the template's `Counter` and `Weather` links — they are scaffolding, and leaving them beside the first real feature makes the app read as unfinished.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- Full suite passes: `dotnet test`
- The boundary test confirms the new pages went through the sanctioned seam: `dotnet test --filter DataAccessBoundaryTests`

#### Manual Verification:

- Signed out, `/materials/import` redirects to `/login`
- A valid `.md` file with Polish characters imports and the detail page shows its exact text, correctly encoded
- The auto-filled title appears on file selection, is editable, and the edited value is what gets saved
- All four rejections show a clear Polish message and save nothing: a `.txt` file, a file over 128 KB, a binary file renamed to `.md`, and an empty file
- After logout and login, the detail URL still shows the material
- Signed in as a second account, the first account's detail URL shows the not-found message — not the content
- The nav shows the import link only when signed in

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before considering the slice done.

---

## Testing Strategy

### Unit Tests:

- **`MarkdownImportValidatorTests`** — extension gate (including case and a near-miss like `.txt`), the 128 KB boundary at exactly the limit and one byte over, invalid UTF-8, BOM stripping, empty and whitespace-only content, and title derivation across plain, multi-dot, over-long, and extension-only filenames. Polish diacritics round-trip.
- **`OwnerScopingTests`** — `SourceMaterial` cases mirroring the existing `Profile` ones: cross-user invisibility, owner stamping on insert, forged-`OwnerId` rejection, and refusal to modify or delete another user's row.

### Integration Tests:

None. The slice adds no service boundary worth integration-testing, and the two seams that could break — the data layer and the validator — are covered directly. The database-facing concerns (the `auth.users` FK, RLS) are Postgres-specific and verified manually against the real database, consistent with the reasoning at `tests/10xNotes.Tests/OwnerScopingTests.cs:8`.

### Manual Testing Steps:

1. Sign in, open **Importuj**, pick a real `.md` file with Polish characters, confirm the title auto-fills, edit it, submit.
2. Confirm the redirect lands on `/materials/{id}` and the content matches the file byte-for-byte visually, diacritics intact.
3. Repeat with each rejection case — `.txt`, >128 KB, a renamed binary, an empty file — confirming a Polish message and no new row.
4. Import the same file twice; confirm two separate materials exist (duplicates are allowed by design).
5. Log out and back in; revisit the detail URL.
6. Register a second account; visit the first account's detail URL and confirm the not-found message.

## Performance Considerations

The whole file is read into memory as a `byte[]` before decoding, which at a 128 KB cap is bounded and negligible. `Content` is a Postgres `text` column with no length constraint, but the application-side cap keeps rows small. The detail page renders the entire document in one `<pre>` — fine at 128 KB, and the natural place to revisit if the cap is ever raised.

The 128 KB bound was chosen partly with `S-01b` in mind: it keeps any imported document comfortably inside an LLM context window, so the generation slice never has to chunk or truncate.

## Migration Notes

The migration is additive — a new table only — so a Coolify rollback to the previous image leaves it in place harmlessly, satisfying the backward-compatibility rule in `CLAUDE.md`. It self-applies at boot through `DatabaseMigrationHostedService`; a failure degrades `/health/ready` rather than crashing the process.

Run `dotnet tool restore` before `dotnet ef migrations add` — the `dotnet-ef` tool is pinned in `.config/dotnet-tools.json`.

## References

- Roadmap slice: `context/foundation/roadmap.md` § S-01a
- PRD: `context/foundation/prd.md` — FR-004, NFR (trwałość, prywatność)
- RLS rule: `context/foundation/lessons.md`
- Entity + migration precedent: `Data/Entities/Profile.cs`, `Migrations/20260727173434_InitialPersistenceBaseline.cs:45`
- Ownership convention: `Data/AppDbContext.cs:70`, `Data/IOwnedByUser.cs:13`
- Data-access boundary: `tests/10xNotes.Tests/DataAccessBoundaryTests.cs:99`
- Form and error-copy precedent: `Components/Pages/Account/Register.razor`
- Blazor file uploads (`InputFile`, `OpenReadStream` 500 KB default, interactive render mode requirement): ASP.NET Core docs, `aspnetcore/blazor/file-uploads.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain entity and migration

#### Automated

- [x] 1.1 Build succeeds: `dotnet build`
- [x] 1.2 Full suite passes, including the new `SourceMaterial` owner-scoping cases: `dotnet test`
- [x] 1.3 Data-access boundary test still passes: `dotnet test --filter DataAccessBoundaryTests`
- [x] 1.4 Migration generated and model snapshot in sync — no pending-model-changes warning

#### Manual

- [x] 1.5 `dotnet run` boots and `/health/ready` reports healthy
- [x] 1.6 Supabase security advisor reports no new RLS finding for `public.source_materials`

### Phase 2: Import validation seam

#### Automated

- [ ] 2.1 Build succeeds: `dotnet build`
- [ ] 2.2 Validator tests pass: `dotnet test --filter MarkdownImportValidatorTests`
- [ ] 2.3 Full suite still green: `dotnet test`

### Phase 3: Import and detail pages

#### Automated

- [ ] 3.1 Build succeeds: `dotnet build`
- [ ] 3.2 Full suite passes: `dotnet test`
- [ ] 3.3 Boundary test confirms the new pages use the sanctioned seam: `dotnet test --filter DataAccessBoundaryTests`

#### Manual

- [ ] 3.4 Signed out, `/materials/import` redirects to `/login`
- [ ] 3.5 A valid `.md` file with Polish characters imports and renders correctly on the detail page
- [ ] 3.6 Title auto-fills on selection, is editable, and the edited value is saved
- [ ] 3.7 All four rejections show a Polish message and save nothing (`.txt`, >128 KB, renamed binary, empty)
- [ ] 3.8 After logout and login, the detail URL still shows the material
- [ ] 3.9 A second account gets the not-found message at the first account's detail URL
- [ ] 3.10 The import nav link appears only when signed in
