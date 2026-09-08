# Usunięcie notatki (S-05) — Plan Brief

> Full plan: `context/changes/delete-note/plan.md`

## What & Why

Roadmap slice **S-05** / **FR-010**: the user can delete one of their own saved notes. It is the last must-have in the content-lifecycle stream that is not blocked on an open PRD question — S-04 and S-06 both wait on decisions about source material, while deleting a note touches nothing but the note.

## Starting Point

Every guarantee a delete needs already exists and is unused. `Note` is 1:1 with `SourceMaterial` behind a unique index, and the only foreign key points *away* from the note, so nothing cascades. Owner isolation on writes is enforced twice — `owner_id` is a concurrency token that lands in the emitted `DELETE … WHERE`, and `StampOwners` throws on a tracked `Deleted` entity owned by somebody else. What is missing is a method on `NoteService` and a button.

## Desired End State

Opening a saved note shows a delete action beside the editor. Clicking it asks, in Polish and in the page, whether the user is sure; confirming removes the note and lands them on `/materials/{id}` for its source material, where a dismissible message says the note was deleted. The material is untouched and can immediately take a newly generated note, because the unique index slot is genuinely free. A note that is not yours cannot be deleted, and cannot be distinguished from one that does not exist.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Where the affordance lives | `/notes/{id}` only | The page is already interactive and shows the note's full text, so the user necessarily sees what they are destroying; the notes index stays static SSR, as its own comment demands. |
| Confirmation | In-page warning panel | Reuses the `Materials/Detail.razor` precedent, whose comment records why `window.confirm` is wrong here — it blocks the circuit, can't be styled, can't be Polish. |
| After deleting | Redirect to `/materials/{id}` | Puts the user one click from regenerating, which is the likely reason the note was deleted. |
| Landing acknowledgement | `?noteDeleted=1` → dismissible alert | Without it the page is indistinguishable from a material that never had a note, so a mis-click is unreadable. |
| Nothing found to delete | Treat as success, redirect anyway | Makes the operation idempotent and preserves the existing posture that a foreign note and a missing note are the same answer. |
| Ledger | Untouched; no `Deleted` event kind | `NoteEvent` deliberately has no FK so the 75%-acceptance measurement outlives the rows it counts; deleting a note does not un-accept it. |
| Deletion mechanism | Tracked `Remove`, not `ExecuteDeleteAsync` | `ExecuteDelete` skips the change tracker and therefore `StampOwners`, dropping one of the two isolation guards in the one place where being wrong destroys data. |
| Result shape | `bool`, no new result type | The single caller acts on nothing finer. |

## Scope

**In scope:** `NoteService.DeleteAsync`; three SQLite tests; the button, confirmation and redirect on `Notes/Detail.razor`; the acknowledgement on `Materials/Detail.razor`.

**Out of scope:** delete from the notes list or the material page; undo and soft delete; any `note_events` change; bulk delete; deleting source materials (S-06, still blocked); any schema change.

## Architecture / Approach

`Notes/Detail.razor` → `NoteService.DeleteAsync(noteId)` → `UserScopedDbContextFactory` → owner-filtered read, tracked `Remove`, `SaveChanges`. The page then redirects to the material it captured before the call. No migration, no new DI registration — `NoteService` is already `AddScoped`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Delete seam in `NoteService` | `DeleteAsync` + three tests; no user-visible change | Reaching for `ExecuteDeleteAsync` and silently dropping the `StampOwners` guard |
| 2. Note page + material page | Button, in-page confirmation, redirect, acknowledgement | Bootstrap JS is **not** loaded, so `data-bs-dismiss` renders a dead close button |

**Prerequisites:** none beyond S-01c, which is done and archived. No migration, no config, no new secrets.
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- **The "ledger survives a delete" invariant is not pinned by a test** — deliberately descoped. The implementation writes no `note_events` code at all, so this is a regression guard rather than a behavior requirement; a future change that "tidies up orphan events" would not be caught by the suite.
- SQLite proves the query filter and the unique index; the `auth.users` FK, `NO ACTION` into `source_materials`, and RLS are unchanged by this slice and are covered by the manual pass against the real database.
- Dismissing the landing alert does not clear the query string, so a manual refresh re-shows it. Accepted.

## Success Criteria (Summary)

- A user can delete their own note in two clicks and immediately sees where it went and that the material survived.
- The same material can take a newly generated note straight afterwards.
- A second account can neither see, reach, nor delete the first account's note.
