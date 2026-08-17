<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Przegląd, edycja i zapis notatki (S-01c)

- **Plan**: `context/changes/note-review-save/plan.md`
- **Scope**: Full plan — Phases 1–4
- **Date**: 2026-08-17
- **Verdict**: REJECTED → APPROVED after triage (7 of 9 findings fixed; 2 skipped as matching precedent)
- **Findings**: 1 critical, 2 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING → PASS after fixes |
| Scope Discipline | PASS |
| Safety & Quality | FAIL → PASS after fixes |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Success criteria — verified, not assumed

| Check | Result |
|---|---|
| `dotnet build` (1.1/2.1/3.1/4.1) | 0 warnings, 0 errors |
| `dotnet test` (1.2/2.2/3.2/4.2) | 208/208 pass at review time; 214/214 after triage fixes |
| `NoteMarkdownTests` (1.3) | 24 pass |
| `NoteValidatorTests` (1.4) | 23 pass; all 4 `NoteValidationFailure` members asserted |
| `NoteServiceTests` (2.3) | 23 pass |
| `DataAccessBoundaryTests` (1.5/2.4/3.3/4.3) | pass |
| Migration applied (2.5) | in `__EFMigrationsHistory` |
| FK delete rule (2.6) | `pg_constraint.confdeltype` = `a` (NO ACTION) |
| RLS (2.7) | on for `notes` and `note_events` |

**Manual criteria were not rubber-stamped.** Criterion 4.6 (re-saving must not inflate the
acceptance numerator) is confirmed by live data rather than by assertion: `Generated`=2,
`Saved`=2, `notes`=2 rows, and one note has `updated_at > created_at` — a genuine re-save that
produced no additional `Saved` event. Criterion 3.10 (source material untouched) is provable
structurally: the only write to `SourceMaterials` in application code is `Import.razor:119`;
every path this change added reads only.

## Plan adherence summary

No planned item is missing. One DRIFT (F3, fixed during this review). Three benign EXTRAs, none
conflicting with a plan decision: `NoteValidator.FallbackTitle`; `NoteEventKind` persisted as
text; a "material no longer available" branch on the note page. The entire
"What We're NOT Doing" list is respected — including the load-bearing entries: OQ2 left open via
`DeleteBehavior.NoAction`, no `NavigationLock`, no ledger UI, no extra sanitizer package, no
index pages.

## Findings

### F1 — Editor accepts 64 KB but the circuit dies above 32 KB, destroying the note

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (reliability / data loss)
- **Location**: `Components/Notes/NoteEditor.razor:41` + `Program.cs:23-24`
- **Detail**: `NoteValidator.MaxContentLength` is 65 536 bytes, and the app advertises it — the character counter (`NoteEditor.razor:46`) and the Polish copy on both pages ("Notatka może mieć najwyżej 64 KB"). But `Program.cs` never calls `AddHubOptions`, so SignalR's default inbound cap stands. Verified against the app's own option graph: `MaximumReceiveMessageSize = 32768`, exactly half the validator's bound. The textarea commits on `@onchange`, sending the whole value as one hub invocation. Exceeding the cap does not surface as an error the component can catch — SignalR aborts the connection, so the user gets the reconnect modal and loses a note that was never saved. The real ceiling is lower than 32 KB: JSON escaping plus 2-byte UTF-8 for Polish diacritics means ~25 KB of typed text can be enough. The failure shape is the worst available: a long generated note displays correctly and only dies when the user touches it. Note the asymmetry that hid this — the title input carries `maxlength`, the textarea carries none.
- **Fix**: Raise the hub limit past the validator's bound, derived from the same constant so the two cannot drift, and add `maxlength` to the textarea so the browser refuses before the wire does.
  - Strength: Keeps one source of truth for the limit; the browser-side `maxlength` makes the bound visible rather than fatal.
  - Tradeoff: A larger inbound cap raises the per-circuit memory ceiling — bounded and small at this size.
  - Confidence: HIGH — the 32768 value was read from the app's own DI graph, not from documentation.
  - Blind spot: Not verified end-to-end against a running browser with a >32 KB paste; the limit and the code path are confirmed, the teardown itself is inferred from SignalR's documented behaviour.
- **Decision**: FIXED + ACCEPTED-AS-RULE: "An advertised limit must be one every layer beneath it can carry" — hub bound derived from `NoteValidator.MaxContentLength` via new `NoteWireLimits`, `maxlength` added to the textarea, three tests in `NoteWireLimitTests` pin the arithmetic against real serialized bytes.

### F2 — `GetDynamicUrl` bypasses the URL scheme allowlist

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (security, latent)
- **Location**: `Notes/NoteMarkdown.cs:66-72`
- **Detail**: Markdig's `LinkInline` carries a `GetDynamicUrl` delegate that the HTML renderer **prefers over `Url`**. Verified: with `Url = "#"` and `GetDynamicUrl` set, the output is `<a href="javascript:alert(1)">`. Not exploitable today — nothing sets it under the bare `.DisableHtml()` pipeline — but this file's entire job is to be a boundary that stays correct, and it silently stops being one the moment anyone adds `.UseAdvancedExtensions()`, `.UseMediaLinks()`, `.UseJiraLinks()` or `.UseAutoLinks()`. The file's own remarks promise "a scheme allowlist closes what is left", which would no longer hold.
- **Fix**: Clear `link.GetDynamicUrl = null` unconditionally inside the existing loop, with a comment pinning why, plus a regression test that sets it and asserts it never reaches the output.
- **Decision**: FIXED — `link.GetDynamicUrl = null` cleared unconditionally; neutralization extracted to the public `NoteMarkdown.NeutralizeUnsafeUrls` so the property is reachable from a test. Both new tests verified to fail without the fix.

### F3 — `RecordGenerationAsync` let cancellation escape a method contracted never to throw

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `Notes/NoteService.cs:250` (pre-fix)
- **Detail**: The plan states the ledger write is best-effort and "nigdy nie wychodzi z metody", explicitly including `OperationCanceledException`. The code filtered OCE *out* of its catch, so it escaped; the page compensated with a local `try`/`catch` at the single call site. Net user-visible behaviour matched the plan, but the guarantee lived at the caller rather than in the method that promised it — so a second caller (S-02's paste flow being the obvious future one) would inherit a contract that did not exist.
- **Fix**: Catch `OperationCanceledException` silently inside `NoteService.RecordGenerationAsync` (cancellation is expected, not a fault, so no log line) and delete the page's compensating wrapper.
- **Decision**: FIXED — applied during this review; build clean, 208/208 still pass.

### F4 — `isGenerating` is set outside the `try`, so a throw leaves the button permanently disabled

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (reliability)
- **Location**: `Components/Pages/Materials/Detail.razor:291-311`
- **Detail**: `isGenerating = true` is assigned before the `try` opens. Anything thrown in between — realistically only `await CancelGenerationAsync()` — both escapes the handler (tearing down the circuit) and leaves `isGenerating` stuck true, because the `finally` that resets it is never entered. Probability is very low; the cost is total. Related: that `CancelGenerationAsync()` call is itself unreachable in a meaningful state — the `isGenerating` guard prevents a concurrent run, and any finished run has already nulled `generationCts` in its own `finally` — so the comment above it describes a state that cannot occur.
- **Fix**: Move the `try` up to enclose the assignment, and correct or drop the stale comment.
- **Decision**: FIXED — `CancellationTokenSource` declared before the try and disposed in the finally, so the setup path is covered without disturbing the clear-before-dispose ordering. Stale comment corrected.

### F5 — Unsaved-changes guard ignores title-only edits

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (reliability)
- **Location**: `Components/Pages/Materials/Detail.razor:262`
- **Detail**: `HasUnsavedEdits` compares only `draft` against `editorContent`. A user who corrects only the title and then clicks "Generuj notatkę" loses that edit with no confirmation, because the guard never fires.
- **Fix**: Also compare `editorTitle` against `NoteValidator.DeriveTitle(material?.Title)`.
- **Decision**: FIXED — `HasUnsavedEdits` now also compares `editorTitle` against the derived title.

### F6 — `[Authorize]` on the note page but not the material page

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (security, second-order)
- **Location**: `Components/Pages/Materials/Detail.razor:1-2` vs `Components/Pages/Notes/Detail.razor:3`
- **Detail**: Not an auth hole — `Program.cs` sets a deny-by-default `FallbackPolicy`, so an anonymous HTTP request to `/materials/{id}` redirects to `/login`. The gap is that the fallback policy is endpoint-level and does not re-evaluate over an already-open circuit. If the cookie expires or the session cap trips mid-circuit, the note page redirects via `AuthorizeRouteView` while the material page keeps rendering an authenticated-looking shell whose reads return nothing — the "indistinguishable from data loss" state `Program.cs` explicitly exists to prevent. The repo is genuinely split here: `Import.razor` also lacks it, so the new note page is the stricter outlier rather than the material page being a regression.
- **Fix**: Add `@attribute [Authorize]` to `Materials/Detail.razor` (and `Import.razor`) so component-level matches endpoint-level everywhere.
- **Decision**: FIXED — `@attribute [Authorize]` added to `Materials/Detail.razor` and `Import.razor`.

### F7 — `AcceptAsync` retries on every `DbUpdateException`, not just the unique-index race

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (reliability)
- **Location**: `Notes/NoteService.cs:141`
- **Detail**: A permanently fatal write (FK violation because the material was deleted between the existence check and the save, a check constraint, a connection failure) costs a pointless second round trip and then surfaces as `Conflict` → "Spróbuj ponownie za chwilę" — "try again" for something that will never succeed. `GenerationQuotaService` has the identical shape, so this matches precedent rather than diverging from it. Separately: if the connection drops after Postgres commits but before EF sees the ack, the retry writes a **second** `Saved` row for one acceptance, inflating the metric the ledger exists to protect. Vanishingly rare and shared with the precedent.
- **Fix**: Filter the retry on `PostgresException.SqlState == "23505"`; if the double-count ever matters, add a uniqueness constraint that makes the ledger insert idempotent.
- **Decision**: SKIPPED — matches the `GenerationQuotaService` precedent; changing one without the other would be the worse inconsistency.

### F8 — Empty image destination is not neutralized, contradicting the constant's own comment

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Notes/NoteMarkdown.cs:92-96` vs `:44-47`
- **Detail**: An empty destination short-circuits to "safe" before `NeutralizedUrl` applies: `![x]()` renders `<img src="" />`. The `NeutralizedUrl` doc-comment cites precisely this case as the reason for choosing `#` over `""` ("an empty `src` makes some browsers re-request the current page"), so the stated rationale is not applied to the one case it describes. Harmless in effect; the code and its comment simply disagree.
- **Fix**: Either neutralize empty image destinations too, or amend the comment to say the empty case is deliberately left alone.
- **Decision**: FIXED — empty destinations neutralized to `#`, with a test.

### F9 — `OnInitializedAsync` is unguarded on both pages

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (reliability)
- **Location**: `Components/Pages/Materials/Detail.razor:220-237`, `Components/Pages/Notes/Detail.razor:102-120`
- **Detail**: A database blip during the circuit's initialization pass throws in the component lifecycle and tears the circuit down. Consistent with `Import.razor`, so it is an existing project posture rather than a regression — but the note page is the one place a user's saved work lives, so a "nie udało się wczytać notatki" branch would be worth more there than elsewhere.
- **Fix**: Wrap both loads and render a failure branch.
- **Decision**: SKIPPED — matches the existing project posture set by `Import.razor`.

## What was attacked and held

The Markdown rendering seam was the primary target, since it produces the project's only
`MarkupString`. Roughly 65 attack strings were executed against the real `ToHtml`, all inert:
reference links and images, fenced-code info-string breakout, link-title and image-alt breakout,
HTML entities in the scheme (`&#106;avascript:`, `&#x6a;`, `&#58;`), percent-encoded schemes,
embedded NUL/``/tab/CR/LF inside angle-bracket destinations, whitespace lookalikes
(` `, `​`, ` `, `　`, `﻿`, `᠎`, soft hyphen), fullwidth `ｊ`,
mixed case, `data:`/`vbscript:`, raw `<script>`/`<img onerror>`/CDATA/comments, HTML in code
spans and code blocks, generic-attributes syntax, and dangerous links nested in emphasis,
blockquotes, tables and strikethrough.

`SchemeOf` is **fail-closed** by construction: any character it does not strip stays in the
scheme string and then fails the allowlist, so it over-blocks rather than under-blocks. No input
was found where it returns an allowed scheme and a browser resolves a different, dangerous one.

Owner isolation was traced end to end and no cross-user read or write is reachable: `OwnerId` is
stamped from the signed-in user and frozen after insert, `owner_id` is a concurrency token so it
lands in the WHERE of every UPDATE/DELETE, the material existence check inside `AcceptAsync` is
owner-scoped, both pages collapse "missing" and "not yours" into one branch, and no new code
calls `IgnoreQueryFilters()`.

The streaming-buffer versus editor-buffer race — the highest-risk part of the slice, on a page
with a documented history of exactly this bug class — is genuinely closed. `draft` is the
interlock: `GenerateAsync` nulls it before streaming, which removes the editor from the render
tree for the stream's whole duration, and `HandOverToEditor` sets it only after the `await
foreach` completes. There is no window in which the throttled repaint and a live textarea
coexist. `IAsyncDisposable` and the `CancellationTokenSource` lifetime are also correct: the
`using` scope encloses the `try`/`finally`, so the field is nulled before disposal and
`DisposeAsync` can never cancel a disposed source.

Realistic blast radius of any rendering bypass is **self-XSS**: notes are owner-scoped by the
global query filter with RLS behind it, the product has no sharing, and the auth cookie is
`HttpOnly`. That is the ceiling, and the tests' own framing says so accurately.

## Pattern compliance

No substantive mismatches. The new code matches `GenerationQuotaService` (typed results, seam
injection, bounded retry), `MarkdownImportValidator`/`MarkdownImportResult` (pure static seam plus
sealed-record result with private ctor and one enum member per message), `Import.razor` (typed
failure mapped to Polish copy at the page, not inside the component),
`AddGenerationQuota` (hand-written `auth.users` FK + RLS block, same `Down` ordering), and
`GenerationQuotaTests` (SQLite in-memory over the real `UserScopedDbContextFactory`).

The hard data-access rule holds: no `DbContext` and no `IDbContextFactory<>` in any new type;
`NoteEditor` injects nothing at all.
