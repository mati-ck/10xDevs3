# UI redesign — Plan Brief

> Full plan: `context/changes/ui-redesign/plan.md`
> Research: `context/changes/ui-redesign/research.md`
> Design: `context/changes/ui-redesign/design/README.md` + `design/*.dc.html`

## What & Why

Restyle the whole 10xNotes app to the Claude Design mockups: dark only, modern SaaS, one violet accent (`#8C7BFF`), Geist + Geist Mono, inline SVG icons. The app today is the stock Blazor template (blue gradient sidebar, Helvetica, default Bootstrap); the redesign makes it look like a product without changing what it does.

## Starting Point

One 59-line template `app.css`, six scoped sheets, stock Bootstrap 5.3.3 CSS with no Bootstrap JS, data-URI nav icons, `lang="en"`. Pages are plain Bootstrap stacks (`list-group`, `row/col-lg-6`, `btn-group` toggles). The bUnit suite that pins markup hooks lives on the unmerged `feature/test-plan` branch.

## Desired End State

Every route renders in the dark design: a 248 px sidebar with icon nav and a user block (CSS-only slide-out drawer on phones), a two-column auth layout with a hero for login/register, a notes card grid, a materials table with source/status badges, an import card with a segmented switch and a dropzone, two-pane material/note pages with breadcrumbs and a restyled editor and delete dialog, and profile settings rows with a danger zone. Copy, behaviour, render modes and services are unchanged, and the `feature/test-plan` suite still passes against the new markup.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Theme mechanism | Keep Bootstrap; `data-bs-theme="dark"` + `--bs-*` theme vars **and** per-component `--bs-btn/alert/modal/card-*` overrides | Bootstrap 5.3.3 hard-codes button colours per variant, so theme vars alone leave blue buttons. | Research |
| Fonts / icons | Google Fonts `<link>`; one `Icon` Razor component (enum names, literal SVG, no `MarkupString`, culture-safe stroke width) | Locked by the user; keeps the note preview the only `MarkupString`. | README / Plan |
| Copy conflicts | Razor copy wins; only subtitles, breadcrumbs, badges, auth hero, profile hints and the dropzone line are added | Design README locks copy 1:1. | README / Research |
| Test hooks | Every hook wins over mockup anatomy (ids, first `btn-primary`, first `div.form-text` counter, exact not-found `h1`, unique button texts) | The bUnit suite on `feature/test-plan` would silently break otherwise. | Research / Orchestrator |
| Generate button | Exactly one, in the note pane head, in every state | Tests select buttons by text with `.Single()`. | Research |
| "Usuń notatkę" | Moves to the note page header (danger-soft) | Mockup; still far from "Zapisz zmiany". | Research |
| Auth layout | New `AuthLayout` on Login/Register only; `#blazor-error-ui` moves to `App.razor` | Both layouts need the error bar; Logout/Error/NotFound stay in the shell. | Research |
| Mobile | Existing checkbox toggler drives a CSS-only drawer with a pseudo-element scrim; 641 px breakpoint kept; panes stack below 992 px | No new JS; the inline `onclick` already closes the menu. | README / Plan |
| Import file mode | Dropzone look around the native `InputFile` stretched transparently over it; chosen file name shown as a badge | Click and native drop work with no JS; restores the feedback the hidden input loses. | Orchestrator / Plan |
| Toggles | Edycja/Podgląd and Wklej/Plik become `.seg` buttons, not `.btn` | Mockup look, and the editor's first `btn-primary` stays the save button. | Plan |
| Verification | `dotnet build` + `dotnet test` every phase; `feature/test-plan` merged into a throwaway worktree after P2–P5; visual pass per route | Nothing on this branch reads markup. | Orchestrator |

## Scope

**In scope:** `App.razor`, `app.css` rewrite, `Icon` component, MainLayout/NavMenu/ReconnectModal restyle, new AuthLayout, all pages' markup/classes (Notes, Materials, Import, both detail pages, NoteEditor, Login, Register, Profile, Logout, Error, NotFound), `lang="pl"`.

**Out of scope:** services, data, validators, `@code` logic, render modes, routes, new features (quota meter, search), light theme, Bootstrap JS / new JS, merging `feature/test-plan`, push.

## Architecture / Approach

Tokens and shared primitives live in `wwwroot/app.css` (scoped CSS cannot be shared across components); only layout components and the editor's preview typography keep scoped sheets. Mockup values are mapped onto Bootstrap classes (`.btn-*`, `.form-control`, `.alert-*`, `.card`, `.badge`, `.modal-*`) plus a few non-colliding primitives (`.page-head`, `.crumbs`, `.seg`, `.pane*`, `.icon-tile`, `.empty-state`, …) — the mockup CSS is never lifted verbatim because its class names collide with Bootstrap's.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Theme foundation | Dark tokens, Bootstrap mapping, primitives, `Icon`, fonts, `lang="pl"` | A missed Bootstrap variant leaves a light remnant |
| 2. Shell + auth layout | Sidebar, user block, mobile drawer, error bar, reconnect dialog, AuthLayout + Login/Register | Breaking the checkbox `~` selector or `FocusOnNavigate` |
| 3. Lists + import | Notes grid, materials table, import card with dropzone | Merge conflict with `feature/test-plan` on `Import.razor`'s title field |
| 4. Detail pages + editor | Two panes, breadcrumbs, editor, stream state, delete dialog | Densest test hooks (button texts, counter, first `btn-primary`) |
| 5. Account/system + responsive | Profile settings rows, Logout/Error/NotFound cards, 400 px pass | Layout overflow on narrow screens |

**Prerequisites:** branch `feature/ui-redesign` checked out on the Mac; `dotnet` 10 at `/opt/homebrew/bin/dotnet`; user-secrets configured for `dotnet run`; `feature/test-plan` available locally for hook checks.
**Estimated effort:** ~5 implementation sessions, one per phase.

## Open Risks & Assumptions

- The hook check only runs after the phase commit (the worktree checks out the branch tip); a failure means amending the phase commit.
- `color-mix()` / `backdrop-filter` assume a current evergreen browser.
- Visual QA of the streaming state spends the daily generation quota — one run per pass.
- A hidden native file input loses the browser's own file label; the file-name badge replaces it.

## Success Criteria (Summary)

- Every route matches its artboard at 1440 px and stays usable at ~400 px.
- `dotnet test` stays green on the branch and the `feature/test-plan` suite passes against the redesigned markup.
- No behaviour, copy, render mode or data path changed; the note preview remains the only `MarkupString`.
