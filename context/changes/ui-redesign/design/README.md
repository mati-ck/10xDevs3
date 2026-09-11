# UI redesign — design source of truth

Canvas (Claude Design): https://claude.ai/code/artifact/4ebbcf9c-d18c-44f6-b33b-fa92318f7615

The `*.dc.html` files in this folder are the exact artboard sources of that canvas
(1440 px desktop frames). They contain every value to lift: the shared `<style>` in
`<helmet>` (tokens + component classes) and the inline styles per screen. Holes like
`{{akcent}}` / `<sc-if>` are canvas-editor template syntax — ignore them; the default
state of each tweak is the one to implement, the other states describe other UI states.

| Artboard | App route / component |
|---|---|
| Login.dc.html | `/login` (and `/register` — same auth layout, same form anatomy) |
| Main.dc.html | `/` Notes index (cards grid; `pustaLista` = empty state) |
| Materials.dc.html | `/materials` (table: title, source badge, date, note status) |
| Import.dc.html | `/materials/import` (`tryb` = paste / file mode) |
| Material.dc.html | `/materials/{id}` (`stan` = empty / generating / draft editor) |
| Note.dc.html | `/notes/{id}` (editor in preview mode; `dialogUsuwania` = delete modal) |
| Profile.dc.html | `/profile` (settings rows + danger zone) |
| System.dc.html | tokens, type scale, buttons, inputs, badges, alerts |

## Locked decisions (from the user, 2026-09-11)

- Dark theme ONLY. Modern SaaS look, one accent (violet `#8C7BFF`).
- Fonts: Geist + Geist Mono loaded via **Google Fonts `<link>`** in `App.razor` (not self-hosted).
- Mobile has no mockups: narrow screens get a top bar with logo + hamburger and a slide-out
  sidebar, CSS-only (keep the existing checkbox-toggler pattern, no new JS); two-pane
  detail pages stack (source first).
- Structure, navigation and ALL Polish copy stay 1:1 with the current Razor components.
  No new features (no quota meter, no search). Small presentational additions from the
  mockups are fine (breadcrumbs, source/status badges, page subtitles, auth-page hero copy).
- Visual only: no changes to services, data access, render modes or validation.
- Keep Bootstrap 5.3 and restyle it (`data-bs-theme="dark"`, `--bs-*` variables mapped to
  the tokens). Keep semantic hooks tests rely on — on branch `feature/test-plan` bUnit tests
  select `div.alert-danger`, `div.alert-warning`, `button.btn-primary`, `div.form-text`,
  ids `#email`, `#password`, `#note-title`, `#note-content`, `form`.
- Security guardrails from the PRD / code comments stay: source material rendered as text in
  `<pre>`, `MarkupString` only in the note preview, user titles via plain interpolation.
- Icons: inline SVG (stroke 1.7, 24 grid) as a small Razor component; no icon font, no emoji.

## Tokens

bg #0A0B0F · side #0C0D12 · panel #111319 · panel-2 #161922 · raise #1C202B · field #0D0F14
line #1F2330 · line-2 #2B3040 · tx #ECEEF3 · tx-2 #A2A8B7 · tx-3 #697083
accent #8C7BFF (text on accent #0B0B12) · ok #3ECF8E · warn #F2B84B · bad #F07474
accent-soft = color-mix(in oklch, accent 14%, transparent) · accent-line = 38%
Radii 8 (buttons) / 9–10 (inputs, alerts, segmented) / 14 (cards) / 16 (modal).
Controls 36 px (sm 30, lg 42), inputs 40 px. Sidebar 248 px. Content padding 40/48 px.
H1 28/600 −0.025em · card title 16/600 · body 14/400 · meta 12.5 · mono 12.5.
