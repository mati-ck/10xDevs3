<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Import pliku Markdown jako materiał źródłowy (S-01a)

- **Plan**: `context/changes/markdown-import/plan.md`
- **Scope**: Full plan — Phases 1–3 of 3
- **Date**: 2026-08-17
- **Verdict**: NEEDS ATTENTION → RESOLVED (4 of 5 fixed, 1 skipped by choice)
- **Findings**: 0 critical, 1 warning, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Success criteria re-verification

All automated criteria were re-run at review time, not taken from the Progress checkboxes:

| Check | Result |
|---|---|
| `dotnet build` | Build succeeded, 0 warnings, 0 errors |
| `dotnet test` | 93 passed, 0 failed |
| `dotnet test --filter DataAccessBoundaryTests` | 1 passed |
| `dotnet test --filter MarkdownImportValidatorTests` | 35 passed |
| `dotnet ef migrations has-pending-model-changes` | No changes since last migration |

Manual criteria (1.5–1.6, 3.4–3.10) were confirmed by the user during implementation. 3.4 was additionally corroborated by an anonymous `curl`, which returned `302 → /login?ReturnUrl=%2Fmaterials%2Fimport`. 1.6 was corroborated against the live Supabase security advisor: `public.source_materials` shows `rls_enabled: true` with the same INFO-level `rls_enabled_no_policy` lint as `profiles`, which is the intended deny-all posture.

## Findings

### F1 — A database failure during import kills the circuit instead of showing a message

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Components/Pages/Materials/Import.razor:118`
- **Detail**: `ImportAsync` maps all four validator failures onto Polish copy, but `SaveChangesAsync` is unguarded. A transient Postgres blip, a connection-pool timeout, or the `auth.users` FK rejecting a stale session all raise `DbUpdateException` out of an interactive event handler, which tears down the SignalR circuit and drops the user into the reconnect modal with their upload lost and no explanation. Every other failure on this page is a clear message. The NFR requires visible feedback for operations that can take time; a killed circuit is the opposite. Note the sibling auth pages classify their infrastructure failure explicitly (`AuthFailureReason.Unavailable` → "Usługa jest chwilowo niedostępna"), so the app already has a precedent for this case.
- **Fix**: Wrap the save in `try/catch (DbUpdateException)` and set `errorMessage` to a Polish "could not save, try again" string, mirroring the `Unavailable` copy in `Register.razor:62`.
- **Decision**: FIXED — `try/catch (DbUpdateException)` added around the save in `Import.razor`; a transient database failure now shows a Polish message instead of tearing down the circuit.

### F2 — Import timestamp renders in the server's timezone, not the user's

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Components/Pages/Materials/Detail.razor:24`
- **Detail**: `@material.CreatedAt.ToLocalTime()` resolves "local" on the **server**. The container runs UTC, so a Polish user who imports at 20:00 sees 18:00 or 19:00 depending on DST. The page is static SSR, so the browser timezone is not available without JS. This is cosmetic today but becomes misleading once a user has several materials and uses the timestamp to tell them apart.
- **Fix**: Either render the offset explicitly so the value is unambiguous, or drop `ToLocalTime()` and label it as UTC — a wrong-looking local time is worse than a correct labelled one.
- **Decision**: FIXED — replaced `ToLocalTime()` with a `TimeProvider` seam (`Time/AppTimeProvider.cs`, `Time/TimeProviderExtensions.cs`, registered in `Program.cs`), backed by 7 tests in `AppTimeProviderTests.cs` including the summer/winter pair a fixed offset would fail. Adjacent fix on the same expression: the date now renders with an explicit `pl-PL` culture, having previously produced English month names in a Polish UI.

### F3 — String truncation can split a surrogate pair

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `SourceMaterials/MarkdownImportValidator.cs:71` (`DeriveTitle`), `:93` (`SanitizeFileName`)
- **Detail**: Both truncations slice by UTF-16 code unit (`title[..MaxTitleLength]`, `name[..MaxFileNameLength]`). A non-BMP character — an emoji, which is common in filenames — occupies two code units, so a cut landing between them leaves a lone surrogate in the stored string. Polish diacritics are BMP and unaffected, which is why the existing tests pass. The exposure is narrow (a filename over 200 or 260 chars *and* an emoji straddling exactly that boundary), but the result is a corrupt title or filename, and depending on the encoder it either becomes U+FFFD or throws at save time.
- **Fix**: Trim back one char when the cut lands on a high surrogate — `if (char.IsHighSurrogate(s[cut - 1])) cut--;` — and add a test with an emoji at the boundary.
- **Decision**: FIXED — both truncations now route through a shared `Truncate` helper that steps back off a high surrogate; 7 new cases in `MarkdownImportValidatorTests.cs` pin both sides of the off-by-one (emoji dropped whole vs. kept whole) and assert well-formedness via strict UTF-8 encoding rather than surrogate absence.

### F4 — Two changes landed outside the plan's Changes Required

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `Components/Pages/Materials/Detail.razor.css`, deleted `Components/Pages/Counter.razor` and `Weather.razor`
- **Detail**: Neither is in the plan's Changes Required. Both are defensible — the CSS caps the content box so a 128 KB document does not push the heading off-screen (and is what actually satisfies the plan's "leave room for a second column"), and the page deletions were requested by the user mid-phase, extending the plan's "remove the template's Counter and Weather links". Also worth noting: `.bi-list-nested-nav-menu` in `NavMenu.razor.css` is now dead, since it only styled the Weather link. Flagged for bookkeeping only — the plan is the ground truth future reviews read, and right now it under-describes what shipped.
- **Fix**: Add a one-line addendum to the plan's Phase 3 Changes Required naming both, so a later reader is not left wondering where they came from.
- **Decision**: SKIPPED — user opted to leave the plan as the pre-implementation contract; git history records what actually shipped. Dead `.bi-list-nested-nav-menu` rule in `NavMenu.razor.css` left in place.

### F5 — `FormName` on an interactive `EditForm` is a static-SSR idiom that does nothing here

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `Components/Pages/Materials/Import.razor:30`
- **Detail**: `FormName="import"` is meaningful only for a static-SSR form post, where it pairs with `[SupplyParameterFromForm]` — the pattern `Login.razor` and `Register.razor` use. This page is explicitly `@rendermode InteractiveServer` and carries a comment explaining that departure, so the attribute is inert. Harmless at runtime, but it copies the idiom of the very pattern the file says it is not following, which is the kind of thing that misleads the next reader.
- **Fix**: Delete the `FormName="import"` attribute.
- **Decision**: FIXED — `FormName="import"` removed from the interactive `EditForm`.
