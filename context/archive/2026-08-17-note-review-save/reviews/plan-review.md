<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Przegląd, edycja i zapis notatki (S-01c)

- **Plan**: `context/changes/note-review-save/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-17
- **Verdict**: REVISE → SOUND (after triage)
- **Findings**: 1 critical, 4 warnings, 3 observations

> **Reviewed after implementation.** All four phases were already committed
> (`616aa2c`, `debda62`, `c33ed14`, `8a19869`) when this review ran, so findings are
> defects in the *plan document*, not in the shipped code. Seven of eight were already
> closed during implementation; the fixes below back-port them into the plan so the
> archived record matches what shipped and why. **F6 is the only finding describing
> live, unaddressed work** — deliberately deferred to S-03.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | WARNING |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | FAIL |
| Plan Completeness | WARNING |

## Grounding

9/9 paths ✓ (verified against pre-implementation tree `d2fc1d0`), 7/7 line references
exact ✓ (including the "275 linii" count for `Detail.razor`), 2/2 archive references ✓,
brief↔plan ✓. Progress↔Phase: 4/4 phases matched, 27/27 success criteria have rows,
no stray checkboxes outside `## Progress` ✓.

Grounding was unusually strong — line-exact across nine files.

## Findings

### F1 — URL allowlist contract misses autolinks; plan-literal build is XSS-able

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Phase 1 §3 (Renderowanie Markdownu) + §4 (Testy szwu)
- **Detail**: The contract says to walk "węzły linków i obrazów" — `LinkInline` only. Markdig models an autolink as `AutolinkInline`, a separate node type that never passes through that walk, and `<javascript:alert(1)>` is a valid CommonMark autolink. Reproduced against Markdig 1.3.2: a plan-literal implementation emits `<p><a href="javascript:alert(1)">javascript:alert(1)</a></p>` straight into the project's only `MarkupString`. The plan's entire safety argument is "DisableHtml closes raw HTML, the scheme allowlist closes the rest" — and the allowlist half had a hole. The prescribed test list contained no autolink case, so the gap would have shipped green.
- **Fix**: Name `AutolinkInline` alongside `LinkInline` in the §3 contract, and add the autolink case to the §4 test list.
  - Strength: Closes the one hole in the plan's own security argument; two lines of contract, one test case.
  - Tradeoff: None — a gap, not a tradeoff.
  - Confidence: HIGH — reproduced empirically.
  - Blind spot: Other node types carrying content-controlled URLs were not exhaustively enumerated; only these two were checked.
- **As-built**: closed. `NoteMarkdown.cs` walks both node types; `NoteMarkdownTests` covers the autolink case.
- **Decision**: FIXED

### F2 — DB-default rationale contradicts the plan's own service design

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 §3 vs Phase 2 §5
- **Detail**: §3 prescribes `gen_random_uuid()`/`now()` defaults reasoning "te wiersze wstawia strona, nie serwis trzymający własny zegar" — but §5's `NoteService` takes `TimeProvider` and does the insert, and requires `UpdatedAt`, which only the service can stamp. Followed literally, a fresh row carries `CreatedAt` from Postgres' clock and `UpdatedAt` from the app's, disagreeing about the same instant — precisely what `AppDbContext`'s `GenerationQuota` comment warns against.
- **Fix**: Record that `NoteService` stamps `Id`/`CreatedAt`/`UpdatedAt` from its own clock, with the column defaults kept as a backstop.
  - Strength: One clock per row; matches the `GenerationQuotaService` precedent the plan itself cites; SQLite tests exercise the real insert path.
  - Tradeoff: Declared defaults are rarely exercised.
  - Confidence: HIGH — the plan cites that precedent for the opposite conclusion in the same section.
  - Blind spot: None significant.
- **As-built**: resolved this way; flagged at the Phase 2 gate.
- **Decision**: FIXED

### F3 — Phase 3 success criterion cannot pass at the Phase 3 gate

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 3 Manual Verification (3.7, 3.11) + Implementation Note
- **Detail**: 3.7 reads "Zapis prowadzi na `/notes/{id}`, a notatka przeżywa odświeżenie i wylogowanie", but `/notes/{id}` is built in Phase 4. The plan then places a hard stop-and-confirm gate at the end of Phase 3, asking the tester to confirm something not yet built. This fired during implementation: the reviewer reported "note is saved but not found on page" — correct behaviour reading as a bug.
- **Fix**: Split 3.7 into a Phase 3 half (redirect fires, row is in the database) and a Phase 4 half (page renders after refresh and re-login, folded into 4.5); scope 3.11 to "link is shown", with 4.9 covering "link works".
  - Strength: Each gate becomes answerable with what that phase built.
  - Tradeoff: Splits one user-facing behaviour across two checklists.
  - Confidence: HIGH — the failure mode was observed, not predicted.
  - Blind spot: None significant.
- **Decision**: FIXED

### F4 — Criterion 2.6 is unsatisfiable by correct `dotnet ef` output

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 Automated Verification (2.6) + Phase 2 §4
- **Detail**: "Wygenerowana migracja zawiera `ON DELETE NO ACTION`" — EF omits the clause because NO ACTION is Postgres' default for an unspecified FK. A correct migration fails the check as worded, pushing an implementer toward "fixing" a migration that was already right.
- **Fix**: Assert the FK is neither CASCADE nor RESTRICT, verified in the database (`pg_constraint.confdeltype` = `a`); separately require an explicit `onDelete: ReferentialAction.NoAction` in `CreateTable` so the decision is visible in the file.
- **Decision**: FIXED

### F5 — Phase 2 test list demands a throw the plan's own design prevents

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §6 (Przypadki)
- **Detail**: "AcceptAsync bez zalogowanego użytkownika rzuca" contradicts §5, where the owner-scoped material lookup returns before any write can reach `StampOwners` — and contradicts the plan's own argument, two paragraphs earlier, against exceptions escaping result-returning methods.
- **Fix**: Restate as "zwraca `NotFound` i nie zapisuje ani notatki, ani zdarzenia", with the reasoning attached.
- **Decision**: FIXED

### F6 — Reachability argument checks one hop and stops

- **Severity**: 📋 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: What We're NOT Doing — "Listy notatek i materiałów"
- **Detail**: The scope-out is defended with "`/notes/{id}` jest osiągalne przez zapis i przez link ze strony materiału" — true, but the plan never asks whether the material page is itself reachable. It is not: `NavMenu.razor` offers only Home / "Zaimportuj materiał" / Wyloguj, and `/materials/{id}` is reached only by the redirect after import. The two pages link to each other inside an island with no entrance, so a saved note is unreachable the next day without pasting the URL by hand. "Notatka przeżywa odświeżenie i wylogowanie" is satisfied literally and hollow in practice.
- **Fix**: Record the real reachability picture in the scope-out, reframing S-03 as owning *entry* to the content graph rather than listing alone, and noting that S-01c is what unblocks it (prerequisites `S-01c, F-02` now met).
  - Strength: Keeps S-03's own open question ("osobna lista czy obok notatki") an explicit design decision rather than an accident.
  - Tradeoff: The product has no usable entry point until S-03 ships.
  - Confidence: HIGH — verified against `NavMenu.razor` and roadmap S-03.
  - Blind spot: None significant.
- **Status**: LIVE — the only finding not closed by the implementation. Raised independently by the reviewer and deliberately deferred to S-03. Roadmap status for S-03 left at `proposed` by choice.
- **Decision**: FIXED (documented; the gap itself is deferred to S-03)

### F7 — Ledger write can cost the user the note it was counting

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §5 / Phase 3 §2 (`RecordGenerationAsync`)
- **Detail**: The plan says when the method is called ("wyłącznie po pomyślnym zakończeniu strumienia") but never what happens if it fails. It is called from inside the generation handler's `try` block, whose `catch` clears the note panel — so an escaping `OperationCanceledException` would discard a successfully generated note in exchange for a telemetry row nothing reads in real time.
- **Fix**: State that the ledger write is best-effort — logged and swallowed, never allowed to reach the handler's `catch` — and note on the page side that the call happens after the buffer is handed to the editor.
- **Decision**: FIXED

### F8 — Result types required by contract but absent from the file lists

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §2, Phase 2 §5
- **Detail**: Both sections require a "typowany wynik" but the file lists name only `NoteValidator.cs` and `NoteService.cs`. The cited precedent (`MarkdownImportValidator.cs` / `MarkdownImportResult.cs`) splits them; the plan did not.
- **Fix**: Add `Notes/NoteValidationResult.cs` and `Notes/NoteSaveResult.cs` to the respective file lists.
- **Decision**: FIXED

## Note on `change.md` status

The skill's default is to set `status: plan_reviewed` when saving a report. That is not
applied here: this review ran *after* implementation, and `implemented` is the more
advanced state. Regressing it would misreport the change as awaiting implementation.
Only `updated` was bumped.
