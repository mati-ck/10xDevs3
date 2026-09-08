---
change_id: delete-note
title: Delete note
status: implementing
created: 2026-09-08
updated: 2026-09-08
archived_at: null
---

## Notes

### Deviations from `plan.md`, decided during implementation

- **Confirmation is a modal dialog, not the in-page panel the plan specifies.** Planning chose the
  in-page `alert alert-warning` pattern from `Materials/Detail.razor`; during manual verification of
  2.6 the user asked for a dialog instead. Implemented as Bootstrap modal markup driven by Blazor
  state, with a hand-rendered `modal-backdrop` — **not** `window.confirm`, whose rejection still
  holds (blocks the circuit's event loop, unstyleable, English buttons), and **not** Bootstrap's own
  modal JS, which `App.razor` does not load. Focus moves into the dialog via `ElementReference.FocusAsync`
  and Escape cancels, so it behaves as a dialog rather than merely looking like one.
  Progress row 2.6 keeps its original title; "no browser dialog" still holds — the dialog is in-app.
- **`noteDeleted` binds as `string?`, not `bool`.** The plan's `?noteDeleted=1` is unchanged, but a
  bool-typed `[SupplyParameterFromQuery]` throws on any value `bool.TryParse` rejects — including
  `1` — which unwound to the stock English `/Error` page. Presence is now the signal, so no value in
  that parameter can crash the page. Found in manual verification of 2.5; fixed in `8fe64da`.
