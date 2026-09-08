# Usunięcie notatki (S-05) — Implementation Plan

## Overview

Give the user a way to delete one of their own saved notes from `/notes/{id}`, behind an in-page confirmation, and land them on that note's source material with a confirmation that it is gone. The source material is never touched, and the acceptance ledger is never touched.

This is roadmap slice **S-05** (`FR-010`), the lowest-risk item in the content-lifecycle stream: a single row, no cascade, no schema change.

## Current State Analysis

Everything the delete needs already exists; nothing today calls it.

- **`Note` is strictly 1:1 with `SourceMaterial`**, enforced by a unique index on `notes.source_material_id` (`Data/AppDbContext.cs:122`). The only foreign key points *from* the note *to* the material, and it is `NO ACTION` by deliberate decision (`Data/AppDbContext.cs:124-141`). Nothing references a note, so deleting one cascades nowhere.
- **Isolation on the write path is already guaranteed twice over.** The global query filter scopes the read (`Data/AppDbContext.cs:189`); `OwnerId` is a concurrency token, so `owner_id` lands in the `WHERE` clause of every emitted `DELETE` (`Data/AppDbContext.cs:198-201`); and `StampOwners` throws on a tracked `Deleted` entity owned by somebody else (`Data/AppDbContext.cs:287-303`). `OwnerScopingTests` already covers the general write path.
- **`NoteService` is the sanctioned seam.** `DataAccessBoundaryTests` fails the build if a component takes a `DbContext` or an `IDbContextFactory<>`, so the delete belongs in `Notes/NoteService.cs` beside `AcceptAsync` / `UpdateAsync` / `ListAsync`.
- **`NoteEvent` deliberately has no foreign key** to notes or materials (`Data/Entities/NoteEvent.cs:38-46`), precisely so the 75%-acceptance measurement cannot erase itself. `NoteEventKind` states there are two kinds "and deliberately no others" (`Data/Entities/NoteEventKind.cs:6-8`).
- **`Notes/Detail.razor` is already `InteractiveServer`** and already loads both the note and its material, so it can host the button with no render-mode change. It does *not* currently inject `NavigationManager`.
- **`Notes/Index.razor` is deliberately static SSR** — its header comment (`Components/Pages/Notes/Index.razor:11-17`) explains that an interactive render mode would run `OnInitializedAsync` twice and query the list twice. This plan leaves it alone.
- **The confirmation precedent is in-page, never `window.confirm`** — `Components/Pages/Materials/Detail.razor:99-114` documents why: a browser dialog blocks the circuit's event loop, cannot be styled, and cannot be written in Polish.

## Desired End State

A signed-in user opening one of their saved notes sees a delete action beside the editor. Clicking it asks, in Polish and in the page, whether they are sure. Confirming removes the note and lands them on `/materials/{id}` for the material it came from, where a dismissible message states the note was deleted. That material is unchanged and its "Generuj notatkę" button now behaves exactly as it does for a material that never had a note — including saving a new one, which the freed unique index now permits. Nobody can delete a note that is not theirs.

Verified by: `dotnet build`, `dotnet test` (three new tests in `NoteServiceTests`), and the manual steps in Testing Strategy.

### Key Discoveries:

- The delete needs **no migration and no new DI registration** — `NoteService` is already `AddScoped` (`Program.cs:159`).
- **Bootstrap JS is not loaded.** `Components/App.razor:9` links `bootstrap.min.css`; the only script is `blazor.web.js` (`Components/App.razor:20`). `data-bs-dismiss="alert"` would silently do nothing — dismissal must be a Blazor `@onclick`.
- `Materials/Detail.razor` runs `OnInitializedAsync` **twice** per load (prerender, then circuit) — documented at `Components/Pages/Materials/Detail.razor:227-232`.
- Static-SSR POST forms are an established pattern (`Components/Pages/Account/Login.razor:23`), so a list-row delete *would* have been possible without changing the index's render mode. Not doing it is a scope decision, not a technical limit.

## What We're NOT Doing

- **No delete affordance on the notes list (`/`) or on the material page.** One entry point, on the page that shows the note's full text.
- **No undo, and no soft delete.** No `deleted_at` column, no restore path. The delete is immediate and final once confirmed.
- **No ledger event for deletion.** `NoteEventKind` gains no member; `note_events` rows are neither written, updated, nor removed by this slice.
- **No change to `Notes/Index.razor`'s render mode.**
- **No bulk delete, and no delete of source materials** — the latter is S-06, still blocked on PRD Open Question 2.
- **No new `NoteDeleteResult` type.** A `bool` carries everything the single caller acts on.

## Implementation Approach

Two phases: the service seam and its tests first, so the delete is provable before any UI can call it; then the two page edits, which are one user-visible flow and are verified together.

The service follows `UpdateAsync` exactly — read the entity through the owner-scoped factory, act on it, save — rather than `ExecuteDeleteAsync`. That is deliberate: `ExecuteDelete` runs as a single SQL statement that never enters the change tracker, so it skips `StampOwners`, which is the *write-side* half of the isolation contract. Losing one of the two guards to save one round trip on an action taken once in a while is a bad trade in the one place where being wrong deletes user data.

## Critical Implementation Details

**Bootstrap JS is absent.** Only `bootstrap.min.css` is linked (`Components/App.razor:9`); the sole script is `blazor.web.js`. Any dismissible alert must close through a Blazor `@onclick` setting a field — `data-bs-dismiss` will render a button that does nothing.

**Order on the note page.** `note.SourceMaterialId` must be captured into a local *before* the delete call, because the redirect target is derived from it and `note` is meaningless afterwards.

**Where the dismissal flag lives on the material page.** `OnInitializedAsync` there runs twice per load. The flag is set by a user click after the circuit is open, so it is safe as a plain field — but it must not be reset inside `OnInitializedAsync`, or the alert would reappear.

---

## Phase 1: Delete seam in `NoteService`

### Overview

One owner-scoped method that removes a note and reports whether it removed anything, plus the tests that pin what it must and must not touch.

### Changes Required:

#### 1. The service method

**File**: `Notes/NoteService.cs`

**Intent**: Add the only code path in the application that deletes a note, so no page talks to the database and the owner guarantees apply by construction rather than by the caller remembering them.

**Contract**: `public async Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default)`, placed after `UpdateAsync`. Obtains a context from `UserScopedDbContextFactory`; finds the note with `FirstOrDefaultAsync(n => n.Id == noteId, …)` and **no hand-written owner filter** — the global query filter is what makes a foreign id find nothing, exactly as every other method in this class notes. Returns `false` when the note is null. Otherwise `db.Notes.Remove(note)`, `SaveChangesAsync`, return `true`.

Writes no `NoteEvent`. The XML remarks must say why, in the same terms `UpdateAsync`'s remarks use: the ledger is the record of what was generated and accepted, deleting the note does not un-accept it, and a `Deleted` kind would break the `count(Saved) / count(Generated)` definition `NoteEventKind` documents.

The remarks must also state why this is a tracked `Remove` rather than `ExecuteDeleteAsync` — see Implementation Approach.

#### 2. Tests

**File**: `tests/10xNotes.Tests/NoteServiceTests.cs`

**Intent**: Prove the three properties the delete has to get right, reusing the class's existing `CreateService` / `CreateContext` / `SeedMaterial` helpers and its `UserA` / `UserB` fixtures.

**Contract**: Three `[Fact]`s under a new `// -- Deleting ---` banner, named in the file's existing sentence style:

- `Deleting_a_note_removes_it_and_leaves_the_users_other_notes` — seed two materials each with an accepted note, delete one, assert the return is `true`, that the remaining note is `Assert.Single(context.Notes)`, and that it is the *other* one.
- `Deleting_someone_elses_note_removes_nothing` — accept a note as `UserA`, call `DeleteAsync` from a service built for `UserB`, assert the return is `false` and that the row still exists when read back as `UserA`.
- `A_material_can_take_a_new_note_after_its_note_is_deleted` — accept, delete, then `AcceptAsync` again for the same material; assert the second accept succeeds and that exactly one note exists for that material. This is what proves the delete genuinely frees the unique index on `source_material_id` rather than leaving a ghost.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- The three new `NoteServiceTests` facts pass
- `DataAccessBoundaryTests` still passes — the new method took the sanctioned seam

#### Manual Verification:

- None. This phase adds no user-visible behavior; proceed straight to Phase 2.

---

## Phase 2: Delete on the note page, acknowledged on the material page

### Overview

The button, the in-page confirmation, the redirect, and the message the user lands on — one flow, verified end to end.

### Changes Required:

#### 1. Delete action on the note page

**File**: `Components/Pages/Notes/Detail.razor`

**Intent**: Offer the delete beside the editor, ask for confirmation in the page rather than through a browser dialog, and hand the user to the note's source material afterwards.

**Contract**: Add `@inject NavigationManager Navigation` (the page does not have it today).

Markup, inside the note column below `<NoteEditor …>`: a `btn btn-outline-danger` labelled `Usuń notatkę`, disabled while `isSaving` or `isDeleting`. When the confirmation is pending, an `alert alert-warning` block matching the shape at `Components/Pages/Materials/Detail.razor:99-114` — the question, a `btn btn-danger btn-sm` accept, and a `btn btn-outline-secondary btn-sm` `Anuluj`. Polish copy: question `"Usunięcie notatki jest nieodwracalne. Materiał źródłowy pozostanie nietknięty. Kontynuować?"`, accept `"Usuń notatkę"`.

State: a `bool isConfirmingDelete` and a `bool isDeleting`. A single `bool` rather than a `Confirmation` enum — that page has two mutually exclusive confirmations to distinguish between, this one has one; if a second is ever added here, promote it to an enum then.

Handler: capture `note.SourceMaterialId` into a local **first**, set `isDeleting` before the first `await` (the same reason `SaveAsync` sets `isSaving` early, `Components/Pages/Notes/Detail.razor:129-131`), call `NoteService.DeleteAsync(Id)`, then `Navigation.NavigateTo($"/materials/{materialId}?noteDeleted=1")` **regardless of the returned bool** — a note that was already gone satisfies the user's intent, and telling a `false` apart from a `true` would reintroduce the id-probing distinction `NoteSaveFailure.NotFound` exists to avoid. Log at information level when it returns `false`, so a bug that silently deletes nothing is findable.

Wrap the call in the same `try`/`catch (Exception)` posture `SaveAsync` uses: `NoteService` returns rather than throws, so reaching the catch means something outside its contract broke; log it and set a Polish error message rather than letting it tear down the circuit.

#### 2. Acknowledgement on the material page

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Tell the arriving user the note was deleted, since otherwise the page is indistinguishable from a material that never had one.

**Contract**: A `[SupplyParameterFromQuery(Name = "noteDeleted")] public bool NoteDeleted { get; set; }` parameter and a `bool noteDeletionDismissed` field. In the note column, above the "Generuj notatkę" button, render an `alert alert-success` reading `"Notatka została usunięta."` when `NoteDeleted && !noteDeletionDismissed`, with a close control that sets `noteDeletionDismissed = true` via `@onclick`.

**Not** `data-bs-dismiss` — no Bootstrap JS is loaded (`Components/App.razor:9,20`), so that attribute renders a dead button. Do not reset `noteDeletionDismissed` in `OnInitializedAsync`, which runs twice per load.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- `DataAccessBoundaryTests` still passes — neither page injects a `DbContext` or `IDbContextFactory<>`
- `grep -r "data-bs-dismiss" Components/` returns nothing

#### Manual Verification:

- Open a saved note at `/notes/{id}`; the `Usuń notatkę` button is visible beside the editor.
- Clicking it shows the Polish warning in the page — no browser dialog appears.
- `Anuluj` dismisses the warning and leaves the note intact after a refresh.
- Confirming lands on `/materials/{id}` for that note's material, showing `Notatka została usunięta.`; the close control actually dismisses it.
- The material's text is unchanged, and the `Ten materiał ma już zapisaną notatkę` line is gone.
- Generating and saving a new note for that same material succeeds (this is the freed unique index, in the real database rather than in SQLite).
- `/` no longer lists the deleted note.
- Opening the deleted note's old URL renders the existing `Nie znaleziono notatki` page rather than an error.
- Signed in as a second account, navigating to the first account's note URL still renders `Nie znaleziono notatki` and shows no delete button.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- The three `NoteServiceTests` facts in Phase 1: removal is scoped to one note, a foreign note is untouched, and a material can take a new note afterwards.
- SQLite in-memory, consistent with the rest of that class. What SQLite proves here is the LINQ, the query filter and the unique index — all provider-independent. The Postgres-only parts (the `auth.users` foreign key, `NO ACTION` into `source_materials`, RLS) are unchanged by this slice and are covered by the manual pass.

### Integration Tests:

- None added. The project has no bUnit, so component behavior is verified manually — the same posture every prior slice took.

### Manual Testing Steps:

1. Sign in, open a saved note from `/`, and delete it through the confirmation.
2. Confirm the landing page, the message, and that dismissing it works.
3. Generate and save a new note for that same material.
4. Confirm the deleted note is gone from `/` and its old URL renders the not-found page.
5. From a second account, attempt the first account's note URL.

## Performance Considerations

None. One indexed `DELETE` by primary key, taken rarely, on a page that is already interactive.

## Migration Notes

No schema change, so nothing to migrate and nothing to roll back. A Coolify rollback to the previous image simply removes the button; notes deleted while the new image was live stay deleted, which is what the user asked for.

## References

- Confirmation-panel precedent: `Components/Pages/Materials/Detail.razor:99-114`
- Owner write-side guards: `Data/AppDbContext.cs:189`, `:198-201`, `:287-303`
- Why the ledger has no foreign key: `Data/Entities/NoteEvent.cs:38-46`
- Why the notes index stays static SSR: `Components/Pages/Notes/Index.razor:11-17`
- Roadmap item: `context/foundation/roadmap.md` → S-05
- PRD requirement: `context/foundation/prd.md` → FR-010

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Delete seam in `NoteService`

#### Automated

- [x] 1.1 Solution builds: `dotnet build`
- [x] 1.2 Test suite passes: `dotnet test`
- [x] 1.3 The three new `NoteServiceTests` facts pass
- [x] 1.4 `DataAccessBoundaryTests` still passes — the new method took the sanctioned seam

### Phase 2: Delete on the note page, acknowledged on the material page

#### Automated

- [ ] 2.1 Solution builds: `dotnet build`
- [ ] 2.2 Test suite passes: `dotnet test`
- [ ] 2.3 `DataAccessBoundaryTests` still passes — neither page injects a `DbContext` or `IDbContextFactory<>`
- [ ] 2.4 `grep -r "data-bs-dismiss" Components/` returns nothing

#### Manual

- [ ] 2.5 The `Usuń notatkę` button is visible beside the editor on `/notes/{id}`
- [ ] 2.6 Clicking it shows the Polish warning in the page — no browser dialog
- [ ] 2.7 `Anuluj` dismisses the warning and leaves the note intact after a refresh
- [ ] 2.8 Confirming lands on `/materials/{id}` with `Notatka została usunięta.`, and the close control dismisses it
- [ ] 2.9 The material's text is unchanged and the `Ten materiał ma już zapisaną notatkę` line is gone
- [ ] 2.10 Generating and saving a new note for that same material succeeds
- [ ] 2.11 `/` no longer lists the deleted note
- [ ] 2.12 The deleted note's old URL renders `Nie znaleziono notatki`
- [ ] 2.13 A second account cannot reach or delete the first account's note
