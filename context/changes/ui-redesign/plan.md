# UI redesign — Implementation Plan

## Overview

Restyle the whole 10xNotes app to the Claude Design mockups in `design/*.dc.html`: dark only, modern SaaS, one violet accent (`#8C7BFF`), Geist + Geist Mono. Bootstrap 5.3.3 stays and is re-themed (`data-bs-theme="dark"` plus `--bs-*` and per-component variable overrides); a small `Icon` Razor component replaces the data-URI nav icons; layouts, lists, forms, detail panes, the editor and the delete dialog take the mockups' anatomy.

It is a **visual change only**. Services, data access, render modes, validation, routes, navigation structure and all Polish copy stay as they are (except the small presentational additions the design README allows). Every markup hook the `feature/test-plan` bUnit suite depends on survives — where the mockup's anatomy would break one, the hook wins and this plan says how the look is reached anyway.

## Current State Analysis

The research (`context/changes/ui-redesign/research.md`) mapped the surface; the parts that shape this plan:

- **Styling is almost all scaffold.** One global sheet (`wwwroot/app.css`, 59 lines of template defaults: Helvetica, blue `.btn-primary`, white/blue focus ring, green/red validation outlines, unused `.darker-border-checkbox` / `.form-floating`), six scoped sheets, stock Bootstrap **5.3.3** CSS, **no Bootstrap JS**, no icon font. Load order `bootstrap.min.css` → `app.css` → `10xnotes.styles.css` (`Components/App.razor:9-11`), so `app.css` wins over Bootstrap at equal specificity and scoped CSS wins last.
- **Bootstrap buttons ignore theme colours.** 5.3.3 hard-codes `--bs-btn-*` per variant (`bootstrap.css:3056-3060`), so re-theming `--bs-primary` alone leaves blue buttons. Alerts, forms, modals, cards and list groups read their own `--bs-<component>-*` variables. Both layers are needed.
- **Shell.** `MainLayout.razor:3-23` = `div.page > div.sidebar > NavMenu` + `main > div.top-row` (English "About" link) + `article.content`, and `#blazor-error-ui` (English copy, `.reload`, `.dismiss` 🗙). Breakpoint 641 px (`MainLayout.razor.css:39-77`). `NavMenu.razor` uses a CSS-only checkbox toggler (`:9`) whose `~` sibling selector reaches `.nav-scrollable` (`:11`, `NavMenu.razor.css:124-130`), an inline DOM `onclick` that closes the menu after a tap, `NavLinkMatch.All` on `/` and `materials` (`:16,25`), a `.nav-user` label that truncates only because `.nav { flex-wrap: nowrap }` (`NavMenu.razor.css:100-122`), and sign-out as a link to `/logout` (`:49-55`).
- **Render modes are fixed by necessity:** Notes/Index, Materials/Index, Login, Register, Logout, Profile, Error, NotFound are static SSR (no circuit — any `@onclick` there is dead); Import, Materials/Detail, Notes/Detail are InteractiveServer.
- **Auth layout is a one-line opt-in.** `Routes.razor:3` sets `DefaultLayout`; a page-level `@layout` overrides it (precedent `NotFound.razor:3`). `FocusOnNavigate Selector="h1"` (`Routes.razor:8`) focuses the first `h1`.
- **Guardrails in markup:** source material as text in `<pre>` (`Materials/Detail.razor:73`, `Notes/Detail.razor:62`); the single `MarkupString` (`NoteEditor.razor:34`); read-only streaming `<pre>` (`Materials/Detail.razor:151`); hand-rolled modal with focus sentinels and `@ref` buttons (`Notes/Detail.razor:103-159`); Blazor-dismissed alert (`Materials/Detail.razor:89-93`); `d-none` import panels (`Import.razor:56,79`); functional `maxlength`s; `disabled` bindings on every mutating control.
- **No component tests on this branch.** The bUnit suite lives on `feature/test-plan` (not merged). `dotnet test` here only covers services and `DataAccessBoundaryTests`; hook preservation is verified by merging `feature/test-plan` into a throwaway worktree.

## Desired End State

Every route renders in the dark design: 248 px sidebar with brand, icon nav, user block (initial + name + e-mail) and footer links; a mobile top bar with a CSS-only slide-out drawer; login/register on a two-column auth layout with the hero; notes as a card grid; materials as a table-like list with source and note-status badges; import as a card with a segmented switch, a counter and a dropzone around the native file input; material/note detail as two panes (source | note) with breadcrumbs, pane heads, the restyled editor and the restyled delete dialog; profile as settings rows with a danger zone; logout/error/not-found as simple cards. `lang="pl"`, Geist fonts, violet focus rings, visible disabled states.

Verified by: `dotnet build` and `dotnet test` on `feature/ui-redesign`; the `feature/test-plan` bUnit suite passing in a throwaway worktree after P2, P3, P4 and P5; the static greps listed per phase; and a visual pass with `dotnet run` against each named route at ~1440 px and ~400 px, compared with the artboards.

### Key Discoveries:

- `bootstrap.css:3056-3060` — button colours are per-variant `--bs-btn-*`; `bootstrap.css:4932-4935` — alerts read `--bs-danger-bg-subtle` etc.; the `[data-bs-theme=dark]` block (`bootstrap.css:~128`) brings `color-scheme: dark`, so native scrollbars and the file input follow.
- `NavMenu.razor:9-11` — the checkbox must stay the **preceding sibling** of `.nav-scrollable`; the inline `onclick` on `.nav-scrollable` is what closes the drawer after a tap, on static pages too.
- `Routes.razor:8` — `FocusOnNavigate` picks the first `h1`: the auth hero headline must not be an `h1`, and nothing may put an `h1` before a page's own.
- `Materials/Detail.razor:107-110` — the one "Generuj notatkę" button; `MaterialsDetailTests.cs:396-414` (on `feature/test-plan`) finds buttons by trimmed text with `.Single()` over all `<button>`s.
- `NoteEditor.razor:50-52` — the counter `@Content.Length / @NoteValidator.MaxContentLength znaków` must stay the first `div.form-text` in the component and unformatted (`NoteEditorTests.cs:173-177`).
- `Import.razor:87-95` vs `feature/test-plan` — that branch rewrites `Import.razor:89` (the `#title` `InputText`, adding `maxlength`); editing the lines touching it guarantees a merge conflict.
- `Auth/AuthCookie.cs:72-73,144` — `ClaimTypes.Email` and `DisplayNameOrEmail(principal)` feed the sidebar user block with no database read.
- Scoped CSS (`b-<hash>` attributes) is applied only to elements a component renders itself — **not** to the output of child components (`NavLink`, `InputText`, `InputFile`, `ValidationMessage`, `EditForm`, and the new `Icon`). Existing precedent: `NavMenu.razor.css` reaches `NavLink`'s `<a>` via `::deep`.
- `context/foundation/lessons.md` — "Give the markup its state before the first await": when restructuring Profile/Import markup, leave the `@code` field initializers exactly as they are.

## What We're NOT Doing

- No change to services, `Data/`, validators, `@code` logic, render modes, routes, `FormName`s, or `Program.cs`.
- No new features: no quota meter, no search, no counts, no drag-and-drop JavaScript, no light theme / theme toggle, no accent picker.
- No copy rewrites. Razor copy wins; only the README-allowed additions are added (page subtitles, breadcrumbs, source/status badges, auth hero copy, profile section hints, the import dropzone line). English copy in ReconnectModal, `#blazor-error-ui`, Error and NotFound stays English.
- No Bootstrap JS, no new JS files, no `IJSRuntime` calls, no `@onclick` on static-SSR pages.
- No change to `feature/test-plan`, no merge of it into `feature/ui-redesign`, no push.
- No self-hosted fonts (Google Fonts `<link>`, locked by the user).
- Logout, Error and NotFound do **not** get the auth layout.
- No `ErrorBoundary`, no new routes, no changes to `RedirectToLogin`.

## Implementation Approach

Global first, then outward-in: **P1** lays the token layer, the Bootstrap mapping, the shared primitives and the `Icon` component in `app.css` / `Components/Shared/` — the app turns dark without any page change. **P2** rebuilds the shell (MainLayout, NavMenu, mobile drawer, ReconnectModal, `#blazor-error-ui`) and adds `AuthLayout` with the restyled Login/Register. **P3** restyles the list and form pages (notes grid, materials list, import). **P4** restyles the two detail pages and the shared `NoteEditor` together (they share panes and the editor). **P5** does Profile, Logout, Error, NotFound and a cross-page narrow-width pass.

Shared primitives live in `wwwroot/app.css` (scoped CSS cannot be shared between components — the duplicated `.source-content` rules are the proof). Only layout components (MainLayout, NavMenu, AuthLayout, ReconnectModal) and `NoteEditor`'s preview typography keep scoped sheets.

Mockup CSS is **never lifted verbatim**: its class names collide with Bootstrap (`.nav`, `.nav-item`, `.btn`, `.card`, `.badge`, `.alert`, `.modal`, `.fade`, `.label`) and its `.input` is a wrapper div, not the control. Values are mapped onto Bootstrap classes (`.btn-*`, `.form-control`, `.alert-*`, `.card`, `.badge`, `.modal-*`) and onto a handful of new, non-colliding primitives (`.page-head`, `.crumbs`, `.meta`, `.seg`, `.pane*`, `.icon-tile`, `.empty-state`, …).

Each phase ends with one commit on `feature/ui-redesign`: `<type>(ui-redesign): <subject> (pN)`, no push. The P1 commit also carries this change folder (`context/changes/ui-redesign/`), which `/10x-plan` leaves uncommitted.

## Decisions

Locked by the user (`design/README.md`) or by the orchestrator, recorded here so the implementer does not re-open them:

| # | Decision | Source |
|---|---|---|
| D1 | Dark only; violet accent `#8C7BFF`; tokens exactly as README/System artboard. | README |
| D2 | Geist 400/500/600/700 + Geist Mono 400/500 via Google Fonts `<link>` in `App.razor`, with fallback stacks. | README + orchestrator |
| D3 | Keep Bootstrap 5.3.3; `data-bs-theme="dark"` on `<html>`; map `--bs-*` theme vars **and** per-component `--bs-btn-*` / `--bs-alert-*` / `--bs-modal-*` / `--bs-card-*` vars. | README + research |
| D4 | Icons = inline SVG through one Razor component (24 grid, stroke 1.7 default, `aria-hidden`, no `<title>`, no text); no icon font, no emoji. | README |
| D5 | Razor copy wins over mockup copy, except the allowed presentational additions (subtitles, breadcrumbs, badges, auth hero, profile section hints, dropzone line). | research D2, confirmed |
| D6 | Materials/Detail keeps exactly **one** "Generuj notatkę" button, in the note pane head, in every state; the empty state shows icon + existing hint, no second CTA. | research D3, confirmed |
| D7 | Notes/Detail "Usuń notatkę" moves to the page header (right side of `.page-head`), styled danger-soft; the markup comment is updated (it is now far from "Zapisz zmiany", which sits in the note pane's footer). | research D4, confirmed |
| D8 | Only Login and Register get `AuthLayout`; Logout, Error, NotFound stay on MainLayout. | research D5, confirmed |
| D9 | Drop MainLayout's "About" top row; mobile gets a logo + hamburger top bar instead. | research D6, confirmed |
| D10 | `#blazor-error-ui` moves from MainLayout to `App.razor`, its CSS to `app.css`, so both layouts have it. | research D7, confirmed |
| D11 | Keep the 641 px sidebar breakpoint; detail panes stack below 992 px (Bootstrap `lg`), source first. | research D9, confirmed |
| D12 | Import file mode adopts the dropzone look as pure CSS around the existing `InputFile` (`#file` kept): the native input is stretched transparently over the zone, so click and native drop both work with no JS. | orchestrator |
| D13 | Both list pages get a header "Dodaj materiał" primary link to `/materials/import`. | orchestrator |
| D14 | `<html lang="en">` → `lang="pl"`. | orchestrator |
| D15 | Character counters keep their current formatting and text (NoteEditor unformatted, Import `N0`); only restyled (mono, `tx-3`). | orchestrator |
| D16 | Every test hook in research §4 is kept; the hook wins over mockup anatomy. | orchestrator |
| D17 | Mobile: CSS-only top bar + slide-out drawer (the existing checkbox toggler); detail panes stack. | README + orchestrator |
| D18 | Commit per phase, Conventional Commits `(pN)`, no push; per-phase `dotnet build` + `dotnet test`; hook check in a throwaway worktree with `feature/test-plan` merged in for phases touching tested components. | orchestrator |

Decided in this plan (non-interactive run — no user questions were asked):

| # | Decision | Why |
|---|---|---|
| D19 | Five phases (theme → shell+auth → lists+import → detail+editor → account/system+responsive). | Each leaves the app consistent; detail pages and the editor must land together; auth pages need the new layout and the form anatomy at once. |
| D20 | `Icon` lives in `Components/Shared/Icon.razor` with an `IconName` enum in `Components/Shared/IconName.cs`; `_Imports.razor` gets `@using _10xnotes.Components.Shared`. | Compile-time icon names; `Shared/` is the conventional home and keeps it out of `Layout/`. |
| D21 | Icon paths are emitted as literal Razor markup in a `switch`, **never** via `MarkupString`. | Keeps `NoteEditor.razor:34` the only `MarkupString` in the project (security guardrail). |
| D22 | Mockup names map to Bootstrap variants in markup: primary → `btn-primary`, secondary → `btn-secondary`, ghost → `btn-outline-secondary`, danger → `btn-danger`, danger-soft → `btn-outline-danger`, warn-confirm → `btn-warning`. No new `btn-*` class names. | Fewer names; Bootstrap classes already carry `:disabled`, focus and active plumbing. |
| D23 | Mode toggles (NoteEditor Edycja/Podgląd, Import Wklej tekst/Plik .md) become `.seg` / `.seg-item` buttons — **not** `.btn`. | Mockup look; and guarantees the editor's first `button.btn-primary` stays the save button. |
| D24 | Alerts get a leading `Icon` in markup (`Check` for success, `Alert` for danger/warning) and keep their element (`div.alert.alert-*`), `role` and text; message text sits in a `<span>`/`<div>` after the icon. | README icon rule; `TextContent.Trim()` is unchanged because an icon has no text. |
| D25 | Notes list = `ul.note-grid > li.card.note-card`, title link made card-wide with Bootstrap's `.stretched-link`; the "Materiał źródłowy" link is lifted above it (`position:relative; z-index:2`). | Whole card clickable like the mockup's hover card, without nesting links. |
| D26 | Materials list = `ul.material-list` of CSS-grid rows inside a `.card`, with a column header row `div.material-head aria-hidden="true"` ("Materiał / Źródło / Dodano / Notatka"). The date cell keeps the Razor verb visible ("Wklejono {date}" / "Zaimportowano {date}"); the file name moves into the source badge, so "z pliku …" is carried by the badge. | Razor copy wins (D5) while matching the table look. |
| D27 | Material detail meta = source badge (file name in mono, or "Wklejony tekst") + `.meta` "Wklejono {date}" / "Zaimportowano {date}" (the "z pliku <code>…</code>" clause is carried by the badge, trailing period dropped). | Same rule as D26. |
| D28 | Generate button in the pane head is `btn-sm`, `btn-primary` while there is no draft and `btn-secondary` once a draft exists (mockup); busy state = `.spin` + "Generuję…". | Mockup; class choice is a one-line presentational expression. |
| D29 | Notes/Detail `savedMessage` renders as a pane-head `span.badge.st-ok role="status"` instead of `div.alert-success`; text unchanged (it comes from `@code`). | Mockup; no test reads it. |
| D30 | Profile primary buttons become `btn-secondary` ("Zapisz nazwę", "Zmień hasło") as in the mockup; delete stays `btn-danger`. The delete field keeps label "Hasło" and the confirmation paragraph. | Mockup look; Razor copy wins. |
| D31 | Profile e-mail becomes a read-only display (`div.readonly-field` with a mail icon), not an input. | Mockup; there is no control to submit. |
| D32 | Import dropzone shows the chosen file's name (`selectedFile.Name`, plain interpolation) as a mono badge under the prompt once a file is picked, because the transparent input hides the browser's own label. | Keeps the user's feedback that D12 would otherwise remove; reads an existing field, no logic added. |
| D33 | Materials/Detail streaming `<pre>` gets a `.stream-bar` above it and a `span.caret` at its end only while `isGenerating`; the `<pre>` stays read-only text. | Mockup; decoration only. |
| D34 | Mobile drawer scrim is a `::before` pseudo-element of `.nav-scrollable`, so tapping it hits the existing inline `onclick` and closes the drawer — no new element, no new JS. On mobile the drawer scrolls on `.side-nav`, not on `.nav-scrollable`, so the pseudo-element is not clipped. | CSS-only (D17). |
| D35 | Shared primitives go in `wwwroot/app.css`; `Pages/Notes/Detail.razor.css` and `Pages/Materials/Detail.razor.css` are removed (their rules move into the `.src`/pane primitives). | Scoped CSS cannot be shared; ends the duplicated `.source-content`. |
| D36 | The English `title="Navigation menu"` on the toggler, ReconnectModal text, `#blazor-error-ui` text, Error and NotFound copy stay as they are. | 1:1 copy rule; out of visual scope. |

## Critical Implementation Details

**Test hooks the redesign must not break** (bUnit on `feature/test-plan`; tests render pages without layouts and assert semantically, never with `MarkupMatches`, so adding classes/wrappers/scoped attributes is safe):

| Component | Must keep |
|---|---|
| `NoteEditor` | `#note-title` and `#note-content` with `maxlength`, `disabled`, `value`, `@onchange`; the **first** `button.btn-primary` is save and its trimmed text is exactly the label (no text-bearing child); the **first** `div.form-text` is the counter, a `div`, text `"{Length} / {Max} znaków"` unformatted; `div.alert-danger` trimmed text equals the message. |
| `Materials/Detail` | `#note-title`, `#note-content`; `div.alert-warning` containing the confirmation question, removed after "Anuluj"; first `div.alert-danger`; `h1` text **exactly** `Nie znaleziono materiału` (no whitespace, no child, no earlier `h1`); every `<button>` text unique among all buttons: "Generuj notatkę"/"Generuję…", "Zapisz notatkę", "Zastąp zapisaną notatkę", "Zastąp notatkę", "Generuj mimo to", "Anuluj" — buttons stay `<button>`. |
| `Login` / `Register` | `#email`, `#password` (value not echoed), exactly one `form`, `div.alert-danger` trimmed text. |
| `Import` | `#title` (and its `maxlength` once test-plan merges). |

**Scoped CSS does not reach child components.** `Icon`, `NavLink`, `InputText`, `InputFile`, `InputTextArea`, `ValidationMessage` and `EditForm` render elements without the parent's `b-<hash>` attribute. Style them from `app.css`, or from a scoped sheet through `::deep` under an element the component renders itself (e.g. `.nav-item ::deep svg`).

**Icon rendering.** `stroke-width` must be culture-invariant — take it as a `string` (default `"1.7"`), never format a `double` (on a `pl-PL` machine `1.7` renders `1,7` and the SVG ignores it). The component renders no `<title>` and no text node, sets `aria-hidden="true"` and `focusable="false"`, and uses no `MarkupString`.

**Mobile drawer.** `.navbar-toggler` must remain the immediately preceding sibling of `.nav-scrollable` (the `~` selector). A `transform` on `.nav-scrollable` makes it the containing block for fixed-position descendants — position the scrim pseudo-element `absolute` to the drawer's right (`left: 100%; width: 100vw`) rather than `fixed`, and never give `.nav-scrollable` an `overflow` other than `visible` on mobile — an `overflow-y: auto` there clips that pseudo-element (Phase 2 §2; review F1).

**Import merge seam.** In `Import.razor`, leave byte-identical the three lines `<label for="title" …>`, `<InputText id="title" …/>` and the `<div class="form-text">` that follows it (`Import.razor:88-90`). `feature/test-plan` rewrites line 89; touching an adjacent line makes the hook-check merge conflict. Restyle that block through CSS and through its wrapper line (`:87`) only.

**Hook-check worktree recipe.** The worktree checks out the *committed* branch tip, so run it after the phase commit; if it fails, fix the markup and `git commit --amend` the phase commit (nothing is pushed) before starting the next phase. Mac shell, pass a generous `timeout` (e.g. 600 s):

```bash
cd /Users/mati/Projects/10xDevs3
git worktree remove --force ../10xDevs3-hookcheck 2>/dev/null; git worktree prune   # leftovers from an interrupted run
git worktree add --detach ../10xDevs3-hookcheck feature/ui-redesign
cd ../10xDevs3-hookcheck && git merge --no-commit --no-ff feature/test-plan
dotnet test
cd /Users/mati/Projects/10xDevs3 && git worktree remove --force ../10xDevs3-hookcheck
```

If the merge still conflicts in `Components/Pages/Materials/Import.razor`, resolve inside the worktree only: take the `feature/ui-redesign` side and add `maxlength="@MarkdownImportValidator.MaxTitleLength"` to `InputText#title` (and accept test-plan's `StringLength` change in `@code`), then run the tests. Any other conflict is a sign the phase touched something it should not — stop and report. Never commit in the worktree.

**Blazor boolean attributes.** `aria-pressed="@bool"` drops the attribute when false; render ARIA state as the strings `"true"`/`"false"`.

---

## Phase 1: Theme foundation — tokens, Bootstrap mapping, primitives, Icon

### Overview

Everything global, nothing per page: the host page goes dark and Polish, `app.css` is rewritten into tokens + Bootstrap mapping + component overrides + shared primitives, and the `Icon` component exists. Existing pages keep their markup and simply render in the new theme; the old gradient sidebar and light top row get a two-line stopgap so the phase ships coherent.

### Changes Required:

#### 1. Host page

**File**: `Components/App.razor`

**Intent**: Turn on the dark theme, fix the document language, and load the fonts before any stylesheet that names them.

**Contract**: `<html lang="pl" data-bs-theme="dark">`. In `<head>`, before the Bootstrap link: `<meta name="theme-color" content="#0A0B0F" />`, `<link rel="preconnect" href="https://fonts.googleapis.com" />`, `<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />`, `<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600;700&family=Geist+Mono:wght@400;500&display=swap" />`. Stylesheet order after that is unchanged (bootstrap → app.css → scoped bundle). Nothing else changes in this phase (`#blazor-error-ui` moves here in P2).

#### 2. Global stylesheet

**File**: `wwwroot/app.css` (full rewrite; delete the template rules `.darker-border-checkbox`, `.form-floating …`, the white/blue focus ring, `.btn-primary` blue, `a, .btn-link` blue, `.content { padding-top }`, the green `.valid.modified` outline, and the `.blazor-error-boundary` rules — the app has no `ErrorBoundary`)

**Intent**: One ordered sheet that (a) declares the design tokens, (b) maps Bootstrap's theme onto them, (c) restyles each Bootstrap component the app uses, and (d) defines the non-colliding primitives later phases compose pages from. Sections in this order, each with a banner comment: Tokens · Bootstrap theme · Base · Buttons · Forms & validation · Alerts · Cards & list groups · Modal · Primitives · Error UI (P2) · Responsive.

**Contract — tokens** (on `:root`; names are the mockups', so values can be lifted 1:1 from the `.dc.html` inline styles):

```css
:root {
  --accent: #8C7BFF; --on-accent: #0B0B12;
  --bg: #0A0B0F; --side: #0C0D12; --panel: #111319; --panel-2: #161922; --raise: #1C202B; --field: #0D0F14;
  --line: #1F2330; --line-2: #2B3040;
  --tx: #ECEEF3; --tx-2: #A2A8B7; --tx-3: #697083;
  --ok: #3ECF8E; --warn: #F2B84B; --bad: #F07474;
  --accent-soft: color-mix(in oklch, var(--accent) 14%, transparent);
  --accent-line: color-mix(in oklch, var(--accent) 38%, transparent);
  --font-sans: 'Geist', ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif;
  --font-mono: 'Geist Mono', ui-monospace, 'SF Mono', Menlo, Consolas, monospace;
  --sidebar-w: 248px; --control-h: 36px; --control-h-sm: 30px; --control-h-lg: 42px; --input-h: 40px;
}
```

**Contract — Bootstrap theme mapping** (selector `:root, [data-bs-theme=dark]`, so it beats Bootstrap's own dark block by source order):

| Bootstrap variable | Value |
|---|---|
| `--bs-font-sans-serif` / `--bs-body-font-family` | `var(--font-sans)` |
| `--bs-font-monospace` | `var(--font-mono)` |
| `--bs-body-font-size` / `--bs-body-line-height` | `14px` / `1.5` |
| `--bs-body-color` / `--bs-body-color-rgb` | `var(--tx)` / `236,238,243` |
| `--bs-body-bg` / `--bs-body-bg-rgb` | `var(--bg)` / `10,11,15` |
| `--bs-emphasis-color`, `--bs-heading-color` | `var(--tx)` |
| `--bs-secondary-color` | `var(--tx-2)` |
| `--bs-tertiary-color` | `var(--tx-3)` |
| `--bs-secondary-bg` / `--bs-tertiary-bg` | `var(--raise)` / `var(--panel-2)` |
| `--bs-border-color` / `--bs-border-color-translucent` | `var(--line)` / `var(--line-2)` |
| `--bs-border-radius` / `-sm` / `-lg` / `-xl` / `-xxl` | `8px` / `7px` / `10px` / `14px` / `16px` |
| `--bs-primary` / `--bs-primary-rgb` | `var(--accent)` / `140,123,255` |
| `--bs-secondary-rgb` (drives `.text-secondary`) | `162,168,183` |
| `--bs-success` / `-rgb` | `var(--ok)` / `62,207,142` |
| `--bs-warning` / `-rgb` | `var(--warn)` / `242,184,75` |
| `--bs-danger` / `-rgb` | `var(--bad)` / `240,116,116` |
| `--bs-link-color` / `-rgb` | `var(--accent)` / `140,123,255` |
| `--bs-link-hover-color` / `-rgb` | `#A598FF` / `165,152,255` (≈ accent 78% + white) |
| `--bs-code-color` | `var(--tx)` |
| `--bs-form-invalid-color` / `-border-color` | `var(--bad)` |
| `--bs-form-valid-color` / `-border-color` | `var(--ok)` |
| `--bs-focus-ring-color` | `var(--accent-soft)` |
| `--bs-*-bg-subtle`, `--bs-*-border-subtle`, `--bs-*-text-emphasis` for success/warning/danger | `color-mix(in oklch, var(--ok/warn/bad) 8%, var(--panel))`, `color-mix(… 24–26%, transparent)`, `var(--tx)` |

**Contract — base**: `body` font/colour/bg from the tokens, `-webkit-font-smoothing: antialiased`; `a { text-decoration: none }`; `h1 { font-size: 28px; line-height: 1.2; font-weight: 600; letter-spacing: -0.025em; text-wrap: balance; margin: 0 }`; keep `h1:focus { outline: none; }` (needed by `FocusOnNavigate`); `code` = mono 12px on `--raise` with `--line-2` border, radius 5px, padding 1px 6px; `.mono` utility = `var(--font-mono)`; `::selection` on `--accent-soft`.

**Contract — buttons**: `.btn` → `display: inline-flex; align-items: center; justify-content: center; gap: 8px; height: var(--control-h); white-space: nowrap;` with `--bs-btn-padding-x: 14px; --bs-btn-padding-y: 0; --bs-btn-font-size: 13.5px; --bs-btn-font-weight: 500; --bs-btn-line-height: 1; --bs-btn-border-radius: 8px; --bs-btn-focus-box-shadow: 0 0 0 3px var(--accent-soft); --bs-btn-disabled-opacity: .5`. `.btn-sm` → height 30, padding-x 11, 12.5px, radius 7, gap 6. `.btn-lg` → height 42, padding-x 18, 14px, radius 10. Variants set every `--bs-btn-{color,bg,border-color}` for normal/hover/active/disabled:

| Class | Normal | Hover |
|---|---|---|
| `.btn-primary` | bg `--accent`, colour `--on-accent`, border transparent, `box-shadow: inset 0 1px 0 rgba(255,255,255,.28), 0 8px 22px -10px var(--accent)` | bg `color-mix(in oklch, var(--accent) 88%, white)` |
| `.btn-secondary` | bg `--raise`, colour `--tx`, border `--line-2` | bg `color-mix(in oklch, var(--raise) 80%, white 6%)`, border `--line-2` |
| `.btn-outline-secondary` (ghost) | bg transparent, colour `--tx-2`, border transparent | bg `--raise`, colour `--tx` |
| `.btn-danger` | bg `--bad`, colour `#170808`, `inset 0 1px 0 rgba(255,255,255,.25)` | bg `color-mix(in oklch, var(--bad) 88%, white)` |
| `.btn-outline-danger` (danger-soft) | bg `color-mix(in oklch, var(--bad) 9%, transparent)`, colour `--bad`, border `color-mix(in oklch, var(--bad) 28%, transparent)` | bg `… 16% …` |
| `.btn-warning` | bg `--warn`, colour `#1A1204` | bg `color-mix(in oklch, var(--warn) 88%, white)` |

Active (`--bs-btn-active-*`) = the hover values; disabled (`--bs-btn-disabled-*`) = the normal values (the dimming comes from `--bs-btn-disabled-opacity`), and `.btn-primary:disabled, .btn-danger:disabled { box-shadow: none }`. Plus `.spin` (13 px ring, `border: 2px solid color-mix(in oklch, currentColor 25%, transparent); border-top-color: currentColor; animation: rot .8s linear infinite`) for busy buttons, and `@keyframes rot`. `.btn-close` → `--bs-btn-close-opacity: .6`, `--bs-btn-close-focus-shadow: 0 0 0 3px var(--accent-soft)` (Bootstrap's dark filter already inverts the glyph). Legacy `.btn-group` needs nothing (it disappears in P3/P4).

**Contract — forms & validation**: `.form-label` 13px/500 `--tx`, margin-bottom 8px. `.form-control` → background `--field`, border `1px solid var(--line-2)`, radius 9px, colour `--tx`, 14px; `input.form-control` height `var(--input-h)`, padding 0 12px; `textarea.form-control` padding 14px 16px, radius 10px, line-height 1.65, 13.5px; `::placeholder` `--tx-3`; `:focus` border `--accent`, `box-shadow: 0 0 0 3px var(--accent-soft)`, and `background-color: var(--field); color: var(--tx)` restated (Bootstrap's `.form-control:focus` resets the background to `--bs-body-bg` at higher specificity); `:disabled` background `--panel`, colour `--tx-3`, opacity .7; `::file-selector-button` styled like `.btn-secondary` (bg `--raise`, colour `--tx`, border-right `--line-2`). Blazor classes: `.form-control.invalid` (and `.invalid` generally) → border `color-mix(in oklch, var(--bad) 70%, transparent)`, `box-shadow: 0 0 0 3px color-mix(in oklch, var(--bad) 14%, transparent)`; drop the `.valid.modified` outline; `.validation-message` → `--bad`, 12.5px, margin-top 6px. `.form-text` → `--tx-3`, 12.5px, margin-top 8px. `.input-icon` wrapper (position relative; child `svg` absolutely at left 12px, vertically centred, colour `--tx-3`, `pointer-events: none`; the `.form-control` inside gets padding-left 38px).

**Contract — alerts**: `.alert` → `display: flex; gap: 11px; align-items: flex-start;` 13.5px, `--bs-alert-padding-x: 14px; --bs-alert-padding-y: 12px; --bs-alert-border-radius: 10px; --bs-alert-color: var(--tx); --bs-alert-margin-bottom: 16px`; `.alert > svg { flex: none; margin-top: 2px }`; `.alert > :not(svg):not(.btn-close) { flex: 1; min-width: 0 }`; `.alert p:last-child { margin-bottom: 0 }`. Variant tokens: `.alert-success` bg `color-mix(in oklch, var(--ok) 8%, var(--panel))`, border `color-mix(in oklch, var(--ok) 24%, transparent)`, icon `--ok`; `.alert-danger` same with `--bad` 8%/26%; `.alert-warning` same with `--warn` 8%/26%. `.alert-actions` = `display: flex; gap: 8px; flex-wrap: wrap; margin-top: 12px`.

**Contract — cards & list groups**: `.card` → `--bs-card-bg: var(--panel); --bs-card-border-color: var(--line); --bs-card-border-radius: 14px; --bs-card-inner-border-radius: 13px; --bs-card-color: var(--tx)`. `.list-group` (still used until P3) → `--bs-list-group-bg: var(--panel); --bs-list-group-border-color: var(--line); --bs-list-group-border-radius: 14px; --bs-list-group-color: var(--tx); --bs-list-group-item-padding-x: 20px; --bs-list-group-item-padding-y: 14px`.

**Contract — modal**: `.modal` → `--bs-modal-width: 460px; --bs-modal-bg: var(--panel-2); --bs-modal-border-color: var(--line-2); --bs-modal-border-radius: 16px; --bs-modal-inner-border-radius: 15px; --bs-modal-box-shadow: 0 40px 90px -24px rgba(0,0,0,.8); --bs-modal-color: var(--tx); --bs-modal-padding: 24px; --bs-modal-header-border-width: 0; --bs-modal-footer-bg: var(--panel); --bs-modal-footer-border-color: var(--line); --bs-modal-footer-gap: 10px;` `.modal-content` gets `overflow: hidden` and `box-shadow: var(--bs-modal-box-shadow)`; `.modal-footer` padding 14px 24px. `.modal-backdrop` → `--bs-backdrop-bg: rgb(5,6,9); --bs-backdrop-opacity: .68; backdrop-filter: blur(3px)`. Z-indexes stay Bootstrap's (backdrop 1050, modal 1055) — above the P2 sidebar/drawer (≤ 1040).

**Contract — primitives** (values from the artboards; later phases compose pages from these, so define them all now):

| Class | Definition |
|---|---|
| `.page-head` | flex, `align-items: flex-end; justify-content: space-between; gap: 16px 24px; flex-wrap: wrap; margin-bottom: 28px`; `.page-head--detail` → `align-items: flex-start; margin-bottom: 24px`; direct child `div` gets `min-width: 0` |
| `.sub` | `margin: 6px 0 0; color: var(--tx-2); font-size: 14px; max-width: 620px` |
| `.crumbs` | flex, gap 6px, 13px, `--tx-3`, margin-bottom 12px; `a` colour `--tx-2`; last `span` truncates (`min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap`) |
| `.meta` | inline-flex, gap 6px, 12.5px, `--tx-3`; `svg { flex: none }` |
| `.meta-row` | flex, gap 12px, `align-items: center; flex-wrap: wrap; margin-top: 10px` |
| `.badge` (overrides Bootstrap's) | `display: inline-flex; align-items: center; gap: 6px; height: 24px; padding: 0 9px; border-radius: 999px; font-size: 12px; font-weight: 500; line-height: 1; background: var(--raise); color: var(--tx-2); border: 1px solid var(--line-2); white-space: nowrap; max-width: 100%`; `svg { color: var(--tx-3); flex: none }`; `.badge.mono` 11.5px mono; long text truncates in an inner `span` (`overflow: hidden; text-overflow: ellipsis`) |
| `.badge.st-ok` | colour `--ok`, bg `color-mix(in oklch, var(--ok) 9%, transparent)`, border `… 24% …`, gap 7px; `a.badge.st-ok:hover` bg 14% |
| `.badge.st-none` | colour `--tx-3`, transparent bg, border `--line-2`, gap 7px |
| `.dot` | 6×6 circle, `background: currentColor` |
| `.icon-tile` | 34×34, radius 9px, grid centre, bg `--accent-soft`, colour `--accent`, border `1px solid var(--accent-line)`; modifiers `.icon-tile--lg` (52×52, radius 14) and `.icon-tile--xl` (56×56, radius 16, `box-shadow: 0 0 0 6px color-mix(in oklch, var(--accent) 6%, transparent), 0 14px 40px -12px var(--accent)`) |
| `.seg` / `.seg-item` / `.seg-item.on` | `.seg`: inline-flex, gap 2px, padding 3px, bg `--field`, border `1px solid var(--line)`, radius 10px. `.seg-item` is a `<button>` reset (no border/bg, `font: inherit`) + inline-flex, gap 7px, height 30px, padding 0 12px, radius 7px, 13px/500, `--tx-3`; hover `--tx-2`. `.on`: bg `--raise`, colour `--tx`, `box-shadow: inset 0 1px 0 rgba(255,255,255,.05), 0 1px 2px rgba(0,0,0,.45)`, `svg` `--accent`. `:focus-visible` ring `0 0 0 3px var(--accent-soft)` |
| `.empty-state` (on a `.card`) | flex column centred, gap 18px, text-align centre, padding 56px 24px, `border-style: dashed`, bg `color-mix(in oklch, var(--panel) 60%, transparent)`; `.empty-state-title` 17px/600; `p` `--tx-2`, max-width 420px |
| `.detail-grid` | grid, `grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; align-items: start` |
| `.pane` (on a `.card`) | `overflow: hidden; position: relative; min-width: 0`; `.pane--note` border `--line-2` |
| `.pane-head` | flex, `align-items: center; justify-content: space-between; gap: 12px; min-height: 54px; padding: 8px 14px 8px 18px; border-bottom: 1px solid var(--line)` |
| `.pane-title` | flex, gap 9px, 13.5px/600, margin 0 (it is an `h2`), `svg` `--tx-3` |
| `.pane-link` | inline-flex, gap 5px, 12.5px/500, truncates (`max-width: 50%`) |
| `.pane-body` | flex column, gap 16px, padding 18px |
| `.pane-foot` | flex, `justify-content: flex-end; gap: 10px; padding: 14px 18px; border-top: 1px solid var(--line)` |
| `.pane-empty` | flex column centred, gap 20px, padding 48px 32px, text centre, `background: radial-gradient(360px 220px at 50% 42%, var(--accent-soft), transparent 70%)`; `p` max-width 340px, `--tx-2`, line-height 1.6 |
| `.src` (on `<pre>`) | `font-family: var(--font-mono); font-size: 12.5px; line-height: 1.8; color: var(--tx-2); white-space: pre-wrap; word-break: break-word; margin: 0; padding: 18px 20px 56px; max-height: 60vh; overflow-y: auto; background: transparent; border: 0` |
| `.src--live` | colour `--tx` (streaming note) |
| `.pane-fade` | `position: absolute; left: 1px; right: 1px; bottom: 1px; height: 72px; border-radius: 0 0 13px 13px; background: linear-gradient(to bottom, transparent, var(--panel)); pointer-events: none` (named to avoid Bootstrap's `.fade`) |
| `.stream-bar` / `.caret` | as System artboard (`@keyframes slide`, `@keyframes blink`) |
| `.readonly-field` | flex, gap 10px, height 40px, padding 0 12px, radius 9px, `border: 1px dashed var(--line-2)`, transparent bg, `--tx-2`, `svg` `--tx-3`; text truncates |
| `.settings-row` | grid `300px minmax(0, 1fr)`, gap 40px, padding 28px 0, `border-top: 1px solid var(--line)`; `.settings-title` 15px/600 margin 0; `.settings-hint` 13px `--tx-3` line-height 1.55 margin 6px 0 0; `.settings-card` (on `.card`) padding 22px, flex column gap 18px, max-width 560px; `.settings-card--danger` border `color-mix(in oklch, var(--bad) 30%, transparent)`, bg `color-mix(in oklch, var(--bad) 4%, var(--panel))`; `.settings-title.text-danger` colour `--bad` |
| `.narrow-card` | `.card` padding 24px, max-width 460px, flex column gap 16px |
| `.field` | flex column; `margin-bottom: 18px` (replaces `mb-3` where a page is restyled), zeroed inside containers that space children with `gap` (`.import-form`, `.note-editor`, `.settings-card`; the auth forms keep the 18 px margin, which is the artboard's field gap); `.field > .form-label` margin-bottom 8px |
| `.form-actions` | flex, gap 12px, `align-items: center; padding-top: 4px` |

Primitives that only one page uses (`.note-grid`, `.note-card`, `.material-list`, `.material-row`, `.dropzone`, `.import-card`) are added in the phase that uses them, in the same Primitives section.

**Contract — responsive**: one `@media (max-width: 991.98px)` block (`.detail-grid` → one column; `.settings-row` → one column, gap 16px) and one `@media (max-width: 640.98px)` block (`.page-head` actions go full width only if they wrap; `h1` 24px). Later phases append to these blocks rather than adding new breakpoints.

#### 3. Icon component

**Files**: `Components/Shared/Icon.razor`, `Components/Shared/IconName.cs` (new), `Components/_Imports.razor`

**Intent**: One component that renders every icon the mockups use as inline SVG, so markup never carries raw `<svg>` blobs and no icon font or data-URI is needed outside CSS.

**Contract**: `public enum IconName` in namespace `_10xnotes.Components.Shared`. `Icon.razor` parameters: `[Parameter, EditorRequired] IconName Name`, `[Parameter] int Size = 16`, `[Parameter] string StrokeWidth = "1.7"`, `[Parameter] string? Class`. Renders `<svg width="@Size" height="@Size" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="@StrokeWidth" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false" class="@Class">` followed by a `@switch (Name)` whose cases emit the literal child elements below — no `MarkupString`, no `<title>`, no text. Injects nothing. Add `@using _10xnotes.Components.Shared` to `_Imports.razor`.

| `IconName` | Children (24 grid) | Used for |
|---|---|---|
| `Logo` | `<path d="M6 7h12"/><path d="M6 12h12"/><path d="M6 17h7"/>` (render with `StrokeWidth="2.4"`) | brand mark |
| `FileText` | `<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6M9 17h4"/>` | nav "Moje notatki", note tiles, "Notatka" pane |
| `File` | `<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/>` | file badge, "Plik .md" segment |
| `Layers` | `<path d="M12 3 3 8l9 5 9-5-9-5z"/><path d="m3 13 9 5 9-5"/>` | nav "Moje materiały", material rows, "Materiał źródłowy" |
| `Plus` | `<path d="M12 5v14M5 12h14"/>` | nav "Dodaj materiał", header CTA (`StrokeWidth="2"`) |
| `User` | `<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>` | nav "Moje konto" |
| `Logout` | `<path d="M9 21H6a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3"/><path d="m16 17 5-5-5-5"/><path d="M21 12H9"/>` | nav "Wyloguj się", logout button |
| `Login` | `<path d="M15 3h3a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-3"/><path d="m10 17 5-5-5-5"/><path d="M15 12H3"/>` (not in mockups; mirrors `Logout`) | anonymous nav "Zaloguj się" |
| `UserPlus` | `<circle cx="10" cy="8" r="4"/><path d="M3 21a7 7 0 0 1 14 0"/><path d="M19 8v6M16 11h6"/>` (not in mockups) | anonymous nav "Zarejestruj się" |
| `Clock` | `<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>` | "Zapisano …" meta |
| `ArrowRight` | `<path d="M5 12h14M13 6l6 6-6 6"/>` | note-card arrow, auth submit (`StrokeWidth="2"`) |
| `ArrowUpRight` | `<path d="M7 17 17 7M8 7h9v9"/>` | note pane source link |
| `ChevronRight` | `<path d="m9 6 6 6-6 6"/>` | breadcrumbs |
| `Clipboard` | `<rect x="8" y="3" width="8" height="4" rx="1"/><path d="M16 5h2a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h2"/>` | paste badge, "Wklej tekst" segment |
| `Upload` | `<path d="M12 15V4M7 9l5-5 5 5"/><path d="M4 15v4a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-4"/>` | dropzone |
| `Sparkle` | `<path d="M12 3.5l1.9 5.1 5.1 1.9-5.1 1.9-1.9 5.1-1.9-5.1L5 10.5l5.1-1.9z"/><path d="M18.5 15.5l.7 1.8 1.8.7-1.8.7-.7 1.8-.7-1.8-1.8-.7 1.8-.7z"/>` | generate button, note empty state, auth hero |
| `Check` | `<path d="m5 12.5 4.5 4.5L19 7.5"/>` (`StrokeWidth="2"`) | save buttons, success alert/badge |
| `Pencil` | `<path d="M4 20h4L19 9l-4-4L4 16z"/><path d="m13.5 6.5 4 4"/>` | "Edycja" segment |
| `Eye` | `<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>` | "Podgląd" segment |
| `Trash` | `<path d="M4 7h16M10 11v6M14 11v6M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12M9 7V4h6v3"/>` | delete note / account |
| `Mail` | `<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/>` | e-mail fields |
| `Lock` | `<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>` | password fields |
| `Alert` | `<path d="M12 4 2.5 20h19z"/><path d="M12 10v4M12 17.3v.2"/>` | danger/warning alerts, not-found |
| `X` | `<path d="M6 6l12 12M18 6 6 18"/>` | error-UI dismiss |

Sizes follow the artboards: nav/pane/meta 16 or 14, badges 13, seg 14–15, crumbs 13, tiles 16/20/24/26.

#### 4. Stopgap for the old shell

**File**: `Components/Layout/MainLayout.razor.css`

**Intent**: Keep P1 coherent until P2 replaces the shell: the blue→purple sidebar gradient and the `#f7f7f7` top row would clash with the dark content.

**Contract**: `.sidebar` background → `var(--side)` with `border-right: 1px solid var(--line)`; `.top-row` background → `var(--bg)`, border-bottom → `var(--line)`. Nothing else; P2 rewrites the file.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test` (incl. `DataAccessBoundaryTests` — `Icon` injects nothing)
- `Components/App.razor` contains `lang="pl"`, `data-bs-theme="dark"` and the `fonts.googleapis.com/css2?family=Geist` link
- `grep -rn "(MarkupString)" Components` matches only `Components/Notes/NoteEditor.razor`
- `grep -n "<title" Components/Shared/Icon.razor` returns nothing and `Icon.razor` has no `@inject`
- `grep -c "\-\-bs-btn-bg" wwwroot/app.css` ≥ 6 (every variant in the table is mapped)

#### Manual Verification:

- `dotnet run`; at http://localhost:5125 every route (`/login`, `/register`, `/`, `/materials`, `/materials/import`, a `/materials/{id}`, a `/notes/{id}`, `/profile`, `/logout`) renders on the dark background in Geist, with no light-theme remnant on buttons, inputs, alerts, list groups, the btn-group toggles or the delete modal
- Focus rings on buttons and inputs are the violet soft ring; disabled buttons and inputs are visibly dimmed
- A failed login shows the dark red alert; an invalid field shows the red ring and red message

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Commit: `feat(ui-redesign): dark theme tokens, bootstrap mapping and icon component (p1)` — include `context/changes/ui-redesign/`.

---

## Phase 2: App shell and auth layout

### Overview

Replace the template shell with the mockup's: sidebar (brand, icon nav, user block, footer links), a mobile top bar with a CSS-only slide-out drawer, the error bar and reconnect dialog in the dark design, and a sidebar-less two-column layout for Login and Register with the restyled forms. These land together because `#blazor-error-ui` must leave MainLayout in the same commit that introduces a second layout.

### Changes Required:

#### 1. Main layout

**Files**: `Components/Layout/MainLayout.razor`, `Components/Layout/MainLayout.razor.css` (rewrite)

**Intent**: A two-column shell — fixed-width sidebar and a content column with the accent glow — without the "About" top row or the error bar.

**Contract**: Markup `div.page > (div.sidebar > <NavMenu />) + (main.main > article.content > @Body)` — `article` loses the `px-4` utility (its `!important` padding would beat `.content`). Delete the `div.top-row` and the `#blazor-error-ui` block (moved to `App.razor`). CSS: `.page` flex column; `.main` `flex: 1; min-width: 0; min-height: calc(100vh - 56px); background: radial-gradient(900px 360px at 28% -140px, color-mix(in oklch, var(--accent) 10%, transparent), transparent 70%), var(--bg)`; `.content` `padding: 40px 48px 64px`. Mobile (`max-width: 640.98px`): `.sidebar` is the sticky top bar (`position: sticky; top: 0; z-index: 1030; height: 56px; background: var(--side); border-bottom: 1px solid var(--line)`), `.content` padding `24px 16px 48px`. Desktop (`min-width: 641px`): `.page` row; `.main` `min-height: 100vh` (on mobile the 56 px top bar sits above it, so a full `100vh` would make every short page scroll); `.sidebar` `width: var(--sidebar-w); flex: none; height: 100vh; position: sticky; top: 0; display: flex; flex-direction: column; background: var(--side); border-right: 1px solid var(--line)`. Drop the P1 stopgap rules and all `.top-row` rules.

#### 2. Navigation

**Files**: `Components/Layout/NavMenu.razor`, `Components/Layout/NavMenu.razor.css` (rewrite; delete the `.bi-*` data-URI icon rules)

**Intent**: The mockup sidebar: brand row, primary nav, and a footer holding the user block, "Moje konto" and "Wyloguj się" — while keeping every behaviour of today's menu.

**Contract**: Structure, in this order (the checkbox stays the preceding sibling of `.nav-scrollable`):

- `div.side-top > a.brand[href=""] > span.logo(<Icon Name="IconName.Logo" StrokeWidth="2.4" />) + span "10xnotes"` — keep the brand text lower-case as today.
- `<input type="checkbox" title="Navigation menu" class="navbar-toggler" />` — unchanged element and title.
- `div.nav-scrollable` with the **unchanged** inline `onclick="document.querySelector('.navbar-toggler').click()"`, containing `nav.nav.flex-column.side-nav` (keep Bootstrap's `nav` class, the `nowrap` rule and its long comment). Inside `AuthorizeView`:
  - `<Authorized>`: `div.side-main` with three `div.nav-item > NavLink.nav-link` items — "Moje notatki" (`href=""`, `Match="NavLinkMatch.All"`, `FileText`), "Moje materiały" (`href="materials"`, `Match="NavLinkMatch.All"` + keep its comment, `Layers`), "Dodaj materiał" (`href="materials/import"`, prefix match, `Plus`). Then `div.side-foot` with: `div.nav-user` (keep the class, its comment and `title="@AuthCookie.DisplayNameOrEmail(context.User)"`) containing `span.avatar[aria-hidden=true]` = first character of the display value upper-cased — by rune, not by `char`, so an emoji or other non-BMP first character is not split in half: a `private static string Initial(string? name) => string.IsNullOrEmpty(name) ? "?" : name.EnumerateRunes().First().ToString().ToUpperInvariant();` helper in a new NavMenu `@code` block (presentational only) (fallback `"?"` when null/empty), and `span.nav-user-text > span.user-name` = `DisplayNameOrEmail` + `span.user-mail` = `context.User.FindFirstValue(ClaimTypes.Email)` rendered **only when it differs** from the name; then "Moje konto" (`profile`, `User`) and "Wyloguj się" (`logout`, `Logout`) nav items (keep the logout-is-a-link comment).
  - `<NotAuthorized>`: "Zaloguj się" (`login`, `Login`) and "Zarejestruj się" (`register`, `UserPlus`) in `div.side-main`.
  - Each link's content: `<Icon Name="…" />` then `<span>label</span>`. Labels unchanged.
- Add `@using System.Security.Claims` for `FindFirstValue`. No `@onclick`, no injected services beyond today's.

CSS (scoped; icons and the `NavLink` `<a>` are reached with `::deep`):

- `.side-top`: flex, `align-items: center; height: 56px; padding: 0 14px` (mobile) / `padding: 20px 14px 0; height: auto` (desktop). In `wwwroot/app.css` (Primitives) rather than this scoped sheet, because AuthLayout renders the same brand and scoped CSS cannot be shared (review F4): `.brand` flex, gap 10px, padding 2px 8px, 15px/600, `letter-spacing: -0.01em`, colour `--tx`. `.logo` 28×28, radius 8px, bg `--accent`, colour `--on-accent`, grid centre, `box-shadow: inset 0 1px 0 rgba(255,255,255,.3), 0 6px 18px -6px var(--accent)`.
- `.side-nav`: `display: flex; flex-direction: column; flex-wrap: nowrap; min-height: 100%; padding: 22px 14px 16px; gap: 22px`. `.side-main` flex column gap 2px. `.side-foot` `margin-top: auto; display: flex; flex-direction: column; gap: 2px; border-top: 1px solid var(--line); padding-top: 14px`.
- `.nav-item ::deep .nav-link`: `display: flex; align-items: center; gap: 10px; height: 36px; padding: 0 10px; border-radius: 8px; color: var(--tx-2); font-weight: 500; font-size: 13.5px`; `::deep .nav-link svg` `color: var(--tx-3); flex: none`; hover bg `color-mix(in oklch, var(--raise) 70%, transparent)`, colour `--tx`; `::deep a.active` bg `--accent-soft`, colour `--tx`, `svg` `--accent`; `:focus-visible` ring `0 0 0 3px var(--accent-soft)`.
- `.nav-user`: `display: flex; align-items: center; gap: 10px; padding: 4px 10px 12px; min-width: 0`; `.avatar` 32×32 circle, bg `--accent-soft`, colour `--accent`, border `1px solid var(--accent-line)`, 13px/600, grid centre, `flex: none`; `.nav-user-text` flex column, `min-width: 0`; `.user-name` 13.5px/500 `--tx`, `.user-mail` 12px `--tx-3`; both `overflow: hidden; text-overflow: ellipsis; white-space: nowrap` (the truncation path the existing comment protects).
- `.navbar-toggler` (mobile): `appearance: none; position: absolute; top: 10px; right: 12px; width: 40px; height: 36px; border-radius: 8px; border: 1px solid var(--line-2); background: var(--raise) url("data:image/svg+xml,…M4 7h16M4 12h16M4 17h16… stroke='%23A2A8B7' stroke-width='1.7' stroke-linecap='round'…") no-repeat center / 20px`; `:checked` bg `--accent-soft`, border `--accent-line`; `:focus-visible` ring.
- `.nav-scrollable` (mobile): `position: fixed; top: 56px; left: 0; bottom: 0; width: min(280px, 85vw); background: var(--side); border-right: 1px solid var(--line); overflow: visible; z-index: 1040; transform: translateX(-100%); visibility: hidden; transition: transform .2s ease, visibility .2s`; `.navbar-toggler:checked ~ .nav-scrollable { transform: none; visibility: visible }`; scrim `.navbar-toggler:checked ~ .nav-scrollable::before { content: ""; position: absolute; top: 0; bottom: 0; left: 100%; width: 100vw; background: rgba(5,6,9,.6) }` — tapping it hits the inline `onclick` and closes the drawer. `@media (prefers-reduced-motion: reduce)` drops the transition. **The drawer scrolls on `.side-nav`, not here:** `overflow-y: auto` on `.nav-scrollable` would force `overflow-x: auto` with it (the kept `.nav` comment documents exactly that) and clip the `left: 100%` scrim into a sideways-scrolling strip inside the drawer, so on mobile `.nav-scrollable` stays `overflow: visible` and `.side-nav` gets `height: 100%; overflow-y: auto` instead (review F1).
- Desktop (`min-width: 641px`): toggler `display: none`; `.nav-scrollable { position: static; transform: none; visibility: visible; width: auto; border: 0; flex: 1; min-height: 0; overflow-y: auto }` — MainLayout's `.sidebar` is `display: flex; flex-direction: column` on desktop, so the drawer fills the height under the brand row without a `calc()`; the `::before` scrim is never shown.

#### 3. Error bar and reconnect dialog

**Files**: `Components/App.razor`, `wwwroot/app.css` (Error UI section), `Components/Layout/ReconnectModal.razor.css`

**Intent**: Both framework UIs match the dark design and work under either layout; their contracts with `blazor.web.js` stay intact.

**Contract**: Move `<div id="blazor-error-ui" data-nosnippet>` into `App.razor` `<body>` right after `<Routes />`, same English copy and the `a.reload` link; replace the 🗙 glyph with `<span class="dismiss" role="button" aria-label="Dismiss"><Icon Name="IconName.X" Size="15" /></span>` (keep the class; `blazor.web.js` wires `.dismiss`/`.reload` by class inside the id). In `app.css`: `#blazor-error-ui { display: none; position: fixed; left: 0; right: 0; bottom: 0; z-index: 1060; padding: 12px 48px 12px 20px; background: color-mix(in oklch, var(--bad) 8%, var(--panel-2)); border-top: 1px solid color-mix(in oklch, var(--bad) 26%, transparent); color: var(--tx); font-size: 13.5px; box-shadow: 0 -12px 32px -16px rgba(0,0,0,.8) }` (no `color-scheme: light only`), `.reload` accent link with 12px left margin, `.dismiss` absolute right 14px centred, `--tx-3`, cursor pointer. ReconnectModal: keep every id, class and English string in the `.razor` and `.razor.js` files untouched; in the CSS change only colours/radii/shadows — dialog bg `--panel-2`, border `1px solid var(--line-2)`, radius 16px, colour `--tx`, `box-shadow: 0 40px 90px -24px rgba(0,0,0,.8)`; `::backdrop` `rgba(5,6,9,.68)` + `backdrop-filter: blur(3px)`; `p` colour `--tx-2`; buttons like `.btn-primary` (bg `--accent`, colour `--on-accent`, radius 8px, height 36px, padding 0 14px, 13.5px/500; hover accent 88% white); the rejoining animation rings `--accent`.

#### 4. Auth layout

**Files**: `Components/Layout/AuthLayout.razor`, `Components/Layout/AuthLayout.razor.css` (new)

**Intent**: The Login artboard's two-column frame: a decorative hero on the left, the page's form centred on the right.

**Contract**: `@inherits LayoutComponentBase`. Markup `div.auth > section.auth-hero + main.auth-main > div.auth-panel > @Body`. Hero, all decorative except the copy: `a.brand[href="/login"]` (same brand anatomy as NavMenu, styled by the global `.brand`/`.logo` rules in `app.css`), `div.auth-art[aria-hidden=true]` with the two illustrative cards from `Login.dc.html` (card 1: `Layers` 13 + mono "wyklad-04-modmono.md" + nine grey bars of widths 92/100/84/97/60/0/88/95/70 %, rotated −3°; card 2: `Sparkle` 14 accent "Szkic notatki", "Streszczenie", two bars, "Konspekt", two dot+bar rows; accent border and glow), `p.auth-hero-title` (**not** an `h1` — `FocusOnNavigate` would grab it) "Nie pisz notatek od zera. Popraw gotowy szkic.", `p.auth-hero-lead` "Wklej wykład, artykuł albo dokument — AI przygotuje streszczenie i konspekt, a Ty tylko je poprawiasz i zatwierdzasz.", and `p.auth-hero-hint` "Twoje materiały i notatki są widoczne tylko dla Ciebie.". No `@onclick`, no injections. CSS from the artboard: `.auth` grid `repeat(2, minmax(0, 1fr))`, `min-height: 100vh`, bg `--bg`; `.auth-hero` `padding: 44px 64px; display: flex; flex-direction: column; justify-content: space-between; gap: 40px; border-right: 1px solid var(--line); background: radial-gradient(700px 420px at 30% 70%, color-mix(in oklch, var(--accent) 16%, transparent), transparent 70%), radial-gradient(circle at 1px 1px, rgba(255,255,255,.06) 1px, transparent 0) 0 0 / 22px 22px, var(--side); overflow: hidden`; `.auth-art` 460×250 relative with the two absolutely placed cards (left 0/top 0/width 280 and right 0/top 56/width 250; values exactly as the artboard); `.auth-hero-title` 38px/600, line-height 1.12, `letter-spacing: -0.03em`, `text-wrap: balance`, max-width 520px; lead 15px `--tx-2` line-height 1.6; hint = `.form-text` look. `.auth-main` flex centre, padding 48px; `.auth-panel` `width: min(380px, 100%)`. Below 992 px: one column, `.auth-hero` collapses to the brand row only (`.auth-art`, title, lead, hint hidden; padding 20px 16px; no border-right, border-bottom `--line`), `.auth-main` padding 32px 16px, align start.

#### 5. Login and Register pages

**Files**: `Components/Pages/Account/Login.razor`, `Components/Pages/Account/Register.razor`

**Intent**: Opt into the auth layout and adopt the artboard's form anatomy, with every hook and all copy unchanged.

**Contract**: Add `@layout AuthLayout` under the `@page`/`@attribute` lines. Markup order: `div.auth-head` (flex column, gap 8px, margin-bottom 28px) `> h1` (unchanged text) `+ p.sub` holding the **existing** cross-link paragraph moved up from the bottom ("Nie masz konta? <a href="/register">Zarejestruj się</a>" / "Masz już konto? <a href="/login">Zaloguj się</a>"); then the `div.alert.alert-danger[role=alert]` (add `<Icon Name="IconName.Alert" />` before a `<span>@errorMessage</span>`); then the one `EditForm` (unchanged attributes). Inside the form each field is `div.field` > `label.form-label` + `div.input-icon` (`<Icon Name="IconName.Mail" />` / `IconName.Lock`) wrapping the unchanged `InputText` (`#email`, `#password`, same `autocomplete`) + (Register only) the unchanged `div.form-text` hint + the unchanged `ValidationMessage`. Submit: `button[type=submit].btn.btn-primary.btn-lg.w-100` with the unchanged label followed by `<Icon Name="IconName.ArrowRight" StrokeWidth="2" />`; `div.form-actions` wrapper with `margin-top: 10px`. Put the `.auth-head` rules in `app.css` (the elements are rendered by the page, so the layout's scoped sheet cannot reach them without `::deep`). `@code` untouched.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- Hook check: the `feature/test-plan` suite passes in the throwaway worktree (recipe in Critical Implementation Details) — `AccountLoginTests` and `AccountRegisterTests` included
- `grep -n 'id="blazor-error-ui"' Components/App.razor` matches and `grep -rn "blazor-error-ui" Components/Layout/MainLayout.razor` does not
- `grep -rln "@layout AuthLayout" Components` lists exactly `Login.razor` and `Register.razor`
- `grep -rn "@onclick" Components/Layout Components/Pages/Account` returns nothing
- In `NavMenu.razor` the `navbar-toggler` line comes immediately before the `nav-scrollable` element, and `grep -c "NavLinkMatch.All" Components/Layout/NavMenu.razor` is 2
- `grep -n "components-reconnect-button\|components-resume-button\|components-seconds-to-next-attempt" Components/Layout/ReconnectModal.razor` still finds all three ids

#### Manual Verification:

- At 1440 px, `/` shows the sidebar as in `Main.dc.html`: brand, three nav items with icons, active item tinted violet, footer with avatar initial, name, e-mail, "Moje konto", "Wyloguj się"; no "About" row
- `/materials/import` lights only "Dodaj materiał"; `/materials/{id}` lights nothing; `/materials` lights only "Moje materiały"
- A 200-character display name (set on `/profile`) truncates with an ellipsis and does not widen the sidebar or add a horizontal scrollbar; with no display name the e-mail shows once
- At ~400 px: top bar with logo + hamburger; tapping it slides the drawer in with a dimmed page; tapping a link navigates and closes it; tapping the dimmed area closes it; works on the static `/` and `/materials` pages as well as on interactive `/materials/import`
- `/login` and `/register` match `Login.dc.html` (hero left, form right, icons in the fields, arrow in the button, cross-link under the heading); at ~400 px the hero reduces to the brand row; wrong credentials show the dark alert with an icon; empty submit shows red rings + messages; a successful login lands on `/`
- Stopping `dotnet run` while on `/materials/import` shows the dark reconnect dialog (violet ring animation, English copy); restarting and pressing "Retry" recovers
- Temporarily un-hiding `#blazor-error-ui` in devtools shows the dark error bar with the X icon

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Commit: `feat(ui-redesign): sidebar shell, mobile drawer and auth layout (p2)`.

---

## Phase 3: Lists and import

### Overview

The two index pages and the import page take the `Main`, `Materials` and `Import` artboards: page heads with subtitles and a "Dodaj materiał" CTA, a card grid for notes, a table-like list with badges for materials, dashed empty states, and the import card with the segmented switch and the dropzone. Both index pages stay static SSR; Import stays interactive with its panels hidden, never removed.

### Changes Required:

#### 1. Notes index

**Files**: `Components/Pages/Notes/Index.razor`, `wwwroot/app.css` (add `.note-grid`, `.note-card*`)

**Intent**: The `Main.dc.html` card grid and empty card, with the page's existing copy, links and render mode.

**Contract**: 
- `div.page-head > div(h1 "Moje notatki" + p.sub "Notatki zatwierdzone z Twoich materiałów.") + a.btn.btn-primary[href="/materials/import"](<Icon Name="IconName.Plus" StrokeWidth="2" /> "Dodaj materiał")`.
- `loadFailed`: `div.alert.alert-danger[role=alert]` (`Alert` icon + span, same sentence) instead of `p.text-danger`; keep the comment.
- Empty: `div.card.empty-state` > `div.icon-tile.icon-tile--lg(<Icon Name="IconName.FileText" Size="24" />)` + `div` > `p.empty-state-title` "Nie masz jeszcze żadnej zapisanej notatki." (period kept — copy stays 1:1) + `p` "Wybierz materiał i wygeneruj z niego notatkę." (the existing sentence split in two) + `a.btn.btn-secondary[href="/materials"](<Icon Name="IconName.Layers" /> "Przejdź do moich materiałów")`; keep the comment.
- List: `ul.note-grid` > per note `li.card.note-card` > `div.note-card-top` (`div.icon-tile(FileText)` + `a.note-card-source[href="/materials/@note.SourceMaterialId"]`(`Layers` 14 + "Materiał źródłowy")) + `a.note-card-title.stretched-link[href="/notes/@note.Id"]` `@note.Title` (plain interpolation; keep the comment) + `div.note-card-foot` (`span.meta`(`Clock` 14 + "Zapisano {same ToDisplayTime/PolishCulture expression}") + `span.note-card-arrow`(`ArrowRight`)). Keep the date comment.
- CSS: `.note-grid` `list-style: none; padding: 0; margin: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px`. `.note-card` `padding: 20px; gap: 16px; min-height: 184px; position: relative; transition: border-color .15s, background .15s`; hover/focus-within: `border-color: var(--accent-line); background: var(--panel-2); box-shadow: 0 0 0 1px var(--accent-line), 0 20px 44px -24px var(--accent)` and the arrow turns `--accent`. `.note-card-top` flex space-between. `.note-card-source` inline-flex gap 5px, 12.5px/500, `position: relative; z-index: 2` (above the stretched link). `.note-card-title` 16px/600, line-height 1.38, `letter-spacing: -0.01em`, colour `--tx`, `text-wrap: pretty`, `overflow-wrap: anywhere`. `.note-card-foot` `margin-top: auto; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding-top: 14px; border-top: 1px solid var(--line)`; `.note-card-arrow` `--tx-3`.

#### 2. Materials index

**Files**: `Components/Pages/Materials/Index.razor`, `wwwroot/app.css` (add `.material-list`, `.material-head`, `.material-row*`)

**Intent**: The `Materials.dc.html` table look — title, source badge, date, note status — keeping the Kind branch and the Razor verbs (D26).

**Contract**:
- `div.page-head > div(h1 "Moje materiały" + p.sub "Wykłady, artykuły i dokumenty, z których generujesz notatki.") + a.btn.btn-primary` "Dodaj materiał" (as on the notes page).
- `loadFailed`: `div.alert.alert-danger` (same sentence).
- Empty: `div.card.empty-state` > `div.icon-tile.icon-tile--lg(Layers 24)` + `p.empty-state-title` "Nie masz jeszcze żadnego materiału." + `a.btn.btn-secondary[href="/materials/import"](Plus + "Dodaj materiał")`.
- List: `div.card.material-table` > `div.material-head[aria-hidden=true]` (four spans "Materiał", "Źródło", "Dodano", "Notatka") + `ul.material-list` > per material `li.material-row` with four cells:
  1. `div.material-cell-title` > `span.material-icon(<Icon Name="IconName.Layers" />)` + `a.material-title[href="/materials/@material.Id"]` `@material.Title`;
  2. `div.material-cell-source` > Kind `Paste` → `span.badge(<Icon Name="IconName.Clipboard" Size="13" /> <span>Wklejony tekst</span>)`; otherwise `span.badge.mono(<Icon Name="IconName.File" Size="13" /> <span>@material.OriginalFileName</span>)` with `title="@material.OriginalFileName"`;
  3. `div.material-cell-date.meta` > Paste → "Wklejono {date}"; otherwise "Zaimportowano {date}" (same `ToDisplayTime(...).ToString("d MMMM yyyy, HH:mm", PolishCulture)` expression);
  4. `div.material-cell-note` > `NoteId` present → `a.badge.st-ok[href="/notes/@noteId"](span.dot + "Otwórz notatkę")`; else `span.badge.st-none(span.dot + "Brak notatki")`.
  Keep the Kind-branch comment and the plain-interpolation comment; delete the `<text>` whitespace comment together with the `·` separators it explained.
- CSS: `.material-head` and `.material-row` share `display: grid; grid-template-columns: minmax(0, 1.55fr) minmax(0, 1fr) minmax(0, .95fr) 168px; gap: 20px; align-items: center; padding: 0 20px`. `.material-head` height 44px, 11.5px/500, `letter-spacing: .07em`, uppercase, `--tx-3`, last span right-aligned. `.material-list` list reset. `.material-row` `min-height: 62px; border-top: 1px solid var(--line)`; hover bg `--panel-2` and title colour `--accent`. `.material-cell-title` flex gap 12px `min-width: 0`; `.material-icon` `--tx-3`; `.material-title` 500 `--tx`, single-line ellipsis. `.material-cell-source` `display: flex; min-width: 0`. `.material-cell-date` 13px `--tx-2`. `.material-cell-note` flex end. Responsive (`max-width: 767.98px`): hide `.material-head`; `.material-row` becomes `grid-template-columns: minmax(0, 1fr) auto; grid-template-areas: "title title" "source note" "date date"; padding: 14px 16px; row-gap: 8px`; each cell takes its area (`.material-cell-title` → `title`, `.material-cell-source` → `source`, `.material-cell-note` → `note`, `.material-cell-date` → `date`).

#### 3. Import

**Files**: `Components/Pages/Materials/Import.razor`, `wwwroot/app.css` (add `.import-card`, `.dropzone*`)

**Intent**: The `Import.dc.html` card: segmented switch, textarea with a right-aligned counter, a dropzone that is the native file input, the title field and a large save button — behaviour untouched.

**Contract**:
- `div.page-head > div(h1 "Dodaj materiał" + p.sub` with the **existing** intro sentence moved in`)`. No header CTA on this page.
- `errorMessage` alert: `div.alert.alert-danger[role=alert]` (`Alert` icon + span), above the card, unchanged condition.
- `div.card.import-card` (`max-width: 840px; padding: 24px; display: flex; flex-direction: column; gap: 22px`) containing, in order:
  - The mode switch: `div.seg[role=group][aria-label="Sposób dodania materiału"]` > two `button[type=button].seg-item` (`on` class when selected; `aria-pressed` as the strings `"true"`/`"false"`) with `Clipboard` 15 + "Wklej tekst" and `File` 15 + "Plik .md"; same `@onclick="() => SelectMode(...)"`. Update the comment above it: the switch now shares the `.seg` look with NoteEditor's toggle.
  - The unchanged `EditForm` (add `class="import-form"`; `.import-form` = flex column, gap 22px) with:
    - Paste panel: wrapper `div.field` + the unchanged `@(mode == InputMode.Paste ? "" : "d-none")` expression; `label.form-label[for=paste]`; the unchanged `textarea#paste.form-control` (add class `import-paste`: `min-height: 292px; resize: vertical`); the counter `div.form-text.mono.text-end` with the unchanged `N0` expression and its comment.
    - File panel: wrapper `div.field` + unchanged `d-none` expression; `label.form-label[for=file]` "Plik Markdown"; `div.dropzone` > (first child) the unchanged `InputFile id="file"` with `class="dropzone-input"` instead of `form-control` (same `accept`, same `OnChange`) + `div.icon-tile.icon-tile--lg(<Icon Name="IconName.Upload" Size="20" />)` + `div.dropzone-text` > `span.dropzone-prompt` "Upuść plik .md tutaj albo <span class="dropzone-link">wybierz z dysku</span>" + `div.form-text` "Maksymalny rozmiar to … KB." (unchanged expression) + when `selectedFile is not null`: `span.badge.mono(<Icon Name="IconName.File" Size="13" /> <span>@selectedFile.Name</span>)`. The `InputFile` comes first and unconditionally, so the conditional badge after it can never cause Blazor to recreate the input (which would reset the browser's chosen file — the reason the panels use `d-none`).
    - Title block: change only the wrapper line (`<div class="mb-3">` → `<div class="field">`) and the `ValidationMessage` line if needed; the three lines from `<label for="title"…>` through the following `<div class="form-text">` stay **byte-identical** (merge seam with `feature/test-plan`).
    - Submit: `div.form-actions > button[type=submit].btn.btn-primary.btn-lg` with `disabled="@isSaving"` and content `@if (isSaving) { <span class="spin"></span> }` + the unchanged label expression.
- CSS: `.dropzone` `position: relative; min-height: 220px; border: 1.5px dashed var(--line-2); border-radius: 12px; background: var(--field); display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px; padding: 24px; text-align: center; transition: border-color .15s, background .15s`; `:hover`, `:focus-within` → `border-color: var(--accent-line); background: color-mix(in oklch, var(--accent) 4%, var(--field))`; `:focus-within` adds `box-shadow: 0 0 0 3px var(--accent-soft)`. `.dropzone-text` flex column gap 4px. `.dropzone-prompt` 500. `.dropzone-link` accent. `.dropzone-input` `position: absolute; inset: 0; width: 100%; height: 100%; opacity: 0; cursor: pointer; z-index: 1` (a native `<input type=file>` accepts dropped files on its own box — no JS). `.dropzone .form-text` margin 0.
- `@code` untouched (the `Input`/`pasteContent` initializers stay where they are — lessons.md).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- Hook check: the `feature/test-plan` suite passes in the throwaway worktree, and its merge of `Import.razor` completes without conflicts
- `git diff main -- Components/Pages/Materials/Import.razor` shows no change on the `<label for="title"`, `<InputText id="title"` and following `<div class="form-text">` lines
- `grep -n 'id="file"\|id="paste"\|id="title"' Components/Pages/Materials/Import.razor` finds all three ids; `grep -c "d-none" Components/Pages/Materials/Import.razor` is 2
- `grep -n "@rendermode" Components/Pages/Notes/Index.razor Components/Pages/Materials/Index.razor` returns nothing
- `grep -rn "list-group" Components` returns nothing

#### Manual Verification:

- `/` matches `Main.dc.html`: three-column card grid at 1440 px, card hover glow, the whole card opens the note, "Materiał źródłowy" opens the material, header "Dodaj materiał" goes to `/materials/import`
- A fresh account's `/` and `/materials` show the dashed empty cards with working links
- `/materials` matches `Materials.dc.html`: headers, file badge in mono vs "Wklejony tekst", verb + date, green "Otwórz notatkę" link vs grey "Brak notatki"; a long title or file name truncates
- At ~400 px: notes grid is one column; material rows stack into title / badges / date
- `/materials/import` matches `Import.dc.html` in both modes; the counter updates on blur; switching modes keeps both the paste text and a chosen file
- In file mode, clicking anywhere in the zone opens the file picker, dropping a `.md` file onto it selects it, the file-name badge appears and the title auto-fills; keyboard Tab reaches the zone and shows the focus ring
- Empty title → red ring + "Podaj tytuł materiału."; a valid save shows the spinner and lands on the new material page

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Commit: `feat(ui-redesign): notes grid, materials list and import card (p3)`.

---

## Phase 4: Detail pages and the note editor

### Overview

`Materials/Detail`, `Notes/Detail` and the shared `NoteEditor` take the `Material` and `Note` artboards: breadcrumbs and meta in the page head, two panes with pane heads, the source as mono text in a scrolling `<pre>`, the editor with a segmented toggle, counter, preview typography and a pane footer holding save, and the restyled delete dialog. They land together: both pages host the editor and share the pane primitives. This is the phase with the densest test hooks — re-read the hook table in Critical Implementation Details before starting.

### Changes Required:

#### 1. Note editor

**Files**: `Components/Notes/NoteEditor.razor`, `Components/Notes/NoteEditor.razor.css`

**Intent**: The editor block from the artboards (title field; toolbar with the Edycja/Podgląd segment on the left and the counter on the right; editor or preview box; error; footer with the save button), with every hook in place.

**Contract**: Markup order, root `div.note-editor` (flex column, gap 16px):
1. `div.field.mb-0` > unchanged `label[for=note-title].form-label` + unchanged `input#note-title.form-control` (all attributes and bindings as today).
2. `div.note-editor-toolbar` (flex, `align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap`) > `div.seg[role=group][aria-label="Tryb notatki"]` with two `button[type=button].seg-item` (`on` when active; `aria-pressed` strings; `Pencil` 14 + "Edycja", `Eye` 14 + "Podgląd"; same `@onclick`s) + the counter `div.form-text.note-counter` with the **unchanged** text `@Content.Length / @NoteValidator.MaxContentLength znaków` (mono, 12px, `--tx-3`, margin 0). It is the first and only `div.form-text` in the component. No `.btn` class on the toggles.
3. Preview branch: `div.note-preview` with the unchanged `MarkupString` line and its comment (drop the `border rounded p-3` utility classes; the box is styled in the scoped sheet). Editor branch: unchanged `textarea#note-content.form-control.note-source` with its comments.
4. `ErrorMessage` → `div.alert.alert-danger[role=alert]` > `<Icon Name="IconName.Alert" />` + `<span>@ErrorMessage</span>`.
5. `div.pane-foot.note-editor-foot` > `button[type=button].btn.btn-primary` (unchanged `disabled`/`@onclick`) whose content is `@if (IsBusy) { <span class="spin"></span> } else { <Icon Name="IconName.Check" StrokeWidth="2" /> }` followed by the unchanged label expression — no other text inside the button. The foot sits flush with the pane edges: `margin: 2px -18px -18px` (cancels `.pane-body` padding).
Update the header comment only where it is now wrong ("the project has no bUnit" — say the component is covered by `NoteEditorTests` on `feature/test-plan`). `@code` untouched.

Scoped CSS (`NoteEditor.razor.css`): `.note-source` and `.note-preview` keep the shared 60vh box (keep the comments) with `background: var(--field); border: 1px solid var(--line-2); border-radius: 10px`; `.note-source` mono 12.5px, line-height 1.8, colour `--tx-2`, padding 14px 16px; `.note-preview` padding 20px 22px, `overflow-y: auto`. Preview typography from the `.md` block of the artboards, all through `::deep` (the MarkupString output has no scope attribute): `h1,h2` 15px/600 `--tx` margin 0 0 8px `letter-spacing: -0.01em`; `h3–h6` 14px/600; `p` margin 0 0 18px `--tx-2` line-height 1.7; `ol, ul` margin 0 0 18px, padding-left 20px, `--tx-2`, line-height 1.7; `li` margin 3px 0; `li::marker` `--accent`; `ul ul, ol ul` margin 2px 0 4px; `strong` `--tx` 600; `code` mono 12px on `--raise`, border `--line-2`, padding 1px 6px, radius 5px, `--tx`; `a` accent; `pre` keeps wrap + `word-break`; `> *:last-child` margin-bottom 0. `.note-editor-toolbar` / `.note-counter` rules here too; the `Icon` inside the seg is styled by the global `.seg-item svg` rule.

#### 2. Material page

**Files**: `Components/Pages/Materials/Detail.razor`; `Components/Pages/Materials/Detail.razor.css` (remove from the Mac shell: `cd /Users/mati/Projects/10xDevs3 && git rm Components/Pages/Materials/Detail.razor.css` — the edit shell cannot delete; its rules are replaced by `.src`/`.pane*` in `app.css`)

**Intent**: The `Material.dc.html` layout in all three note states (empty, generating, draft), with exactly one generate button and the guardrails intact.

**Contract**:
- Not-found branch: `div.card.empty-state` > `div.icon-tile.icon-tile--lg(<Icon Name="IconName.Alert" Size="24" />)` + `<h1>Nie znaleziono materiału</h1>` on one line, no child, no surrounding whitespace inside the tag + the unchanged `p` + `a.btn.btn-secondary[href="/materials/import"]` "Dodaj nowy materiał". Style `.empty-state h1` at 22px. Nothing that renders an `h1` may precede it.
- Page head: `div.page-head.page-head--detail > div` > `nav.crumbs[aria-label="Ścieżka"]` (`a[href="/materials"]` "Moje materiały" + `<Icon Name="IconName.ChevronRight" Size="13" />` + `span` `@material.Title`) + `h1` `@material.Title` + `div.meta-row` with the Kind branch (keep both comments): Paste → `span.badge(Clipboard 13 + "Wklejony tekst")` + `span.meta` "Wklejono {date}"; otherwise `span.badge.mono(File 13 + span @material.OriginalFileName)` + `span.meta` "Zaimportowano {date}" (D27).
- `div.detail-grid` (keep the two-columns comment; stacking below 992 px is in the P1 responsive block, source first because it is first in the DOM):
  - `section.card.pane` > `div.pane-head > h2.pane-title(<Icon Name="IconName.Layers" /> "Materiał źródłowy")` + `pre.source-content.src` `@material.Content` (keep the raw-text comment; no border/padding utility classes) + `div.pane-fade`.
  - `section.card.pane.pane--note` > `div.pane-head` > `h2.pane-title(FileText + "Notatka")` + the **only** generate button: `button[type=button].btn.btn-sm` + `@(draft is null ? "btn-primary" : "btn-secondary")`, unchanged `disabled` and `@onclick`, content `@if (isGenerating) { <span class="spin"></span> } else { <Icon Name="IconName.Sparkle" Size="14" /> }` + the unchanged `@(isGenerating ? "Generuję…" : "Generuj notatkę")`.
  - Then `@if (isGenerating) { <div class="stream-bar"></div> }` and `div.pane-body` holding, in today's order: the deletion notice `div.alert.alert-success.d-flex.align-items-start[role=alert]` (`Check` icon + `span.flex-grow-1` + the unchanged `button.btn-close` with `@onclick`; keep the comment); the saved-note `p` (class `pane-hint`: 13px `--tx-2`, margin 0; same text and link); the `errorMessage` `div.alert.alert-danger` (`Alert` icon + span); the confirmation `div.alert.alert-warning[role=alert]` > `Alert` icon + `div` > `p` `@ConfirmationQuestion` + `div.alert-actions` > `button.btn.btn-warning.btn-sm` (unchanged) + `button.btn.btn-secondary.btn-sm` "Anuluj" (was `btn-outline-secondary`; unchanged `disabled`/`@onclick`); then the `draft` / `note.Length > 0` / empty branches with unchanged conditions:
    - draft → the unchanged `<NoteEditor … />`;
    - streaming → `pre.note-content.src.src--live` `@note` followed, only while `isGenerating`, by `<span class="caret"></span>` inside the `<pre>` — with no line break or indentation between `@note` and the `@if` block (e.g. `<pre class="note-content src src--live">@note@if (isGenerating){<span class="caret"></span>}</pre>`), because Razor keeps whitespace inside `<pre>` verbatim and it would show up in the note; keep the read-only comment; the element stays a `<pre>`;
    - empty → `div.pane-empty` > `div.icon-tile.icon-tile--xl(<Icon Name="IconName.Sparkle" Size="26" />)` + the unchanged hint `p`. No button here (D6).
  - The streaming `<pre>` stays inside `.pane-body` (so any alert above it keeps today's order) and is pulled flush with the pane edges by `.pane-body > pre.src { margin: 0 -18px -18px; }` in `app.css`; the empty and draft states keep the body padding.
- Remove `h2.h5` headings (replaced by `h2.pane-title`), `row g-4`/`col-lg-6` and `mb-3` utilities. `@code` untouched.

#### 3. Note page

**Files**: `Components/Pages/Notes/Detail.razor`; `Components/Pages/Notes/Detail.razor.css` (remove from the Mac shell: `cd /Users/mati/Projects/10xDevs3 && git rm Components/Pages/Notes/Detail.razor.css`)

**Intent**: The `Note.dc.html` layout: crumbs, title, saved meta and the delete action in the page head; source pane with a link to its material; note pane with the saved badge, the editor and its footer; the dialog as the artboard's `dialogUsuwania` state.

**Contract**:
- Not-found branch: same `div.card.empty-state` treatment as the material page, `h1` "Nie znaleziono notatki", unchanged `p` and link (as `btn-secondary`). Keep the probe comment.
- Page head `div.page-head.page-head--detail` > `div` (`nav.crumbs` with `a[href="/"]` "Moje notatki" + chevron + `span @note.Title`; `h1 @note.Title`; `div.meta-row > span.meta(<Icon Name="IconName.Clock" Size="14" /> "Zapisano {date}.")`, keep the date comment) + `button[type=button].btn.btn-outline-danger` (`Trash` 15 + "Usuń notatkę"; unchanged `disabled="@(isSaving || isDeleting)"` and `@onclick="OpenDeleteDialog"`). Replace the "Below the editor and behind a rule" comment with one explaining the new placement: the only irreversible action sits in the page head, as far as the layout allows from "Zapisz zmiany" in the note pane's footer. Remove the `hr.my-4`.
- `div.detail-grid`:
  - `section.card.pane` > `div.pane-head` (`h2.pane-title(Layers + "Materiał źródłowy")` + when `material` is not null `a.pane-link[href="/materials/@note.SourceMaterialId"]` `@material.Title` + `<Icon Name="IconName.ArrowUpRight" Size="13" />`) + either `div.pane-body > p.pane-hint` "Materiał źródłowy nie jest już dostępny." or `pre.source-content.src @material.Content` + `div.pane-fade` (keep the raw-text comment).
  - `section.card.pane.pane--note` > `div.pane-head` (`h2.pane-title(FileText + "Notatka")` + when `savedMessage` is not null `span.badge.st-ok[role=status](<Icon Name="IconName.Check" Size="13" StrokeWidth="2.2" /> @savedMessage)`) + `div.pane-body` > the unchanged `<NoteEditor … SaveLabel="Zapisz zmiany" … />` (keep the no-confirmation comment).
- Dialog (keep every comment, `@onkeydown`, `role`, `aria-modal`, `aria-labelledby`, both sentinels as first and last children of `div.modal`, both `@ref`s): `div.modal-dialog.modal-dialog-centered > div.modal-content` > `div.modal-body.delete-dialog-body` (flex column, gap 14px, padding 24px 24px 20px) > `div.modal-icon` (40×40, radius 11px, colour `--bad`, bg `color-mix(in oklch, var(--bad) 12%, transparent)`, border `… 30% …`; `<Icon Name="IconName.Trash" Size="19" />`) + `h2.modal-title#delete-note-dialog-title` "Usunąć notatkę?" (18px/600, `letter-spacing: -0.01em`; the `modal-header` wrapper is removed) + the unchanged `p` (colour `--tx-2`, line-height 1.6) + the unchanged `deleteErrorMessage` alert (`Alert` icon + span, `mb-0`); `div.modal-footer` > `button.btn.btn-secondary` "Anuluj" (was `btn-outline-secondary`) + `button.btn.btn-danger` (`@if (!isDeleting) { <Icon Name="IconName.Trash" Size="15" /> } else { <span class="spin"></span> }` + unchanged label expression). The hand-rendered `div.modal-backdrop.show` stays.
- Add `.modal-icon`, `.delete-dialog-body`, `.pane-hint` to `app.css`. `@code` untouched.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- Hook check: the `feature/test-plan` suite passes in the throwaway worktree — `NoteEditorTests` and `MaterialsDetailTests` included
- `grep -rn "(MarkupString)" Components` still matches only `Components/Notes/NoteEditor.razor`
- `grep -c "<button" Components/Pages/Materials/Detail.razor` is unchanged from before the phase (no button added or removed)
- `grep -n "<h1>Nie znaleziono materiału</h1>" Components/Pages/Materials/Detail.razor` matches
- `grep -n 'class="form-text' Components/Notes/NoteEditor.razor` matches exactly one line, the counter, and it appears before the textarea
- `grep -n 'class="[^"]*btn-primary' Components/Notes/NoteEditor.razor` matches exactly one line, the save button (patterns target markup, not the comments that name these on purpose)
- `grep -rnE 'data-bs-(dismiss|toggle)="|IJSRuntime' Components` returns nothing
- `Components/Pages/Materials/Detail.razor.css` and `Components/Pages/Notes/Detail.razor.css` no longer exist

#### Manual Verification:

- A material with no note matches `Material.dc.html` `stan=pusty`: crumbs, badge + meta, source pane with fade, empty note pane with the glowing sparkle tile and the hint, one primary "Generuj notatkę" in the pane head
- Generating (one run — it spends the daily quota) shows the moving stream bar, the busy button with spinner and "Generuję…", live text with a blinking caret in a read-only `<pre>`; when it finishes the draft editor appears and the pane-head button turns secondary
- Draft: title field, segment toggle (active item raised, violet icon), counter "N / 65536 znaków", mono textarea, footer "Zapisz notatkę" with a check icon; toggling to "Podgląd" renders headings, lists with violet markers and inline code as in `Note.dc.html`, without the box changing size
- On a material that already has a note, saving shows the amber confirmation with "Zastąp notatkę" and a secondary "Anuluj"; "Anuluj" removes it
- `/notes/{id}` matches `Note.dc.html`: crumbs to "Moje notatki", "Usuń notatkę" top right, source link with ↗ icon, "Zapisz zmiany" shows the green "Zapisano zmiany o HH:mm." badge in the pane head
- Delete dialog matches `dialogUsuwania`: blurred scrim, trash tile, title, quoted note title, secondary "Anuluj" focused, red "Usuń notatkę"; Tab/Shift+Tab cycle inside, Escape closes, confirming lands on the material with the dismissible green notice
- An unknown `/materials/{guid}` and `/notes/{guid}` show the not-found cards
- At ~400 px both detail pages stack source above note; at ~1000 px (below `lg`) they stack too

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Commit: `feat(ui-redesign): detail panes, note editor and delete dialog (p4)`.

---

## Phase 5: Account and system pages, responsive pass

### Overview

The remaining pages — Profile (settings rows + danger zone), Logout, Error, NotFound — adopt the primitives, and a cross-page pass at narrow widths fixes whatever the per-page work left. Closes with a full hook check and the complete grep suite.

### Changes Required:

#### 1. Profile

**File**: `Components/Pages/Account/Profile.razor`

**Intent**: The `Profile.dc.html` settings layout — title + hint on the left, form card on the right — for the four existing sections, each still its own `EditForm` with its own `FormName` and error state.

**Contract**:
- `div.page-head > div(h1 "Moje konto" + p.sub "Dane logowania, nazwa w menu i ustawienia bezpieczeństwa.")`.
- Four `section.settings-row`, each `div.settings-intro` (`h2.settings-title` + `p.settings-hint`) + `div.card.settings-card`:
  1. "Dane konta" — hint "Adres, którym logujesz się do aplikacji." (new, allowed); card: `div.field.mb-0` > `div.form-label` "Adres e-mail" + `div.readonly-field(<Icon Name="IconName.Mail" /> <span>@Email</span>)` + the unchanged `div.form-text` "Adresu e-mail nie można zmienić." (replaces the `dl.row`).
  2. "Nazwa wyświetlana" — hint = the **existing** paragraph "Nazwa pojawi się w menu zamiast adresu e-mail. Zostaw pole puste, aby wrócić do adresu." moved from the card into `p.settings-hint`; card: `displayNameError` alert (`Alert` icon + span), the unchanged `EditForm` with `div.field` > label + `InputText#display-name` (unchanged attributes) + `div.form-text` + `ValidationMessage`, then `div.form-actions > button[type=submit].btn.btn-secondary` "Zapisz nazwę" (D30).
  3. "Zmiana hasła" — hint "Hasło, którym logujesz się do aplikacji." (new, allowed); card: `passwordError` danger alert / `passwordChanged` success alert (`Check` icon + span; same text), the unchanged `EditForm` with two `div.field`s (`#current-password`, `#new-password` + its hint), `button.btn.btn-secondary` "Zmień hasło".
  4. "Usunięcie konta" — `h2.settings-title.text-danger`, hint "Znikną wszystkie Twoje notatki, materiały źródłowe i samo konto." (new, allowed); card `div.card.settings-card.settings-card--danger`: `deleteError` alert, the two unchanged paragraphs (colour `--tx-2`, line-height 1.6), the unchanged `EditForm` with `div.field` > label "Hasło" + `div.input-icon(<Icon Name="IconName.Lock" />` + `InputText#delete-password`) + `ValidationMessage`, then `button.btn.btn-danger(<Icon Name="IconName.Trash" Size="15" /> "Usuń konto na zawsze")`.
- `@code` untouched — in particular the model properties that the forms bind (lessons.md: state before the first await).

#### 2. Logout, Error, NotFound

**Files**: `Components/Pages/Account/Logout.razor`, `Components/Pages/Error.razor`, `Components/Pages/NotFound.razor`

**Intent**: Give the three copy-only pages the card treatment so no page renders as raw template output; copy and headings stay.

**Contract**:
- Logout: `div.page-head > div > h1 "Wyloguj się"`; `div.card.narrow-card` > the unchanged `p` + the unchanged `form` (`method=post`, `@formname`, `AntiforgeryToken`) whose button becomes `button[type=submit].btn.btn-primary(<Icon Name="IconName.Logout" /> "Wyloguj się")`.
- Error: wrap everything below `PageTitle` in `div.card.narrow-card.system-card` (max-width 640px); keep `h1.text-danger` "Error.", `h2.text-danger` (restyle `.system-card h2` to 16px/600), the request-id `code`, `h3` + the English paragraphs (`.system-card p` `--tx-2`). No copy change.
- NotFound: `div.card.empty-state` > `div.icon-tile.icon-tile--lg(<Icon Name="IconName.Alert" Size="24" />)` + the unchanged `h3` "Not Found" (styled 17px/600) + the unchanged `p`. No link is added (it would be new copy). Keep `@layout MainLayout`.

#### 3. Responsive pass and cleanup

**Files**: `wwwroot/app.css`, the scoped sheets touched in P2–P4 (only if a defect is found)

**Intent**: Walk every route at ~400 px and ~768 px and fix overflow, wrapping and spacing issues in the existing breakpoint blocks; remove what the redesign made dead.

**Contract**: No new breakpoints (641 px shell, 768 px materials rows, 992 px panes/settings/auth). Fix-ups are expected in: `.page-head` wrapping (actions drop under the title, full width buttons only below 641 px), `.crumbs` truncation, `.meta-row` wrapping, `.badge` truncation inside grid cells, `.seg` not overflowing, `.pane-head` wrapping when the generate button and a long title meet, modal width (`--bs-modal-margin: 16px` below 576 px), `#blazor-error-ui` padding. Remove the now-unused `.list-group` mapping from `app.css`. Confirm no page scrolls horizontally at 400 px.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- Full hook check: the entire `feature/test-plan` suite passes in the throwaway worktree
- `grep -n 'FormName="display-name"\|FormName="change-password"\|FormName="delete-account"' Components/Pages/Account/Profile.razor` finds all three
- `grep -rn "(MarkupString)" Components` matches only `Components/Notes/NoteEditor.razor`
- `grep -rn "@onclick" Components/Pages/Account Components/Pages/Notes/Index.razor Components/Pages/Materials/Index.razor Components/Pages/Error.razor Components/Pages/NotFound.razor` returns nothing (static SSR pages stay non-interactive)
- `grep -rn "text-secondary small\|list-group\|bi-.*-nav-menu" Components wwwroot/app.css` returns nothing (template leftovers gone)

#### Manual Verification:

- `/profile` matches `Profile.dc.html` at 1440 px; each of the three forms submits independently (name change shows in the sidebar; wrong current password shows the dark alert; a successful change shows the green alert) — test account deletion only on a throwaway account
- `/logout` shows the card and signs out to `/login`; `/not-found` (any unknown URL) and `/Error` show their cards in the dark design with the English copy
- Every route at ~400 px: no horizontal scroll, readable page heads, badges and crumbs truncate, forms full width, dialog fits with side margins
- Every route at 1440 px compared side by side with its artboard (`design/README.md` map) — spacing, radii, colours and type sizes match within a few pixels

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful. Commit: `feat(ui-redesign): profile settings, system pages and responsive pass (p5)`.

---

## Testing Strategy

### Unit Tests:

- None added. The change is presentational; the existing suite (services, `DataAccessBoundaryTests`, `NoteMarkdownTests`) must stay green each phase. `DataAccessBoundaryTests` automatically covers the new `Icon` and `AuthLayout` components (they inject nothing).

### Integration Tests:

- The `feature/test-plan` bUnit suite is the real guard for markup hooks. It is run against each phase's result in a throwaway worktree with `feature/test-plan` merged in (P2, P3, P4, P5), then the worktree is removed. A failing hook test means the phase broke a contract — fix the markup, not the test.
- Static greps per phase (listed in each phase) pin the invariants a test cannot reach from this branch: one `MarkupString`, no Bootstrap JS attributes, no `IJSRuntime`, no `@onclick` on static pages, ids present, the Import merge seam untouched.

### Manual Testing Steps:

1. `dotnet run` (http://localhost:5125, user-secrets already configured); sign in with a test account.
2. Walk the routes in `design/README.md`'s map at 1440 px next to each artboard, then at ~400 px.
3. Exercise the states: login error + validation; register hint; notes/materials empty (fresh account) and populated; import paste and file (click and drop) + validation; material empty → generate (one run) → draft → save → confirm-replace; note preview/edit/save badge; delete dialog keyboard (Tab, Shift+Tab, Escape) and deletion notice dismiss; profile three forms; logout; unknown URL; reconnect dialog by stopping the server.
4. Mobile drawer on a static page (`/`) and an interactive one (`/materials/import`).

## Performance Considerations

One extra cross-origin stylesheet (Google Fonts) with `preconnect` and `display=swap` — text renders in the fallback stack until Geist arrives. `color-mix()` and `backdrop-filter` are evaluated by the browser (supported by all current evergreen browsers); no JS, no new round trips on the circuit. The `Icon` component is stateless and cheap; the notes grid renders one extra small component per card.

## Migration Notes

No data, schema or configuration change. Rollback = revert the phase commits; each phase leaves the app consistent, so a partial rollback to any phase boundary is safe. When `feature/test-plan` and this branch are both merged to `main`, whichever lands second resolves `Import.razor` only if the merge seam rule was broken.

## References

- Research: `context/changes/ui-redesign/research.md`
- Design source of truth: `context/changes/ui-redesign/design/README.md` and `design/*.dc.html` (shared `<style>` identical across artboards; `System.dc.html` for tokens and components)
- Canvas: https://claude.ai/code/artifact/4ebbcf9c-d18c-44f6-b33b-fa92318f7615
- Bootstrap variable plumbing: `wwwroot/lib/bootstrap/dist/css/bootstrap.css:~128` (dark block), `:3056-3060` (buttons), `:4932-4935` (alerts), `:5399` (btn-close)
- Hooks: `feature/test-plan:tests/10xNotes.Tests/{NoteEditorTests,MaterialsDetailTests,AccountLoginTests,AccountRegisterTests,MaterialsImportTests}.cs`
- Prior modal/no-Bootstrap-JS decisions: `context/archive/2026-09-08-delete-note/plan.md`
- Lessons: `context/foundation/lessons.md` ("Give the markup its state before the first await")

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Theme foundation — tokens, Bootstrap mapping, primitives, Icon

#### Automated

- [x] 1.1 Solution builds: `dotnet build` — 9ee7f32
- [x] 1.2 Test suite passes: `dotnet test` (incl. `DataAccessBoundaryTests` — `Icon` injects nothing) — 9ee7f32
- [x] 1.3 `Components/App.razor` contains `lang="pl"`, `data-bs-theme="dark"` and the `fonts.googleapis.com/css2?family=Geist` link — 9ee7f32
- [x] 1.4 `grep -rn "(MarkupString)" Components` matches only `Components/Notes/NoteEditor.razor` — 9ee7f32
- [x] 1.5 `grep -n "<title" Components/Shared/Icon.razor` returns nothing and `Icon.razor` has no `@inject` — 9ee7f32
- [x] 1.6 `grep -c "\-\-bs-btn-bg" wwwroot/app.css` ≥ 6 (every variant in the table is mapped) — 9ee7f32

#### Manual

- [ ] 1.7 Every route renders on the dark background in Geist, with no light-theme remnant on buttons, inputs, alerts, list groups, the btn-group toggles or the delete modal
- [ ] 1.8 Focus rings on buttons and inputs are the violet soft ring; disabled buttons and inputs are visibly dimmed
- [ ] 1.9 A failed login shows the dark red alert; an invalid field shows the red ring and red message

> P1 implementation notes (non-interactive run): manual rows 1.7–1.9 are left for a logged-in browser pass. Small additions to the P1 contract, all inside `wwwroot/app.css`: the alert child rule is `.alert > :not(svg):not(.btn-close):not(.btn)` so the legacy direct-child buttons of the Materials/Detail warning alert keep their width until P4; `pre code` reset (Markdig fenced code in the preview must not get the inline-chip look); `--bs-modal-header-padding: 24px 24px 0` and `.modal-footer { gap }` with child margins zeroed; list-group action hover vars; `.seg-item` disabled/focus states; `prefers-reduced-motion` stops `.stream-bar`/`.caret`; the 640 px `.page-head` rule uses a 999:1 grow ratio so actions stretch only once wrapped; file-input selector button sized to the 40 px field. Icon's comment avoids the literal `<title` so check 1.5 stays empty. Per the run's one-commit rule the P1 rows carry no SHA suffix; the P2 commit may back-fill it.

### Phase 2: App shell and auth layout

#### Automated

- [x] 2.1 Solution builds: `dotnet build`
- [x] 2.2 Test suite passes: `dotnet test`
- [x] 2.3 Hook check: the `feature/test-plan` suite passes in the throwaway worktree — `AccountLoginTests` and `AccountRegisterTests` included
- [x] 2.4 `#blazor-error-ui` is in `App.razor` and no longer in `MainLayout.razor`
- [x] 2.5 `@layout AuthLayout` appears in exactly `Login.razor` and `Register.razor`
- [x] 2.6 `grep -rn "@onclick" Components/Layout Components/Pages/Account` returns nothing
- [x] 2.7 `navbar-toggler` immediately precedes `nav-scrollable` in `NavMenu.razor`, and `NavLinkMatch.All` appears twice
- [x] 2.8 ReconnectModal still carries its three element ids

#### Manual

- [ ] 2.9 At 1440 px the sidebar matches `Main.dc.html` (brand, icon nav, violet active item, user block, footer links, no "About" row)
- [ ] 2.10 Active states: `/materials/import` lights only "Dodaj materiał", `/materials/{id}` lights nothing, `/materials` lights only "Moje materiały"
- [ ] 2.11 A 200-character display name truncates without widening the sidebar; with no display name the e-mail shows once
- [ ] 2.12 At ~400 px the top bar + slide-out drawer opens, closes on link tap and on scrim tap, on static and interactive pages
- [ ] 2.13 `/login` and `/register` match `Login.dc.html`, collapse to the brand row at ~400 px, and show dark alerts, red validation and a working sign-in
- [ ] 2.14 The reconnect dialog appears in the dark design when the server stops and recovers on Retry
- [ ] 2.15 The un-hidden `#blazor-error-ui` shows the dark error bar with the X icon

> P2 implementation notes (non-interactive run): manual rows 2.9–2.15 are left for a browser pass. Hook check 2.3 ran before the commit instead of after it: a detached worktree at `/tmp/10xDevs3-hookcheck` (not `../10xDevs3-hookcheck`, which is outside the repo) at the P1 tip, `feature/test-plan` merged in (no conflicts), the P2 working-tree files copied over — `feature/test-plan` touches none of them — then `dotnet test`: 387 passed (9 `AccountLogin`/`AccountRegister`); worktree removed. Same result as the post-commit recipe without needing an amend. Small additions to the P2 contract: AuthLayout groups the art and copy as `div.auth-hero-body > (div.auth-art + div.auth-hero-copy)` (the artboard's own grouping: brand / body / hint with `space-between`), the bars are `span.bar` with inline widths, the draft card reuses the global `.dot`; the auth submit spacing is `.auth-panel .form-actions { margin-top: 10px; padding-top: 0 }` in `app.css` (18 + 10 = the artboard's 28 px); `.brand` and the nav links get the soft violet `:focus-visible` ring; the mobile drawer's transition is dropped under `prefers-reduced-motion`; NavMenu gains a comment above the checkbox worded without the class names so check 2.7's grep stays two lines; the kept `.nav` comment now says 248px (the sidebar width) instead of 250px; `MainLayout.razor` lost its UTF-8 BOM in the rewrite. Per the run's one-commit rule the P2 rows carry no SHA suffix; the P3 commit may back-fill it.

### Phase 3: Lists and import

#### Automated

- [ ] 3.1 Solution builds: `dotnet build`
- [ ] 3.2 Test suite passes: `dotnet test`
- [ ] 3.3 Hook check: the `feature/test-plan` suite passes in the throwaway worktree, and its merge of `Import.razor` completes without conflicts
- [ ] 3.4 The three `#title` merge-seam lines in `Import.razor` are unchanged against `main`
- [ ] 3.5 `#file`, `#paste` and `#title` are present in `Import.razor`, and `d-none` appears twice
- [ ] 3.6 Neither index page declares a `@rendermode`
- [ ] 3.7 `grep -rn "list-group" Components` returns nothing

#### Manual

- [ ] 3.8 `/` matches `Main.dc.html`: card grid, hover glow, card opens the note, source link opens the material, header CTA opens import
- [ ] 3.9 A fresh account shows the dashed empty cards on `/` and `/materials` with working links
- [ ] 3.10 `/materials` matches `Materials.dc.html`: headers, source badges, verb + date, note-status badges, truncation
- [ ] 3.11 At ~400 px the notes grid is one column and material rows stack
- [ ] 3.12 `/materials/import` matches `Import.dc.html` in both modes; switching modes keeps the paste text and the chosen file
- [ ] 3.13 The dropzone opens the picker on click, accepts a dropped `.md`, shows the file-name badge, auto-fills the title and shows a focus ring
- [ ] 3.14 Empty title shows the red ring and message; a valid save shows the spinner and lands on the material page

### Phase 4: Detail pages and the note editor

#### Automated

- [ ] 4.1 Solution builds: `dotnet build`
- [ ] 4.2 Test suite passes: `dotnet test`
- [ ] 4.3 Hook check: the `feature/test-plan` suite passes in the throwaway worktree — `NoteEditorTests` and `MaterialsDetailTests` included
- [ ] 4.4 `grep -rn "(MarkupString)" Components` still matches only `Components/Notes/NoteEditor.razor`
- [ ] 4.5 The number of `<button` elements in `Materials/Detail.razor` is unchanged
- [ ] 4.6 `<h1>Nie znaleziono materiału</h1>` is present verbatim in `Materials/Detail.razor`
- [ ] 4.7 NoteEditor has exactly one `div.form-text`, the counter, before the textarea
- [ ] 4.8 `btn-primary` in NoteEditor matches only the save button
- [ ] 4.9 `grep -rnE 'data-bs-(dismiss|toggle)="|IJSRuntime' Components` returns nothing
- [ ] 4.10 Both detail pages' scoped `.razor.css` files are removed

#### Manual

- [ ] 4.11 A material with no note matches `Material.dc.html` `stan=pusty` with one primary "Generuj notatkę" in the pane head
- [ ] 4.12 Generating shows the stream bar, busy button, caret and read-only text, then the draft editor with a secondary pane-head button
- [ ] 4.13 The draft editor matches the artboard and the preview typography renders without the box changing size
- [ ] 4.14 The replace confirmation is amber with "Zastąp notatkę" and a secondary "Anuluj" that removes it
- [ ] 4.15 `/notes/{id}` matches `Note.dc.html`, including the saved badge after "Zapisz zmiany"
- [ ] 4.16 The delete dialog matches `dialogUsuwania`, traps focus, closes on Escape, and a confirmed delete lands on the dismissible notice
- [ ] 4.17 Unknown material and note ids show the not-found cards
- [ ] 4.18 Both detail pages stack source above note at ~400 px and below 992 px

### Phase 5: Account and system pages, responsive pass

#### Automated

- [ ] 5.1 Solution builds: `dotnet build`
- [ ] 5.2 Test suite passes: `dotnet test`
- [ ] 5.3 Full hook check: the entire `feature/test-plan` suite passes in the throwaway worktree
- [ ] 5.4 Profile still carries all three `FormName`s
- [ ] 5.5 `grep -rn "(MarkupString)" Components` matches only `Components/Notes/NoteEditor.razor`
- [ ] 5.6 No `@onclick` on any static-SSR page
- [ ] 5.7 Template leftovers (`text-secondary small`, `list-group`, `bi-*-nav-menu`) are gone

#### Manual

- [ ] 5.8 `/profile` matches `Profile.dc.html` and its three forms submit independently
- [ ] 5.9 `/logout`, `/not-found` and `/Error` show their cards in the dark design
- [ ] 5.10 Every route at ~400 px has no horizontal scroll and readable, truncating heads, badges and crumbs
- [ ] 5.11 Every route at 1440 px matches its artboard within a few pixels
