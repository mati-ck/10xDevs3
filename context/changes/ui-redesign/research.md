---
date: 2026-09-11T15:00:43Z
researcher: Mateusz Pajewski
git_commit: 4c8279d119622549b953c43dac81177aae411e39
branch: feature/ui-redesign
repository: 10xDevs3
topic: "UI redesign — styling surface, per-page anatomy, test hooks and guardrails the restyle must respect"
tags: [research, codebase, ui, bootstrap, blazor, layout, navmenu, bunit, css-isolation]
status: complete
last_updated: 2026-09-11
last_updated_by: Mateusz Pajewski
---

# Research: UI redesign — what the restyle touches, and what it must not break

**Date**: 2026-09-11T15:00:43Z
**Researcher**: Mateusz Pajewski
**Git Commit**: `4c8279d` (= `main` = `origin/main`; `feature/ui-redesign` has no commits of its own yet)
**Branch**: `feature/ui-redesign`
**Repository**: 10xDevs3 (`mati-ck/10xDevs3`)

## Research Question

Ground the `ui-redesign` change (restyle the whole app to the `design/*.dc.html` mockups: dark
only, Bootstrap 5.3 kept and re-themed via `data-bs-theme="dark"` + `--bs-*`, copy and behavior
1:1) in the current code:

1. the styling surface today (App, app.css, layout, scoped CSS, ReconnectModal, `#blazor-error-ui`,
   Bootstrap version, icons);
2. per page: route, render mode, layout, Bootstrap classes and structure, Blazor-driven interactions;
3. how auth pages can get a sidebar-less layout;
4. every selector / id / class / element / literal text tests depend on — on this branch and on
   `feature/test-plan` (bUnit);
5. security/behavior guardrails embedded in markup;
6. gaps between mockups and app (missing artboards, data the mockups need);
7. risks, phase boundaries and a verification approach.

## Summary

Seven findings shape the plan:

1. **The styling surface is small and mostly scaffold.** One global sheet (`wwwroot/app.css`, 60
   lines of template defaults), six scoped sheets, stock Bootstrap **v5.3.3** CSS only
   (`wwwroot/lib/bootstrap/dist/css/bootstrap.min.css`, header confirms 5.3.3), **no Bootstrap JS,
   no Bootstrap Icons font** — the nav "icons" are data-URI SVG backgrounds in
   `NavMenu.razor.css:27-63`. No CSP is configured, so a Google Fonts `<link>` works as-is.

2. **Re-theming through `--bs-*` alone will not restyle buttons.** Bootstrap 5.3.3 hard-codes
   component variables for buttons (`.btn-primary { --bs-btn-bg: #0d6efd; … }`,
   `bootstrap.css:3056-3060`) instead of deriving them from `--bs-primary`. Alerts, forms, list
   groups and modals *do* read theme variables (`.alert-danger` → `--bs-danger-bg-subtle`,
   `bootstrap.css:4932-4935`). So the plan needs two layers in `app.css`: theme tokens under
   `[data-bs-theme=dark]` **and** per-component `--bs-btn-*` / `--bs-alert-*` / `--bs-modal-*`
   overrides.

3. **Tests pin far more markup than the brief's list.** The brief names `div.alert-danger`,
   `div.alert-warning`, `button.btn-primary`, `div.form-text`, `#email`, `#password`,
   `#note-title`, `#note-content`, `form`. On `feature/test-plan` the bUnit suite additionally
   depends on: `#title` (+ its `maxlength`), **exact button text** with `.Single()` among *all*
   `<button>`s of `Materials/Detail` (so no second "Generuj notatkę"/"Anuluj" button may ever be
   rendered), **exact `h1` text** `"Nie znaleziono materiału"` with no trim, the **first**
   `div.form-text` in `NoteEditor` being the character counter, the **first** `button.btn-primary`
   in `NoteEditor` being save, and the counter printing **unformatted** digits
   (`"{Length} / 65536"`) — which contradicts the mockup's `612 / 65 536 znaków`.

4. **Auth layout is a one-line opt-in per page.** `Routes.razor:3` sets
   `DefaultLayout="typeof(Layout.MainLayout)"`; a page-level `@layout` overrides it (the precedent
   already exists: `NotFound.razor:3`). Nothing in Login/Register depends on MainLayout except that
   `#blazor-error-ui` lives there (`MainLayout.razor:19-23`); bUnit renders pages without layouts,
   so tests are unaffected.

5. **Guardrails are all local to markup and all survive a restyle if the elements stay**:
   source as text in `<pre>` (`Materials/Detail.razor:73`, `Notes/Detail.razor:62`), the single
   `MarkupString` (`NoteEditor.razor:34`), read-only streaming `<pre>` (`Materials/Detail.razor:151`),
   the hand-rolled modal with focus sentinels (`Notes/Detail.razor:103-159`), Blazor-dismissed alert
   (`Materials/Detail.razor:89-93`), `disabled` bindings on every mutating control, and
   `d-none` panels on Import (`Import.razor:56,79`).

6. **No mockup needs new data.** Every value the artboards show is already loaded by the page
   (`NoteListItem`, `SourceMaterialListItem`, entity rows, auth claims). The sidebar user block
   (name + e-mail + initial) can be fed from existing claims (`AuthCookie.cs:72-73,144`). Nothing in
   scope needs a service/data change. Where mockup copy conflicts with Razor copy, the Razor copy wins
   (README locked decision), except for the explicitly allowed additions.

7. **Verification gap: this branch has no component tests.** The bUnit suite lives only on
   `feature/test-plan` (10 commits ahead of `main`, not merged). `dotnet test` here proves nothing
   about markup; hook preservation has to be checked by running the `feature/test-plan` suite against
   the redesigned components in a throwaway worktree. User-secrets are configured
   (`ConnectionStrings:Postgres` and `Ai:ApiKey` present), so the app can be run for visual checks.

## Detailed Findings

### 1. Styling surface today

**Host page** — `Components/App.razor`
- `App.razor:2` `<html lang="en">` — no `data-bs-theme`; the app is light-only today. (`lang="en"`
  in a Polish app — see Open Questions.)
- `App.razor:9-11` load order: `bootstrap.min.css` → `app.css` → `10xnotes.styles.css` (scoped-CSS
  bundle). Overrides in `app.css` win on equal specificity; scoped CSS wins last.
- `App.razor:18-20` `<Routes />`, `<ReconnectModal />`, and the only script, `blazor.web.js`.
  Bootstrap's JS ships in `wwwroot/lib/bootstrap/dist/js` but is never referenced.

**Global CSS** — `wwwroot/app.css` (template leftovers)
- `:1-3` Helvetica font stack; `:5-13` link colour `#006bb7`, `.btn-primary` blue `#1b6ec2`;
  `:15-17` focus ring `0 0 0 .1rem white, 0 0 0 .25rem #258cfb` on `.btn`, `.form-control`,
  `.form-check-input`; `:23-25` `h1:focus { outline: none }` (needed by `FocusOnNavigate`,
  `Routes.razor:8`); `:27-37` Blazor validation classes `.valid.modified` (green outline),
  `.invalid` (red outline), `.validation-message` (red); `:39-47` `.blazor-error-boundary` (no
  `ErrorBoundary` is used anywhere in `Components/`); `:49-60` unused `.darker-border-checkbox`
  and `.form-floating` rules.

**Layout** — `Components/Layout/MainLayout.razor` + `.razor.css`
- Structure `MainLayout.razor:3-17`: `div.page > div.sidebar > <NavMenu/>` + `main > div.top-row.px-4
  (English "About" link to learn.microsoft.com) + article.content.px-4 > @Body`.
- `MainLayout.razor:19-23` `#blazor-error-ui` (English copy, `a.reload`, `span.dismiss` with the
  emoji `🗙`). `blazor.web.js` wires `.dismiss`/`.reload` by class inside `#blazor-error-ui` — keep
  the id and both classes; the glyph can become an inline SVG.
- Breakpoints `MainLayout.razor.css:39-77`: `max-width: 640.98px` / `min-width: 641px`. Above 641 the
  sidebar is `250px`, `height:100vh; position: sticky` and `.top-row` is sticky. Sidebar background
  is the template's blue→purple gradient (`:11-13`).
- `#blazor-error-ui` styling `MainLayout.razor.css:79-98` (light yellow, `color-scheme: light only`,
  `display:none` until Blazor shows it). Because it is scoped to MainLayout, a new AuthLayout would
  need its own copy — or the element moves to `App.razor` with its CSS in `app.css`.

**Nav** — `Components/Layout/NavMenu.razor` + `.razor.css`
- Brand row `NavMenu.razor:3-7` (`.top-row.navbar.navbar-dark > a.navbar-brand "10xnotes"`).
- **Checkbox toggler** `NavMenu.razor:9` `<input type="checkbox" class="navbar-toggler">`;
  `.nav-scrollable` `NavMenu.razor:11` carries a plain DOM `onclick="document.querySelector('.navbar-toggler').click()"`
  (not Blazor `@onclick`), which closes the menu after a tap and works on static SSR pages.
  CSS: hidden by default, shown via `.navbar-toggler:checked ~ .nav-scrollable`
  (`NavMenu.razor.css:124-130`); at `min-width: 641px` the toggler is hidden and the nav always
  shown, scrolling inside `calc(100vh - 3.5rem)` (`:132-145`). The sibling selector means the
  checkbox must stay a **preceding sibling** of `.nav-scrollable`.
- Links `NavMenu.razor:16,25,31,44,52,59,65`: `NavLink` with `Match="NavLinkMatch.All"` on `/` and
  `materials` (comment `:21-23` — prefix matching would light "Moje materiały" on `/materials/import`
  and `/materials/{id}`). Active state is Bootstrap-less: `NavLink` adds `.active`, styled with
  `::deep a.active` (`NavMenu.razor.css:90-93`).
- Sign-out is a **link to `/logout`**, not a form (`NavMenu.razor:49-55`) — the POST lives on the
  static-SSR Logout page.
- User label `NavMenu.razor:39-41` `div.nav-item.nav-user` = `AuthCookie.DisplayNameOrEmail(context.User)`
  (plain interpolation + `title`). It depends on `.nav { flex-wrap: nowrap }` (`NavMenu.razor.css:100-112`,
  long comment: without it a 200-char display name widens the sidebar) and on
  `overflow:hidden; text-overflow: ellipsis` (`:116-122`). Any new user block must keep a
  truncation path.
- Icons `NavMenu.razor.css:27-63`: `.bi` + `.bi-*-nav-menu` data-URI SVG backgrounds (fill white).
  These are the **only** icon usage in the app; there is no Bootstrap Icons font or package.

**Reconnect UI** — `Components/Layout/ReconnectModal.razor` (+ `.css`, `.js`)
- `<dialog id="components-reconnect-modal">` with state classes `components-reconnect-*-visible` and
  buttons `#components-reconnect-button`, `#components-resume-button`
  (`ReconnectModal.razor:3-31`). The JS module keys on those three ids and on the class names
  Blazor toggles (`ReconnectModal.razor.js:2-9,55`). Copy is English. CSS is white card, blue
  button, blue rings (`ReconnectModal.razor.css:23-30,91-119`). Restyle = colours/radii only; ids
  and class names are a contract with `blazor.web.js`.

**Scoped CSS files (all six)**
- `Layout/MainLayout.razor.css`, `Layout/NavMenu.razor.css`, `Layout/ReconnectModal.razor.css` (above).
- `Notes/NoteEditor.razor.css:1-43` — `.note-source` (mono, `60vh` fixed box), `.note-preview`
  (same box, `overflow-y:auto`), `::deep` heading scaling, `::deep pre` wrap.
- `Pages/Materials/Detail.razor.css:1-18` — `.source-content, .note-content` (`max-height:60vh`,
  `pre-wrap`, `var(--bs-secondary-bg, #f8f9fa)`), `.note-content` transparent.
- `Pages/Notes/Detail.razor.css:1-11` — a deliberate duplicate of `.source-content`. Scoped CSS
  cannot be shared between components, so shared pane/badge/segmented/crumb styles belong in
  `app.css`, not in per-page files.

### 2. Per page / component

| Component | Route | Render mode | Layout | Structure & Bootstrap classes | Blazor-driven interaction |
|---|---|---|---|---|---|
| `Pages/Notes/Index.razor` | `/` | **static SSR** (`:11-17`) | Main | `h1`; `p.text-danger` load failure (`:29-31`); empty `p.text-secondary` + plain `<a href="/materials">` (`:37-41`); `ul.list-group > li.list-group-item > a.fw-semibold` + `div.text-secondary.small` with date and `a` "Materiał źródłowy" (`:45-60`) | none |
| `Pages/Materials/Index.razor` | `/materials` | **static SSR** (`:12`) | Main | `h1`; `p.text-danger` (`:22-24`); empty `p.text-secondary` + `<a href="/materials/import">` (`:28-30`); `ul.list-group` rows: `a.fw-semibold` title, `div.text-secondary.small` branched on `Kind` — "Wklejono {date}" vs "Zaimportowano {date} z pliku `<code>`{name}`</code>`" (`:49-59`), then " · Otwórz notatkę" link or " · Brak notatki" (`:65-72`, whitespace-inside-`<text>` comment `:61-64`) | none |
| `Pages/Materials/Import.razor` | `/materials/import` | **InteractiveServer** (`:2`) | Main | `h1` + `p.text-secondary` intro (`:23-28`); `div.alert.alert-danger` (`:30-33`); mode switch `div.btn-group.btn-group-sm` of `btn-secondary`/`btn-outline-secondary` (`:38-47`); `EditForm` (no FormName, interactive) with two panels kept in DOM and hidden via `d-none` (`:56,79`); `textarea#paste.form-control` `maxlength`, `@onchange`, `disabled=isSaving` (`:68-70`); counter `div.form-text` N0 Polish (`:72-76`); `InputFile#file.form-control` (`:81`); `InputText#title.form-control` (`:89`) + `div.form-text` + `ValidationMessage.text-danger` (`:90-95`); submit `button.btn.btn-primary` `disabled=isSaving`, label swaps to "Zapisywanie…" (`:98-100`) | mode toggle, paste change, file change, submit |
| `Pages/Materials/Detail.razor` | `/materials/{Id:guid}` | **InteractiveServer** (`:2`) | Main | not-found: `h1` "Nie znaleziono materiału" + `p` + link (`:32-37`); `h1` title; `p.text-secondary` provenance branched on Kind (`:48-60`); `div.row.g-4 > div.col-lg-6 ×2` (`:65-160`); `h2.h5` headings; source `pre.source-content.border.rounded.p-3` (`:73`); deletion notice `div.alert.alert-success.d-flex` + `button.btn-close` via `@onclick` (`:89-93`); saved-note `p.text-secondary` + link (`:101-104`); generate `button.btn.btn-primary.mb-3` (`:107-110`); `div.alert.alert-danger` (`:112-115`); confirmation `div.alert.alert-warning` with `button.btn-warning.btn-sm` + `button.btn-outline-secondary.btn-sm` "Anuluj" (`:117-132`); `<NoteEditor>` when `draft` (`:134-144`); streaming `pre.note-content.border.rounded.p-3` (`:145-152`); hint `p.text-secondary` (`:153-158`) | generate/stream, confirm gates, editor, dismiss |
| `Pages/Notes/Detail.razor` | `/notes/{Id:guid}` | **InteractiveServer** (`:2`) | Main | not-found `h1` + `p` + link (`:25-32`); `h1` title + `p.text-secondary` "Zapisano {date}." (`:35-41`); `row g-4 / col-lg-6` (`:46-94`); source link + `pre.source-content` (`:56-63`) or "Materiał źródłowy nie jest już dostępny." (`:52`); `div.alert.alert-success` saved message (`:69-72`); `<NoteEditor>` (`:77-82`); `hr.my-4` + `button.btn.btn-outline-danger` "Usuń notatkę" (`:87-92`); manual modal `div.modal.d-block[role=dialog][aria-modal][aria-labelledby]` + `@onkeydown` (`:103-105`), focus sentinels `span.visually-hidden[tabindex=0]` (`:114,:156`), `modal-dialog-centered > modal-content > modal-header/body/footer` (`:116-154`), `div.alert.alert-danger.mb-0` inside (`:135`), `btn-outline-secondary` "Anuluj" `@ref=cancelButton` + `btn-danger` `@ref=confirmButton` (`:142-151`), `div.modal-backdrop.show` (`:159`) | editor save, delete dialog open/Escape/focus trap/delete |
| `Notes/NoteEditor.razor` | — (child) | inherits (interactive) | — | `label` + `input#note-title.form-control` `maxlength`, `@onchange`, `disabled=IsBusy` (`:11-15`); `div.btn-group.btn-group-sm` Edycja/Podgląd (`:17-26`); preview `div.note-preview.border.rounded.p-3` with the **only** `MarkupString` (`:28-35`); `textarea#note-content.form-control.note-source` (`:45-47`); counter `div.form-text.mb-3` `@Content.Length / @NoteValidator.MaxContentLength znaków` (`:50-52`); `div.alert.alert-danger` (`:54-57`); save `button.btn.btn-primary` (`:59-61`) | title/content change, toggle, save |
| `Pages/Account/Login.razor` | `/login` | **static SSR** (`:14-15`) | Main | `h1` "Zaloguj się"; `div.alert.alert-danger` (`:21-24`); `EditForm method=post FormName="login"` (`:26`); `div.mb-3 > label.form-label + InputText#email.form-control + ValidationMessage.text-danger` (`:29-33`); `#password` (`:35-39`); `button.btn.btn-primary` (`:41`); `p.mt-3` "Nie masz konta? Zarejestruj się" (`:44`) | form POST only |
| `Pages/Account/Register.razor` | `/register` | **static SSR** (`:8`) | Main | same anatomy as Login; extra `div.form-text` with `PasswordRequirementAttribute.Hint` (`:31`); cross link (`:38`) | form POST only |
| `Pages/Account/Logout.razor` | `/logout` | **static SSR** (`:5-7`) | Main | `h1`, `p`, `form method=post @formname="logout"` + `AntiforgeryToken` + `button.btn.btn-primary` (`:15-18`) | form POST only |
| `Pages/Account/Profile.razor` | `/profile` | **static SSR** (`:14-20`) | Main | `h1`; four `section.mb-5` (`:26-125`): `dl.row` e-mail + `div.form-text`; display-name form (`FormName="display-name"`, `#display-name`, `maxlength`, `btn-primary`); password form (`FormName="change-password"`, `#current-password`, `#new-password`, `alert-success`/`alert-danger`); danger zone `section.border.border-danger.rounded.p-3` + `h2.text-danger`, `#delete-password`, `button.btn-danger` (`:98-125`) | three independent form POSTs |
| `Pages/Error.razor` | `/Error` | static SSR (via `UseExceptionHandler`, `Program.cs:205`) | Main (default) | `h1.text-danger`, `h2.text-danger`, `code` request id, English dev-mode text (`:9-30`) | none |
| `Pages/NotFound.razor` | `/not-found` | static SSR (via `UseStatusCodePagesWithReExecute`, `Program.cs:209`) | **explicit** `@layout MainLayout` (`:3`) | `h3` + `p`, English (`:9-10`) — no `h1` | none |
| `Account/RedirectToLogin.razor` | — | — | — | no markup; `NavigateTo("/login?ReturnUrl=…")` (`:7-15`) | — |

**Must keep working without JS**: every static-SSR page (Notes/Index, Materials/Index, Login,
Register, Logout, Profile, Error, NotFound) — there is no circuit, so any `@onclick` added there
would be dead. The only client-side behaviours outside Blazor today are the nav checkbox (pure
CSS + one inline `onclick` attribute) and `ReconnectModal.razor.js`. The mobile hamburger must stay
CSS-only (README locked decision).

### 3. A sidebar-less layout for auth pages

- `Routes.razor:3` `AuthorizeRouteView … DefaultLayout="typeof(Layout.MainLayout)"`; a component-level
  `@layout X` (a `LayoutAttribute`) takes precedence over `DefaultLayout`. `NotFound.razor:3` already
  uses `@layout MainLayout`, so the pattern is established. Works identically under static SSR.
- Recommended: new `Components/Layout/AuthLayout.razor` (+ `.razor.css`) and `@layout AuthLayout` on
  `Login.razor` and `Register.razor` only. The mockup's left hero panel (brand, two illustrative
  cards, headline, lead, privacy hint) lives in the layout; the page supplies `h1`, subtitle link and
  form.
- Dependencies on MainLayout wrapping them: (a) `#blazor-error-ui` (`MainLayout.razor:19-23`) — move
  it to `App.razor` (and its CSS to `app.css`) so every layout gets it, or duplicate it; (b) the
  anonymous NavMenu links "Zaloguj się"/"Zarejestruj się" (`NavMenu.razor:57-69`) disappear on these
  pages — acceptable, the pages already cross-link (`Login.razor:44`, `Register.razor:38`);
  (c) `FocusOnNavigate Selector="h1"` (`Routes.razor:8`) focuses the **first** `h1` — the layout's
  hero headline must not be an `h1`.
- Nothing else depends on the layout: login/register code uses only `HttpContext`,
  `NavigationManager` and injected services. bUnit tests render the page component directly
  (`ctx.Render<LoginPage>`, `AccountLoginTests.cs:126`), which ignores `@layout`.
- Enhanced navigation between a MainLayout page and an AuthLayout page (e.g. `NavigateTo("/login")`
  after logout `Logout.razor:27`, after account deletion `Profile.razor:355`, or from
  `RedirectToLogin`) patches the whole document, so switching layouts needs nothing special.

### 4. Test hooks

**Current branch (`feature/ui-redesign` = `main`)** — no component tests. Tests touching components
are reflection-only:
- `DataAccessBoundaryTests.cs:30-101` — scans compiled types (incl. components' `[Inject]`
  properties) for `DbContext` / `IDbContextFactory<>`. It does **not** read `.razor` sources. Any new
  component (Icon, AuthLayout) is safe as long as it injects neither.
- `NoteMarkdownTests.cs` pins `NoteMarkdown.ToHtml` (the preview's safety), not markup.
- No test on this branch reads `.razor` or `.css` text.

**`feature/test-plan` (bUnit 2.10.3, `tests/10xNotes.Tests/10xNotes.Tests.csproj`)** — component
tests that will run once that branch merges:

| Test file | Hook | What breaks it |
|---|---|---|
| `NoteEditorTests.cs:49,68,112-113,143,156` | `#note-title`, `#note-content` (`Change`, `disabled`, `maxlength`) | renaming ids, removing `maxlength`/`disabled` |
| `NoteEditorTests.cs:87,91,93,114` | **first** `button.btn-primary` in NoteEditor = save; `TextContent.Trim()` == label | making the Edycja/Podgląd toggle `btn-primary`, adding text (e.g. `<title>`) inside the button |
| `NoteEditorTests.cs:128` | `div.alert-danger` `TextContent.Trim()` == message | turning the alert into a non-`div`, adding a dismiss button with text |
| `NoteEditorTests.cs:173-177` | **first** `div.form-text` contains `"{Length} / {MaxContentLength}"` + `"znaków"`, not `"KB"` | adding another `div.form-text` above the counter; making the counter a `span`; formatting with N0 (`65 536`) as the mockup shows |
| `AccountLoginTests.cs:63,76,129-131` / `AccountRegisterTests.cs:54,68,115-117` | `#email`, `#password` (value attr), `form` (`Submit`) | renaming ids, a second `<form>` in the page component |
| `AccountLoginTests.cs:93,108` / `AccountRegisterTests.cs:80,96` | `div.alert-danger` exact trimmed text | text inside an alert icon |
| `MaterialsImportTests.cs:54` | `#title` `maxlength` (added to `Import.razor` on that branch) | renaming `#title` |
| `MaterialsDetailTests.cs:104,122-123,225,246,275,377` | `#note-title`, `#note-content`, value attr | as NoteEditor |
| `MaterialsDetailTests.cs:175,220,272` | `div.alert-warning` contains confirmation text; disappears after Anuluj | changing element/class |
| `MaterialsDetailTests.cs:303-309` | `div.alert-danger` (first) | as above |
| `MaterialsDetailTests.cs:327` | `Find("h1").TextContent` **exactly** `"Nie znaleziono materiału"` (no Trim) | whitespace/newline/icon inside that `h1`, any `h1` earlier in the page |
| `MaterialsDetailTests.cs:396-414` | buttons found by `TextContent.Trim()` and `.Single()` over **all** `<button>`s: "Generuj notatkę", "Zapisz notatkę", "Zastąp zapisaną notatkę", "Zastąp notatkę", "Generuj mimo to", "Anuluj" | a second button with the same text (e.g. mockup's empty-state "Generuj notatkę" CTA *plus* a pane-head one), icon SVGs carrying text, turning buttons into `<a>` |

Notes on the harness: tests render pages without layouts; assertions are semantic, never
`MarkupMatches`, because scoped CSS adds `b-<hash>` attributes (`NoteEditorTests.cs:27-28`) — so
adding scoped classes/attributes is safe. bUnit's JS interop is strict by default: introducing any
`IJSRuntime` call in these pages would throw in tests (a second reason for "no new JS").
**"What the markup must not say"** (commit `fab42ed`) = the Login/Register tests above: the
password value must not be echoed (`#password` value empty), the rejection must be uniform, no
"already registered" wording — all copy/behavior, untouched by a restyle.

**Merge coupling**: `feature/test-plan` also edits `Import.razor:89` (adds `maxlength` to
`#title`) and two `@code` lines in each detail page. A redesign that rewrites the Import title
field will conflict when either branch lands second.

### 5. Guardrails embedded in markup

- **Source as text** — `<pre class="source-content …">@material.Content</pre>`
  (`Materials/Detail.razor:73`, `Notes/Detail.razor:62`), comment `Materials/Detail.razor:69-72`.
  Mockup `.src` is also a `<pre>` — keep the element and plain interpolation.
- **Single `MarkupString`** — `NoteEditor.razor:34` only; mockup `.md` typography must be applied via
  `::deep` rules on `.note-preview` (`NoteEditor.razor.css:21-43` pattern), never by re-rendering.
- **User titles by plain interpolation** — `Notes/Index.razor:49-50`, `Materials/Index.razor:38-40`,
  `Notes/Detail.razor:123-127` (dialog quotes the title). Breadcrumbs added from the mockup must
  interpolate the same way.
- **Read-only streaming** — `pre.note-content` while generating (`Materials/Detail.razor:145-152`,
  rationale `:147-150`). The mockup's caret/stream-bar are decorative and can be conditional on
  `isGenerating`; the `<pre>` must not become a textarea.
- **Disabled states** — `Import.razor:70,98`; `Materials/Detail.razor:108,124,128`;
  `Notes/Detail.razor:90,144,149`; `NoteEditor.razor:14,47,59`. Restyle must keep a visible
  disabled look (Bootstrap `:disabled` opacity) for the dark tokens.
- **Manual modal** — no Bootstrap JS (comment `Notes/Detail.razor:98-102`); `modal d-block` +
  hand-rendered `modal-backdrop show`; focus sentinels must remain the first and last focusable
  children (`:114,:156`); `@ref` on both footer buttons; Escape via `@onkeydown` on the container.
  Mockup `.scrim` (blur) maps onto `.modal-backdrop`, mockup `.modal` onto `.modal-content`.
- **Confirmation alerts instead of `window.confirm`** — `Materials/Detail.razor:117-132`.
- **Dismiss without Bootstrap JS** — `button.btn-close` + `@onclick` (`Materials/Detail.razor:89-93`),
  never `data-bs-dismiss`. `[data-bs-theme=dark] .btn-close` already inverts the glyph
  (`bootstrap.css:5399`).
- **Import panels hidden, not removed** — `d-none` keeps the chosen file alive (`Import.razor:52-55`).
- **`maxlength` attributes are functional**, not cosmetic (`Import.razor:63-67`, `NoteEditor.razor:41-44`).
- **NavMenu `Match.All`** and **logout as link** — `NavMenu.razor:16,21-25,49-55`.

### 6. Gaps between mockups and app

**Screens/states with no artboard** (style from System.dc.html tokens/components):
- `/register` — README maps it to the Login artboard; it has an extra `div.form-text` hint
  (`Register.razor:31`).
- `/logout` (`Logout.razor`), `/Error` (English, `Error.razor`), `/not-found` (English, `h3`,
  `NotFound.razor`), reconnect `<dialog>`, `#blazor-error-ui`.
- Error alerts on every page, validation messages (`ValidationMessage` + `.invalid` on inputs — System
  artboard has the invalid-field look), load-failure `p.text-danger` on both index pages, material/note
  not-found branches, "Materiał źródłowy nie jest już dostępny.", saved-note paragraph and deletion
  notice on Materials/Detail, the in-dialog delete error, Materials index empty state (Main's empty
  card is the template), password-changed success on Profile (shown in artboard).
- Streaming vs draft vs empty are all in Material.dc.html (`stan`); the error state is not.

**Data the mockups show — all already available**
- Notes cards: title, `UpdatedAt`, source link → `NoteListItem(Id, Title, UpdatedAt, SourceMaterialId)`
  (`Notes/NoteListItem.cs:22`).
- Materials table: title, source badge (file name or "paste"), date, note status →
  `SourceMaterialListItem(Id, Title, Kind, OriginalFileName, CreatedAt, NoteId)`
  (`SourceMaterials/SourceMaterialListItem.cs:32-38`).
- Breadcrumbs (Material/Note): current title is loaded; parent is a static link.
- Note pane-head source link: `material.Title` already loaded (`Notes/Detail.razor:57`).
- Sidebar user block (initial + name + e-mail): claims `ClaimTypes.Email` and `display_name`
  (`Auth/AuthCookie.cs:72-73,81`) + `DisplayNameOrEmail` (`:144`). No DB read needed.
- No counts, quota meter or search appear in the default states → nothing needs service/data changes.

**Copy/structure conflicts (Razor wins unless the addition is on the README's allowed list)**
- Note counter `612 / 65 536` in the mockup vs unformatted digits pinned by `NoteEditorTests.cs:175`.
- Materials table replaces "Wklejono/Zaimportowano {date} z pliku {name}" with a source badge +
  date column (+ `title` tooltip "Wklejono"/"Zaimportowano"); adds column headers
  "Materiał/Źródło/Dodano/Notatka" and badge label "Wklejony tekst".
- Material detail meta: badge with file name + "Zaimportowano {date}" (drops "z pliku …").
- Notes detail: "Usuń notatkę" moves from below the editor (`Notes/Detail.razor:84-92`) to the page
  header; saved message becomes a pane-head badge instead of `alert-success`.
- Materials detail: the generate button sits in the pane head; the empty state shows a second, large
  "Generuj notatkę" CTA (conflicts with `.Single()` if both render).
- Import file mode: dropzone copy "Upuść plik .md tutaj albo wybierz z dysku" (new copy).
- Profile: section hints ("Adres, którym logujesz się do aplikacji." etc.), subtitle, delete-account
  field labelled with the confirmation sentence instead of "Hasło"; primary buttons shown as
  secondary.
- Login: cross-link moves under the `h1` as a subtitle; hero copy is new (allowed).
- Page subtitles and header "Dodaj materiał" CTAs on both index pages (new, presentational).
- MainLayout's English "About" top row has no counterpart in any artboard.

**Class-name collisions** between mockup and Bootstrap: `.nav`, `.nav-item`, `.btn`, `.card`,
`.badge`, `.alert`, `.modal`, `.content`, `.label`. The mockup's `.input` is a *wrapper div*, while
the app styles the `<input>` itself (`.form-control`). Lifting mockup CSS verbatim would fight
Bootstrap; map values onto Bootstrap classes instead.

### 7. Risks, phase boundaries, verification

**Risks**
1. *Hook drift* (highest): an innocuous-looking change (active toggle as `btn-primary`, a hint
   `div.form-text` above the counter, a second generate CTA, an SVG `<title>`, N0 counter) silently
   breaks the not-yet-merged bUnit suite. Nothing on this branch would catch it.
2. *Bootstrap variable coverage*: buttons ignore `--bs-primary` (finding 2); forgetting a variant
   (`btn-warning`, `btn-outline-*`, `btn-close`, `list-group`, `form-control:disabled`,
   `::file-selector-button`) leaves light-theme remnants. `color-scheme: dark` comes with
   `[data-bs-theme=dark]` (`bootstrap.css:128` block), so native scrollbars/file inputs follow.
3. *Scoped vs global CSS*: shared primitives in a `.razor.css` do not reach other components; the
   duplicated `.source-content` rules show this already bit once.
4. *Mobile nav*: moving the checkbox, or adding a wrapper between it and `.nav-scrollable`, breaks the
   `~` selector; the sticky/100vh sidebar math (`NavMenu.razor.css:142`) changes with a new header.
5. *Modal stacking*: Bootstrap `.modal` z-index 1055 / backdrop 1050 must stay above a sticky
   sidebar/top bar; the focus sentinels must remain inside `.modal`.
6. *Merge conflicts* with `feature/test-plan` in `Import.razor` (title field).
7. *Visual QA cost*: the streaming state needs a real generation — it consumes the daily generation
   quota and AI spend.

**Recommended phases** (each ends in a buildable, shippable app):
- **P1 — Theme foundation (global only)**: `App.razor` (`data-bs-theme="dark"`, Google Fonts
  `preconnect` + `<link>` for Geist/Geist Mono), `app.css` rewritten: tokens as custom properties,
  `--bs-*` theme mapping, per-component overrides (btn variants, forms, alerts, list-group, modal,
  btn-close, validation, focus ring, headings/type scale), shared primitives (page head, crumbs,
  badges, segmented, pane, card grid); small `Icon` Razor component (inline SVG, stroke 1.7, 24 grid,
  `aria-hidden`, no `<title>`). Ships alone: the existing structure turns dark.
- **P2 — Shell**: MainLayout (drop the About row; sidebar 248 px; brand; user block; mobile top bar
  keeping the checkbox toggler), NavMenu icons → `Icon`, `#blazor-error-ui` moved to `App.razor`,
  ReconnectModal restyle, new `AuthLayout` + `@layout` on Login/Register (+ hero). Must land together
  (error UI move and layouts are coupled).
- **P3 — List & form pages**: Notes/Index (cards + empty state), Materials/Index (table/grid + badges),
  Import (segmented, textarea, file panel, title), Profile (settings rows + danger zone),
  Logout/Error/NotFound (card treatment, copy unchanged). Independent of P4.
- **P4 — Detail pages + editor**: Materials/Detail, Notes/Detail, NoteEditor, delete modal, shared
  pane CSS (consolidate the duplicated `.source-content`). Must land together — both pages host
  NoteEditor and share the pane look.
- **P5 (optional) — responsive & QA pass**: narrow-width polish, two-pane stacking (source first),
  cross-page consistency.

**Verification per phase** (on the Mac; `dotnet` 10.0.301 at `/opt/homebrew/bin/dotnet`)
- Every phase: `dotnet build` then `dotnet test` (current suite: must stay green;
  DataAccessBoundaryTests covers any new component).
- Hook check for P1, P3, P4 (the phases that touch pages/NoteEditor): in a throwaway worktree of the
  phase commit, merge `feature/test-plan` without committing and run `dotnet test` there — the bUnit
  suite is the real guard (e.g. `git worktree add ../10xDevs3-hookcheck feature/ui-redesign`,
  `git -C ../10xDevs3-hookcheck merge --no-commit --no-ff feature/test-plan`, `dotnet test`,
  then `git worktree remove --force`). Plus a static grep that the hook ids/classes above still exist.
- Visual: user-secrets are configured (`ConnectionStrings:Postgres` and `Ai:ApiKey` both present in
  `UserSecretsId 39f75e4c-…`), so `dotnet run` (http://localhost:5125) works against Supabase. Check
  each route at 1440 px and ~400 px, compare with the artboards; exercise: login error + validation,
  register, import paste/file + validation, material empty → generate (streaming) → draft →
  confirm-replace, note preview/edit/save, delete dialog (Tab/Shift+Tab/Escape), deletion notice
  dismiss, profile three forms, logout, `/not-found`, mobile hamburger open/close. Keep generation to
  one or two runs (quota).

## Code References

- `Components/App.razor:2,9-11,18-20` — lang, CSS order, only script
- `Components/Routes.razor:3,4-6,8` — DefaultLayout, NotAuthorized redirect, FocusOnNavigate h1
- `Components/Layout/MainLayout.razor:3-17,19-23` — shell, error UI
- `Components/Layout/MainLayout.razor.css:39-77,79-98` — 641 px breakpoint, error-UI styles
- `Components/Layout/NavMenu.razor:9,11,16,21-25,39-41,49-55` — checkbox, inline onclick, Match.All, user label, logout link
- `Components/Layout/NavMenu.razor.css:27-63,100-122,124-145` — data-URI icons, nowrap + ellipsis, mobile toggle
- `Components/Layout/ReconnectModal.razor:3-31`, `.razor.js:2-9` — ids/classes contract
- `wwwroot/app.css:1-60` — template globals
- `wwwroot/lib/bootstrap/dist/css/bootstrap.css:1-5,128,3056-3060,4932-4935,5399` — v5.3.3, dark block, hard-coded btn vars, alert vars, btn-close
- `Components/Pages/Notes/Index.razor:11-17,23-61`
- `Components/Pages/Materials/Index.razor:12,18-76`
- `Components/Pages/Materials/Import.razor:2,30-47,56-100`
- `Components/Pages/Materials/Detail.razor:32-37,65-160` (+ `.razor.css:1-18`)
- `Components/Pages/Notes/Detail.razor:25-160` (+ `.razor.css:1-11`)
- `Components/Notes/NoteEditor.razor:11-61` (+ `.razor.css:1-43`)
- `Components/Pages/Account/Login.razor:14-44`, `Register.razor:8-38`, `Logout.razor:5-18`, `Profile.razor:14-125`
- `Components/Pages/Error.razor:1-30`, `NotFound.razor:1-10`, `Components/Account/RedirectToLogin.razor:7-15`
- `Program.cs:31-32,67-69,110,115,205,209,215,219-222` — render modes, login path, fallback policy, cascading auth, error/status pages
- `Auth/AuthCookie.cs:72-73,81,144` — claims for the sidebar user block
- `Notes/NoteListItem.cs:22`, `SourceMaterials/SourceMaterialListItem.cs:32-38` — list data
- `tests/10xNotes.Tests/DataAccessBoundaryTests.cs:30-101` — reflection boundary test
- `feature/test-plan:tests/10xNotes.Tests/{NoteEditorTests,AccountLoginTests,AccountRegisterTests,MaterialsImportTests,MaterialsDetailTests}.cs` — hooks tabled in §4

## Architecture Insights

- Render-mode split is by necessity, not taste: auth/profile/logout are static SSR because cookie
  writes need an unstarted response; lists are static SSR to avoid double queries; Import/detail pages
  are interactive for `InputFile` and streaming. The redesign must not add interactivity to static pages.
- The codebase already treats "no Bootstrap JS" as a rule with comments at every site that would
  normally use it — the redesign inherits it (CSS-only toggles, Blazor-owned state).
- Rules live in testable C# (`AuthCookie.DisplayNameOrEmail`, validators) and markup only renders
  them; keep new presentational logic (initials, badge choice) trivial and in markup, or it becomes
  untested logic in a component.
- Comments in markup are load-bearing documentation of guardrails; restyling commits should preserve
  or update them rather than drop them.

## Historical Context (from prior changes)

- `context/archive/2026-09-08-delete-note/plan.md:30,51,67,161` — "Bootstrap JS is not loaded";
  dismiss via Blazor, modal backdrop by hand, focus trap rationale.
- `context/archive/2026-08-17-ai-note-generation/plan.md:259` — two-column `row/col-lg-6`, source
  first when stacked.
- `context/archive/2026-08-18-browse-notes-and-sources/plan.md:175` — list pages as Bootstrap
  `list-group`, static SSR.
- `context/archive/2026-08-17-markdown-import/plan.md:204` — Import anatomy (`InputFile`, alert region).
- `context/foundation/lessons.md` — "Give the markup its state before the first await" (keep field
  initializers when restructuring markup, e.g. Profile/Import models); "An advertised limit must be
  one every layer can carry" (the `maxlength` attributes and counters are part of that chain).
- `feature/test-plan:context/archive/2026-09-09-testing-component-layer-bootstrap/research.md` —
  origin of the bUnit hooks.

## Related Research

- `feature/test-plan:context/archive/2026-09-09-testing-component-layer-bootstrap/research.md`
- No other `research.md` exists on this branch.

## Decisions made without the user

This step ran non-interactively; these were decided here and should be confirmed or overridden in
`/10x-plan`:

1. **Scope/depth**: full-depth research over all seven brief areas; no scope question asked.
2. **Copy conflicts resolve to the Razor copy**, except the README's allowed additions (breadcrumbs,
   source/status badges, page subtitles, auth hero, section hints). Concretely: keep the NoteEditor
   counter unformatted (test-pinned), keep Profile's "Hasło" label + confirmation paragraph, keep the
   Materials "Wklejono/Zaimportowano" verbs (as badge + `title`/meta text).
3. **Materials/Detail keeps exactly one "Generuj notatkę" button**, recommended in the pane head in all
   states; the empty state shows icon + existing hint, no second CTA.
4. **Notes/Detail "Usuń notatkę" may move to the page header** as the mockup shows — it stays far from
   "Zapisz zmiany", which is the rationale in `Notes/Detail.razor:84-86`; the comment must be updated.
   Flagged as the one structural move worth a plan-level confirmation.
5. **Logout, Error, NotFound stay on MainLayout** (Logout is reached by a signed-in user from the
   sidebar; Error/NotFound are anonymous and the nav already handles that). Only Login/Register get
   `AuthLayout`.
6. **Remove the MainLayout "About" top row** (template leftover, no artboard); mobile gets the logo +
   hamburger top bar instead.
7. **`#blazor-error-ui` moves to `App.razor`** so both layouts have it.
8. **Research doc uses plain `path:line` references**, matching the prior research artifact, rather than
   GitHub permalinks (commit `4c8279d` is pushed; permalinks are `https://github.com/mati-ck/10xDevs3/blob/4c8279d…/<file>#L<n>`).
9. **Sidebar breakpoint**: recommend keeping the existing 641 px unless the visual pass shows the
   248 px sidebar crowding the content; detail panes keep stacking below Bootstrap `lg` (992 px).

## Open Questions

- Import file mode: adopt the dropzone look (native `<input type=file>` stretched transparently over
  the zone accepts drops without JS) and its new copy, or only restyle the plain file input?
- Header "Dodaj materiał" CTAs on Notes/Materials index — include (pure links) or skip as new
  navigation?
- `<html lang="en">` in a Polish app — out of the visual scope, but a one-attribute fix; include?
- Should the English ReconnectModal / `#blazor-error-ui` / Error / NotFound copy stay English (1:1
  rule) — assumed yes.
- Order relative to `feature/test-plan`: if it merges to `main` first, rebase before P3 to absorb the
  `Import.razor` title-field change and get the bUnit suite running natively.
