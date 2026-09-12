<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: UI redesign — Implementation Plan

- **Plan**: context/changes/ui-redesign/plan.md
- **Scope**: Phases 1–5 of 5 (full plan) — `43b702b..6c03e82` on `feature/ui-redesign`
- **Mode**: Deep (non-interactive run — triage decided by the reviewer, fixes applied to the code)
- **Date**: 2026-09-12
- **Verdict**: APPROVED (after the applied fixes)
- **Findings**: 0 critical · 4 warnings · 7 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Evidence

**Diff scope** (`git diff --name-status 43b702b..HEAD`): 29 files — every file the plan names and
nothing else. Two files added by plan (`AuthLayout.razor(.css)`, `Shared/Icon.razor`,
`Shared/IconName.cs`), two removed by plan (`Pages/Materials/Detail.razor.css`,
`Pages/Notes/Detail.razor.css`). No stray files, no debug CSS, no `TODO`/`FIXME`/`console.log`.

**Verification re-run on the branch tip (6c03e82, before the review fixes):**

| Check | Result |
|---|---|
| `dotnet build` | succeeded, 0 warnings, 0 errors |
| `dotnet test` | **355 passed**, 0 failed |
| Hook check — `feature/test-plan` merged into a detached worktree at `/tmp/10xDevs3-hookcheck` | clean auto-merge (`Detail.razor`, `Import.razor`, `Notes/Detail.razor`), **387 passed**, 0 failed; worktree removed |
| `grep -rn "(MarkupString)" Components` | one hit: `Notes/NoteEditor.razor:53` |
| `grep -rnE 'data-bs-(dismiss\|toggle)="\|IJSRuntime' Components` | empty |
| `@onclick` on static-SSR pages (`Pages/Account`, both index pages, `Error`, `NotFound`, `Layout`) | empty |
| `@rendermode` | unchanged: `InteractiveServer` on Materials/Detail, Import, Notes/Detail only |
| `list-group`, `text-secondary small`, `bi-*-nav-menu`, `darker-border-checkbox`, `form-floating`, `blazor-error-boundary` | empty |
| `wwwroot/*.js` | none — no new JS anywhere |
| Import merge seam (`git diff main -- Import.razor`) | the `<label for="title">`, `<InputText id="title">` and following `<div class="form-text">` lines appear as context, unchanged |
| `grep -c -- '--bs-btn-bg' wwwroot/app.css` | 6 |
| `@code` diff across all components | only NavMenu's presentational `Initial()` helper and `Icon`'s parameters; no service, validator, `FormName` or logic change |

**Guardrails re-read by hand, all intact:** source material as text in `<pre class="source-content src">`
on both detail pages; the streaming `<pre>` read-only with the caret on the same source line
(`@note@if (isGenerating){<span class="caret"></span>}`); plain interpolation for every user title,
file name and the avatar initial (rune-based); `Icon` emits literal SVG, `aria-hidden`, no `<title>`,
no text node, `StrokeWidth` a `string` (culture-safe); Materials/Detail keeps exactly one
"Generuj notatkę" button and every button text unique; `<h1>Nie znaleziono materiału</h1>` verbatim
with nothing rendering an `h1` before it; NoteEditor's counter is the only `div.form-text` and sits
before the textarea, and the save button is the only `btn-primary`; all three Profile `FormName`s
present; the delete dialog keeps both focus sentinels, both `@ref`s, `@onkeydown`, `role`,
`aria-modal`, `aria-labelledby`; the checkbox toggler is still the immediately preceding sibling of
`.nav-scrollable` with its inline `onclick`; `NavLinkMatch.All` twice; ReconnectModal's three ids
and its `.razor`/`.razor.js` untouched (CSS-only change).

## Findings

### F1 — `#blazor-error-ui` dismiss is a `role="button"` a keyboard cannot reach

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (accessibility)
- **Location**: Components/App.razor:26, wwwroot/app.css (Error UI)
- **Detail**: P2 replaced the template's `<span class="dismiss">🗙</span>` with
  `<span class="dismiss" role="button" aria-label="Dismiss">`. The `role` and the label make screen
  readers announce a button, but a `<span>` has no `tabindex`, so the control is unreachable by
  keyboard and unactivatable by Enter/Space — a new WCAG 2.1.1 failure that the plain span did not
  have. `blazor.web.js` wires it with
  `document.querySelectorAll("#blazor-error-ui .dismiss").forEach(e => e.onclick = …)` (verified in
  `microsoft.aspnetcore.app.internal.assets/10.0.9/_framework/blazor.web.js`), i.e. by class, so the
  element type is free.
- **Fix**: Render `<button type="button" class="dismiss" aria-label="Dismiss">` and reset the UA
  button chrome in `#blazor-error-ui .dismiss` (`background: none; border: 0; padding: 4px;
  border-radius: 6px`) plus a `:focus-visible` accent ring.
  - Strength: natively focusable and key-activatable; the framework's class selector is unchanged.
  - Tradeoff: none — the visual result is identical.
  - Confidence: HIGH — the wiring selector was read out of the shipped `blazor.web.js`.
  - Blind spot: the error bar is only visible during a circuit failure, so it is not exercised by any test.
- **Decision**: FIXED

### F2 — A focused note textarea keeps a grey border under the violet glow

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency (focus affordance)
- **Location**: Components/Notes/NoteEditor.razor.css:33-47
- **Detail**: P4 moved the editor box's `border: 1px solid var(--line-2)` into the scoped sheet
  (`.note-source, .note-preview`). Scoped CSS compiles to `.note-source[b-…]` — specificity (0,2,0),
  the same as `app.css`'s `.form-control:focus` — and `10xnotes.styles.css` loads *after* `app.css`,
  so the scoped border wins. `#note-content` therefore focuses with the violet `box-shadow` ring but
  a grey border, unlike every other field in the app. `#note-title`, `#paste` and all the auth and
  profile inputs are unaffected (no scoped border on them).
- **Fix**: Add `.note-source:focus { border-color: var(--accent); }` to the scoped sheet, with a
  comment naming the load-order reason.
  - Strength: one rule, in the file that caused the conflict; no specificity escalation in `app.css`.
  - Tradeoff: none.
  - Confidence: HIGH — same-specificity, later-sheet cascade is deterministic.
  - Blind spot: not visually confirmed in a browser (manual row 4.13).
- **Decision**: FIXED

### F3 — Inline links in running prose lost their only non-colour cue

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (accessibility) / Plan Adherence (the artboards say otherwise)
- **Location**: wwwroot/app.css (Base, `a { text-decoration: none }`)
- **Detail**: The artboards set `a { color: var(--accent); text-decoration: none }` globally and the
  implementation copies that. For links that *are* their own control — nav items, note cards,
  material rows, badges, breadcrumbs, the pane link — that is fine: position and shape carry the
  affordance. But the app also has links inside sentences: the Login/Register cross-link in `p.sub`,
  "Ten materiał ma już zapisaną notatkę: <title>." in `p.pane-hint` on Materials/Detail. There the
  accent (`#8C7BFF`) against the surrounding `--tx-2` (`#A2A8B7`) is a **1.38:1** luminance
  difference, so colour is the only distinction — WCAG 2.2 §1.4.1 (Level A) requires ≥ 3:1 or a
  second cue. Before the redesign these links carried the UA underline.
- **Fix**: Underline links in prose contexts only (`.sub a`, `.pane-hint a`, `.alert a`,
  `.empty-state p a`, `.narrow-card p a`, `.settings-card p a`, `.delete-dialog-body p a`) with
  `text-underline-offset: 2px`. Every navigational link keeps the artboards' bare style.
  - Strength: closes a Level-A failure with ~8 lines confined to body copy; the mockups' look is
    preserved everywhere the mockups actually draw a link.
  - Tradeoff: **a deliberate deviation from the locked design.** The block is commented and
    self-contained — deleting it restores the artboards exactly.
  - Confidence: HIGH on the contrast arithmetic; MEDIUM on the user accepting the deviation.
  - Blind spot: the user locked "1:1 with the mockups" and may prefer a different second cue
    (e.g. a distinct link colour at ≥ 3:1 against `--tx-2`).
- **Decision**: FIXED — flagged here for sign-off; revert by deleting the commented block.

### F4 — `## Progress` under-reports Phase 5 and `change.md` was not stamped

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/ui-redesign/plan.md (Progress → Phase 5), context/changes/ui-redesign/change.md
- **Detail**: Rows 5.1–5.7 are ticked but carry no ` — <sha>` suffix, which the section's own
  convention requires; the P5 note argued no later phase could back-fill them. Everything else in
  Progress is accurate: all 29 manual rows (1.7–1.9, 2.9–2.15, 3.8–3.14, 4.11–4.18, 5.8–5.11) are
  honestly left `[ ]` with a note saying a browser pass is owed, and no row claims evidence the diff
  does not show. `change.md` still read `status: implemented`.
- **Fix**: Back-fill ` — 6c03e82` on 5.1–5.7, restate the P5 note to say the review back-filled them
  and what it re-ran, and stamp `change.md` `status: impl_reviewed`.
- **Decision**: FIXED

### F5 — Dead CSS left behind by later phases

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: wwwroot/app.css (Buttons, Forms)
- **Detail**: `input[type=file].form-control`, `.form-control::file-selector-button` and its hover
  twin (20 lines) were written in P1 for the then-`form-control` file input; P3 replaced it with
  `.dropzone-input`, and no `<input type="file">` in the app carries `.form-control` any more.
  `.btn-group-sm > .btn` / `.btn-group-lg > .btn` survive although no `.btn-group` exists (P3/P4
  turned both toggles into `.seg`), and the `.btn-outline-secondary` comment still says
  "legacy btn-group toggles until P3/P4".
- **Fix**: Delete the three file-input rules and the two `.btn-group-*` selectors; shorten the
  stale comment.
- **Decision**: FIXED

### F6 — A very long note title is clipped in the delete dialog

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: wwwroot/app.css (`.delete-dialog-body p`)
- **Detail**: The dialog quotes the title back in full. `.modal-content` now has `overflow: hidden`,
  so a long space-free title is silently cut off at the dialog edge rather than wrapping. Every other
  place a user title lands already has a break rule (`h1` and `.system-card code` got
  `overflow-wrap: anywhere` in P5, `.note-card-title` in P3).
- **Fix**: `overflow-wrap: anywhere` on `.delete-dialog-body p`.
- **Decision**: FIXED

### F7 — `--tx-3` muted text is below WCAG AA on every surface

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality (accessibility)
- **Location**: wwwroot/app.css (Tokens) — consumed by `.form-text`, `.meta`, `.crumbs`,
  `.settings-hint`, `.note-counter`, `.user-mail`, `.material-head`, `.badge.st-none`, inactive
  `.seg-item`, `.auth-hero-hint`
- **Detail**: `--tx-3` (`#697083`) measures **3.98:1** on `--bg`, **3.75:1** on `--panel` and
  **3.91:1** on `--field`. WCAG 2.2 §1.4.3 (AA) asks 4.5:1 for text under 18.66 px, and every one of
  these uses is 11.5–13 px. The inactive segment label and the field hints are the worst cases,
  because they are control labels rather than decoration. `--tx-2` (`#A2A8B7`) is fine at 7.8:1.
- **Fix**: Raise `--tx-3` to roughly `#7D8598` (≈5.0–5.3:1 on all three surfaces) and let `--tx-2`
  keep the current `--tx-3` role where the design wants a third step.
  - Strength: one token, no markup change, fixes ten call sites at once.
  - Tradeoff: `--tx-3` is a **user-locked token** (`design/README.md`, decision D1) lifted verbatim
    from the artboards — changing it is exactly the kind of drift the rest of this plan refuses.
  - Confidence: HIGH on the measurements; the decision is the user's, not the reviewer's.
  - Blind spot: the artboards may have been checked against a different target (e.g. AA for the
    "non-text/incidental" exemption, which these do not qualify for).
- **Decision**: NOT FIXED — reported for the user's decision; the token is locked by the design.

### F8 — Plain links have no `:focus-visible` ring of their own

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: wwwroot/app.css (Base)
- **Detail**: The redesign defines the violet `0 0 0 3px var(--accent-soft)` ring for `.btn`,
  `.form-control`, `.seg-item`, `.brand`, the nav links and (now) the error-bar dismiss — but not for
  bare `<a>`. Breadcrumbs, `.pane-link`, `.note-card-title`, `.material-title` and the `st-ok` badge
  link fall back to the UA outline. That is still visible on the dark ground (nothing sets
  `outline: none` on links), so this is a consistency gap rather than a WCAG failure.
- **Fix**: `a:focus-visible { outline: 0; border-radius: 6px; box-shadow: 0 0 0 3px var(--accent-soft) }`.
- **Decision**: SKIPPED — cosmetic, and a blanket link rule interacts with `.stretched-link` on the
  notes grid (the ring would draw around the text, not the card); worth deciding with the browser pass.

### F9 — No fallback for `color-mix(in oklch, …)`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: wwwroot/app.css (Tokens and throughout — 50+ uses)
- **Detail**: `--accent-soft`, `--accent-line`, every alert/badge/danger tint and the subtle Bootstrap
  variables are `color-mix(in oklch, …)`. On a browser without it the custom property is invalid at
  computed-value time, so the declaration using it resolves to `unset` — alerts, badges, icon tiles
  and focus rings lose their background entirely rather than degrading. Support is Chrome 111+,
  Safari 16.2+, Firefox 113+ (all ≥ 2023), and the artboards use the same function, so this is a
  documented floor rather than a defect.
- **Fix**: If an older floor is ever required, precede each `color-mix` declaration with a static
  hex approximation.
- **Decision**: ACCEPTED — evergreen-only is the design's own assumption.

### F10 — `prefers-reduced-motion` covers two of the eight animated things

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (accessibility)
- **Location**: wwwroot/app.css (Responsive, last block); Components/Layout/NavMenu.razor.css
- **Detail**: The reduced-motion block stops `.stream-bar` and `.caret`, and NavMenu drops the
  drawer transition. Still moving: `.spin` (`animation: rot … infinite`) and the hover transitions on
  `.note-card`, `.material-row`, `.dropzone`, `.seg-item` and the nav links. The transitions are
  120–150 ms colour fades, which the criterion tolerates; `.spin` is the only continuous one, and it
  is a genuine progress indicator paired with "Generuję…"/"Zapisywanie…" text.
- **Fix**: If the user wants strict conformance, add the transitions to the reduced-motion block and
  swap `.spin` for a static ring.
- **Decision**: SKIPPED — matches what P1 recorded; no continuous decorative motion is left.

### F11 — 29 manual verification rows are still open

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: context/changes/ui-redesign/plan.md (Progress, rows 1.7–1.9, 2.9–2.15, 3.8–3.14, 4.11–4.18, 5.8–5.11)
- **Detail**: Every automated criterion in all five phases passes and was re-run here. Nothing
  automated can stand in for the rest: pixel fidelity against the artboards at 1440 px, the mobile
  drawer opening and closing on a touch target, the dropzone accepting a dropped `.md`, the focus
  trap in the delete dialog, the reconnect dialog, the error bar, and horizontal overflow at ~400 px.
  `dotnet run` is out of scope for this run, so the redesign is **verified structurally but not
  visually**.
- **Fix**: Walk the `design/README.md` route map at 1440 px and ~400 px before merging.
- **Decision**: PENDING — the browser pass is the remaining gate.

## Checked and found sound (no finding)

- **Mobile drawer (plan-review F1).** `.nav-scrollable` is `overflow: visible` on mobile and the
  scroll lives on `.side-nav` (`height: 100%; overflow-y: auto`), so the `left: 100%; width: 100vw`
  scrim is not clipped. The drawer is `position: fixed`, which makes it the containing block for the
  absolutely positioned `::before` whether or not the transform is active, and the scrim is inside
  `.nav-scrollable`, so a tap on it hits the kept inline `onclick`. `.sidebar`'s `z-index: 1030`
  creates a stacking context that still paints above `.main` (unpositioned) and below the modal
  (1050/1055) and the error bar (1060).
- **Scoped CSS.** Every child-component element is reached through `::deep`
  (`.nav-item ::deep .nav-link`, `.auth-art-file ::deep svg`, the whole `.note-preview ::deep` block
  for the `MarkupString` output); nothing tries to style a `NavLink`/`InputText`/`Icon` from a scoped
  sheet without it, and the shared `.brand`/`.logo`/`.auth-head` live in `app.css` as plan-review F4
  required.
- **Bootstrap 5.3.3 mapping.** All six button variants set `--bs-btn-*` for normal/hover/active/
  disabled; `.form-control:focus` restates `background-color`/`color`; `.badge`, `.alert`, `.card`,
  `.modal`, `.modal-backdrop` are overridden at equal specificity from a later sheet.
  `--bs-modal-margin: 16px` sits in the `.modal` block, so the dialog keeps side gutters below 576 px
  where Bootstrap does not cap its width.
- **Narrow widths.** `h1`, `.note-card-title` and `.system-card code` break anywhere; `.crumbs`,
  `.material-title`, `.badge > span`, `.user-name`, `.user-mail`, `.readonly-field > span` and
  `.pane-link` truncate; `.material-row` re-flows into named grid areas below 768 px; `.detail-grid`
  and `.settings-row` collapse below 992 px; `.seg` is `max-width: 100%`. The three breakpoints the
  plan pins (992 / 768 / 641) are the only ones in the sheet.
- **Heading order and focus.** `FocusOnNavigate Selector="h1"` still lands on each page's own `h1`;
  the auth hero headline is a `p`; the not-found `h1`s are first on their pages; pane titles are
  `h2`. `NotFound.razor`'s lone `h3` (no `h1`, no `<PageTitle>`) is unchanged template copy and
  predates this change.
- **Icon-only controls.** `button.btn-close` keeps `aria-label="Zamknij"`; the avatar and the hero
  art are `aria-hidden`; every `Icon` sits beside a text label, so no trimmed `TextContent` moved.

## Triage summary

Fixed: F1, F2, F3, F4, F5, F6 (6) · Skipped: F8, F10 (2) · Accepted: F9 (1) · Reported for the user: F7 (1) · Pending: F11 (1)

**After the fixes**: `dotnet build` clean, `dotnet test` 355 passed, hook check with
`feature/test-plan` merged 387 passed. Commit: `fix(ui-redesign): review follow-ups (p6)`.
