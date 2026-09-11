<!-- PLAN-REVIEW-REPORT -->
# Plan Review: UI redesign — Implementation Plan

- **Plan**: context/changes/ui-redesign/plan.md
- **Mode**: Deep (non-interactive run — triage decided by the reviewer, fixes applied to plan.md)
- **Date**: 2026-09-11
- **Verdict**: REVISE → SOUND after the applied fixes
- **Findings**: 2 critical · 4 warnings · 1 observation

## Verdicts

| Dimension | Verdict (before fixes) |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | FAIL |
| Plan Completeness | WARNING |

## Grounding

Paths 9/9 ✓ (`Components/App.razor`, `Layout/MainLayout.razor(.css)`, `Layout/NavMenu.razor(.css)`, `Notes/NoteEditor.razor(.css)`, `Pages/Materials/{Detail,Import,Index}.razor`, `Pages/Notes/{Detail,Index}.razor`, both detail `.razor.css`, `Account/{Login,Register,Profile,Logout}.razor`; `Components/Shared/` and `AuthLayout` correctly absent) · symbols 8/8 ✓ (`AuthCookie.DisplayNameOrEmail` → `string?`, `ClaimTypes.Email` claim, `FocusOnNavigate Selector="h1"`, `NotFound @layout MainLayout`, bootstrap.css 5.3.3 `[data-bs-theme=dark]` at :128, `.alert-danger` vars at :4932, `--bs-modal-footer-bg` at :5491, `.modal-backdrop` `--bs-backdrop-*` at :5559) · brief↔plan ✓ · Progress↔Phase ✓ (5/5 headings, 6+3 / 8+7 / 7+7 / 10+8 / 7+4 criteria, no checkboxes outside Progress).

Verified against `feature/test-plan` (read-only `git show`): every hook in the plan's table matches the tests — `NoteEditorTests` (first `button.btn-primary` trimmed text, first `div.form-text` counter, `div.alert-danger`), `MaterialsDetailTests` (`Button()` = `.Single()` over all `<button>` by trimmed text, `cut.Find("h1").TextContent` exact, `div.alert-warning` removal), `AccountLogin/RegisterTests` (`#email`, `#password` value, one `form`, `div.alert-danger`), `MaterialsImportTests` (`#title` `maxlength`). Icons carry no text and the planned wrappers keep every trimmed text intact. `feature/test-plan` touches `Import.razor` only on line 89 of the title block (plus `@code`), so editing the wrapper on line 87 with line 88 untouched merges cleanly. `feature/ui-redesign` = `main` = merge base (4c8279d), so `git diff main` is a valid baseline. No CSP exists, so the Google Fonts link loads. Bootstrap mapping checked: app.css loads after Bootstrap, so equal-specificity `.btn-*` / `.alert` / `.modal` / `.modal-backdrop` / `.card` variable overrides win; `a` colour is driven by `--bs-link-color-rgb` (mapped); `.text-secondary` by `--bs-secondary-rgb` (mapped); `.form-control:focus` is hard-coded and overridden directly (see F3).

## Findings

### F1 — Mobile drawer scrim is clipped by the drawer's own overflow

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots (contradiction with documented current state)
- **Location**: Phase 2 §2 Navigation (CSS), D34, Critical Implementation Details → Mobile drawer
- **Detail**: The plan makes `.nav-scrollable` `position: fixed; overflow-y: auto` and draws the scrim as its `::before` at `left: 100%; width: 100vw`. `overflow-y: auto` forces `overflow-x: auto` — the very behaviour the existing `.nav` comment in `NavMenu.razor.css` (kept by the plan) documents — and a fixed element is the containing block of its absolutely positioned pseudo-element, so the scrim is clipped to the 280 px drawer and becomes horizontally scrollable content inside it. No dimmed page, no tap-to-close, and a sideways scrollbar in the drawer: criterion 2.12 would fail.
- **Fix**: On mobile `.nav-scrollable` keeps `overflow: visible`; the scroll moves to `nav.side-nav` (`height: 100%; overflow-y: auto`). Desktop is unchanged (the scrim never renders there). The scrim stays part of `.nav-scrollable`, so the existing inline `onclick` still closes the drawer — no new element, no JS.
  - Strength: keeps D34's no-new-element/no-JS promise; one CSS property moves.
  - Tradeoff: none significant; the `.nav` nowrap comment's reasoning still holds on desktop.
  - Confidence: HIGH — CSS overflow clipping of positioned descendants is specified behaviour, and the codebase already hit its x-axis side effect in S-07.
  - Blind spot: iOS Safari momentum scrolling inside the inner nav not checked (manual 2.12 covers it).
- **Decision**: FIXED (applied to Phase 2 §2, D34 and Critical Implementation Details)

### F2 — Four static-grep criteria fail on the current code

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: criteria 1.4, 4.4, 5.5 (`grep -rn "MarkupString" Components`), 4.9 (`grep -rn "data-bs-dismiss\|data-bs-toggle\|IJSRuntime" Components`), 4.8 (`grep -rn "btn-primary" …NoteEditor.razor`)
- **Detail**: The plan tells the implementer to keep the guardrail comments, but those comments contain the grepped words: "never MarkupString" in `Materials/Index.razor:38`, `Notes/Index.razor:49`, `Notes/Detail.razor:125`, "The only MarkupString" in `NoteEditor.razor:30`; "never data-bs-dismiss" in `Materials/Detail.razor:85` and "data-bs-toggle's" in `Notes/Detail.razor:101`. Verified: today the MarkupString grep hits 4 files and the data-bs grep hits 2. The implementer would either fail the criterion or delete the comments. 4.8 has the same exposure once the NoteEditor header comment is rewritten.
- **Fix**: Target markup, not prose: `grep -rn "(MarkupString)" Components` (hits only `NoteEditor.razor:34`), `grep -rnE 'data-bs-(dismiss|toggle)="|IJSRuntime' Components`, and `grep -n 'class="[^"]*btn-primary' Components/Notes/NoteEditor.razor` = one line.
- **Decision**: FIXED (body criteria and Progress items 1.4, 4.4, 4.9, 5.5 updated; 4.8 body updated, Progress title unchanged)

### F3 — Button active/disabled values and input focus background left for the implementer to work out

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness (Bootstrap 5.3.3 mapping)
- **Location**: Phase 1 §2 — Buttons, Forms & validation
- **Detail**: The contract says variants set `--bs-btn-*` "for normal/hover/active/disabled", but the table gives normal and hover only. Bootstrap 5.3.3 paints `:active` from `--bs-btn-active-*` and `:disabled` from `--bs-btn-disabled-*`, both still holding Bootstrap blue/grey unless overridden — a pressed or disabled primary button would flash the old palette. Separately, "`:focus` … background unchanged" is not enough: `bootstrap.css:2143` `.form-control:focus` (specificity 0,2,0) sets `background-color: var(--bs-body-bg)`, so a focused field would jump from `--field` to `--bg`.
- **Fix**: Active = the hover values, disabled = the normal values (dimming via `--bs-btn-disabled-opacity`), no glow on disabled primary/danger; restate `background-color: var(--field); color: var(--tx)` in `.form-control:focus`.
- **Decision**: FIXED

### F4 — Brand styles live in NavMenu's scoped sheet but AuthLayout renders the same brand

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 §2 (NavMenu CSS) and §4 (AuthLayout)
- **Detail**: `.brand` / `.logo` are specified only in `NavMenu.razor.css`, while AuthLayout uses "the same brand anatomy". Scoped CSS cannot reach AuthLayout's elements, so the auth brand renders unstyled or gets a silent copy — the duplication the plan itself cites as the reason for global primitives (`.source-content`).
- **Fix**: Put `.brand` / `.logo` in `app.css` Primitives; both layouts use them.
- **Decision**: FIXED

### F5 — Shell: mobile pages always scroll 56 px, and `px-4` would override the content padding

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §1 Main layout
- **Detail**: On mobile `.page` is a column: 56 px sticky top bar + `.main { min-height: 100vh }` = 100vh + 56 px, so even an empty page scrolls. Also today's markup is `article.content px-4`; the contract lists `article.content` but does not say to drop `px-4`, whose `!important` padding would beat the planned `.content` 48/16 px padding.
- **Fix**: `.main` `min-height: calc(100vh - 56px)` on mobile, `100vh` on desktop; drop `px-4` explicitly.
- **Decision**: FIXED

### F6 — Hook-check recipe breaks after an interrupted run; deletions have no exact command

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Critical Implementation Details → Hook-check worktree recipe; Phase 4 §2–§3 files
- **Detail**: If `dotnet test` times out or the shell dies, `../10xDevs3-hookcheck` stays registered and the next `git worktree add` fails. The recipe runs four times (P2–P5), so this is likely to happen once. The two `.razor.css` deletions say "`git rm` on the Mac" without the command. The edit shell cannot delete, so a cold implementer needs the exact line.
- **Fix**: Start the recipe with `git worktree remove --force ../10xDevs3-hookcheck 2>/dev/null; git worktree prune`; spell out `cd /Users/mati/Projects/10xDevs3 && git rm <path>` for both files.
- **Decision**: FIXED

### F7 — Small gaps a cold implementer would have to guess

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phases 1–4 (various)
- **Detail**: (a) the materials-row mobile grid names areas but never assigns `grid-area` to the cells; (b) `.field`'s margin reset lists `.auth-form`, a class no form ever gets (the EditForm keeps "unchanged attributes"); (c) the avatar initial is "first character upper-cased" with no expression — `name[0]` splits an emoji/non-BMP first character of a 200-char display name; (d) the streaming `<pre>` gains a conditional caret, and Razor preserves whitespace inside `<pre>`, so a line break before `@if` would appear in the note; (e) the notes empty-state title silently drops the period from the existing sentence (copy is locked 1:1); (f) the `app.css` rewrite does not say what happens to `.blazor-error-boundary`.
- **Fix**: (a) assign the four areas; (b) drop `.auth-form` from the list (auth fields keep the 18 px margin, the artboard's gap); (c) rune-based `Initial()` helper; (d) keep `@note@if (…){<span class="caret"></span>}` on one line; (e) keep the period; (f) drop `.blazor-error-boundary` (no `ErrorBoundary` exists).
- **Decision**: FIXED

## Checked and found sound (no finding)

- Security guardrails: the plan keeps the single `MarkupString`, `<pre>` source text, and plain interpolation for titles and file names (incl. the new file-name badge and avatar). The `Icon` component uses literal SVG with no `MarkupString` and a string `stroke-width`, so it stays culture-safe. Static SSR pages gain no `@onclick`, and `#blazor-error-ui` relies on `blazor.web.js` wiring by class.
- Blazor: `@layout AuthLayout` on the two static pages overrides `DefaultLayout` (precedent `NotFound.razor`), and `_Imports` already has `Components.Layout`. The hero title is a `p`, so `FocusOnNavigate` still lands on the page `h1`. `::deep` is used correctly for `NavLink`, `Icon` and the `MarkupString` output. The `InputFile` comes first and unconditionally inside the dropzone, so it is never recreated, and both panels keep `d-none`. The modal keeps its sentinels, `@ref`s and `@onkeydown`, and its z-index (1055) sits above the sidebar stacking context (1030). Render modes are untouched.
- Hooks: one generate button (pane head, every state), the save button stays the first `btn-primary` (toggles become `.seg`), the counter is the first and only `div.form-text`, the not-found `h1` is exact and first, and every button text stays unique.
- Phases each leave the app consistent; `dotnet build` / `dotnet test` run from the repo root (the solution includes the test project); `dotnet` is on the Mac PATH (`/opt/homebrew/bin`).

## Triage summary

Fixed: F1, F2, F3, F4, F5, F6, F7 (7) · Skipped: — · Accepted: — · Dismissed: —

Verdict after fixes: **SOUND**. Left open, by design: visual fidelity and the transitional look of P1 pages (e.g. the old confirmation alert's buttons stretching until P4) are judged in each phase's manual pass.
