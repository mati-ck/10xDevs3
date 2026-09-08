<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Usunięcie notatki (S-05)

- **Plan**: `context/changes/delete-note/plan.md`
- **Scope**: Phases 1–2 of 2 (full plan)
- **Date**: 2026-09-08
- **Verdict**: NEEDS ATTENTION (triaged 2026-09-08 — F1–F4 fixed, F5 accepted)
- **Findings**: 0 critical, 2 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — A concurrent delete is reported as a false ownership violation

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Notes/NoteService.cs:236-256`, `Data/AppDbContext.cs:245-247`
- **Detail**: The plan decided the delete is idempotent — "treat as success, redirect anyway". The
  code implements that only for the case where the *read* finds nothing. If the row disappears
  between the read and `SaveChangesAsync` (two tabs on the same note), `OwnerId` is a concurrency
  token, so the emitted `DELETE … WHERE id = @p AND owner_id = @p1` affects zero rows, EF raises
  `DbUpdateConcurrencyException`, and `AppDbContext.IsOwnershipViolation` reclassifies it —
  the entry is `IOwnedByUser`, so the test passes — into
  `InvalidOperationException("Cannot modify or delete a Note owned by another user.")`.
  The page's catch-all then shows "Nie udało się usunąć notatki. Spróbuj ponownie za chwilę." for
  a note that is already gone, so every retry fails identically.
  **Verified in-session** with a throwaway SQLite probe: exception type `InvalidOperationException`,
  message as quoted above.
  This slice also invalidates a documented assumption: `AppDbContext.IsOwnershipViolation`'s remarks
  claim "there is no other way for an owned entity to produce one" — deletion is now that other way.
- **Fix A ⭐ Recommended**: Stop classifying a vanished row as an ownership violation, and let
  `DeleteAsync` treat it as the already-gone case.
  - Strength: Fixes it at the source, restores the idempotency the plan decided on, and repairs the
    `AppDbContext` remark this slice falsified. `Deleted` entries are the only state where "zero rows
    matched" has an innocent explanation, so the ownership guard keeps its teeth for `Modified`.
  - Tradeoff: Touches core infrastructure every entity flows through, so it needs its own test.
  - Confidence: HIGH — mechanism reproduced directly against the real `AppDbContext`.
  - Blind spot: Not checked whether any future caller wants to distinguish the two cases.
- **Fix B**: Leave the behavior; document the race in `DeleteAsync`'s remarks.
  - Strength: Zero risk to shared infrastructure; the window is milliseconds wide and a refresh
    shows the truth.
  - Tradeoff: Leaves a misleading message in the logs and on screen, and leaves the falsified
    `AppDbContext` remark standing for the next reader.
  - Confidence: HIGH — the impact really is limited to a rare double-tab case.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — `IsOwnershipViolation` now excludes `Deleted` entries; `DeleteAsync` catches `DbUpdateConcurrencyException` and returns `false`; one new test pins it.

### F2 — `plan.md` still specifies the confirmation and query binding that were replaced

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/changes/delete-note/plan.md` (Phase 2, "Changes Required" 1 and 2)
- **Detail**: Two authorized deviations landed during implementation — the in-page
  `alert alert-warning` panel became a modal dialog (user request during 2.6), and
  `public bool NoteDeleted` became `string?` (crash found during 2.5). Both are recorded in
  `change.md`, but `plan.md` still asserts the superseded contract verbatim, including the
  `alert alert-warning` shape and the `bool` parameter whose binding throws. `/10x-archive` freezes
  `plan.md` as the durable record, so the wrong contract is what a future reader inherits.
- **Fix**: Add a short "Addendum — deviations" section to `plan.md` pointing at the two `change.md`
  entries, leaving the original Phase 2 text intact as the record of what was planned.
- **Decision**: PENDING

### F3 — The hand-rolled modal omits three behaviors Bootstrap's JS would supply

- **Severity**: 📋 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: `Components/Pages/Notes/Detail.razor:103-145`
- **Detail**: Driving the modal from Blazor state was the right call given `App.razor` loads no
  Bootstrap JS, and focus-on-open plus Escape-to-cancel are implemented. Three things Bootstrap's
  own modal provides are still missing: the page behind scrolls (no `body.modal-open`), clicking the
  backdrop does not dismiss, and there is no focus trap — Tab walks out of the dialog into the page
  behind. That last one sits awkwardly beside `aria-modal="true"`, which tells assistive tech the
  outside is inert when for keyboard users it is not.
- **Fix**: Add a focus trap by handling Tab/Shift+Tab within the dialog, or accept the gap and drop
  `aria-modal="true"` so the markup stops promising modality it does not enforce.
- **Decision**: FIXED — focus trap added via two `visually-hidden` focus sentinels; initial focus moved from the container to `Anuluj` so the wrap is correct in both directions. Scroll lock and backdrop-click-dismiss deliberately still absent. Needs a manual Tab / Shift+Tab check.

### F4 — No test pins the ledger surviving a delete

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `tests/10xNotes.Tests/NoteServiceTests.cs`
- **Detail**: Deliberately descoped during planning and recorded in the brief's Open Risks. Verified
  correct by inspection — `DeleteAsync` touches no `note_events` code — so this is a regression guard,
  not a defect. The invariant it would protect is load-bearing: `NoteEvent` carries no foreign key
  precisely so the 75%-acceptance measurement outlives the rows it counts.
- **Fix**: Add one fact asserting `note_events` rows for the material survive `DeleteAsync`.
- **Decision**: FIXED — `Deleting_a_note_leaves_the_acceptance_ledger_intact` asserts both the `Generated` and `Saved` rows survive while the note is gone.

### F5 — The deletion notice outlives the event it reports

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Components/Pages/Materials/Detail.razor:79-93`
- **Detail**: `?noteDeleted=1` stays in the URL, so the notice returns on refresh after dismissal and
  remains on screen while a replacement note is streaming. Named as an accepted tradeoff in the
  brief's Open Risks. Bounded: accepting a draft navigates away to `/notes/{id}`, so the notice
  cannot be shown next to a saved note.
- **Fix**: On dismissal, replace the URL without the parameter via `NavigationManager.NavigateTo(…, replace: true)`.
- **Decision**: SKIPPED — accepted tradeoff, already recorded in the brief's Open Risks; bounded because accepting a draft navigates away.
