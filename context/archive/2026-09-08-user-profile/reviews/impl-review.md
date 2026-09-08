<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Zarządzanie kontem (S-07)

- **Plan**: context/changes/user-profile/plan.md
- **Scope**: Phases 1–4 of 4 (full plan)
- **Date**: 2026-09-08
- **Verdict**: NEEDS ATTENTION (triaged: 6 fixed, 2 skipped, 1 accepted)
- **Findings**: 0 critical, 4 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Submitted passwords are echoed back into the response HTML on every failure path

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Components/Pages/Account/Profile.razor:262, :313
- **Detail**: `InputText` renders `value="@CurrentValue"`; `type="password"` is a pass-through attribute and does not suppress it. The success path resets the model (`PasswordInput = new ChangePasswordForm()`, :277) but the failure paths `return` without clearing, so a wrong current password, a `SamePassword`/`WeakPassword` rejection, or a wrong delete-confirmation password re-renders the page with the plaintext password in a `value=` attribute. `DeleteInput` is never reset at all. Confirmed empirically during phase-3 manual testing: after a wrong current password both fields came back populated. Puts credential material into view-source, screenshots, and any intermediary that logs response bodies. `Login.razor` has the same latent issue.
- **Fix**: Reset the model before each failure `return` — `PasswordInput = new ChangePasswordForm();` and `DeleteInput = new DeleteAccountForm();`.
  - Strength: Two lines; matches what the success path already does; removes the class entirely.
  - Tradeoff: The user retypes the current password after a mistake — standard behaviour for password forms.
  - Confidence: HIGH — verified in the rendered page during manual testing.
  - Blind spot: `Login.razor` and `Register.razor` share the shape and are out of this plan's scope.
- **Decision**: FIXED — model reset on every failure path in Profile.razor (both forms); Login.razor and Register.razor clear only the password so the address survives.

### F2 — Sign-out after a successful account deletion is not guaranteed

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Components/Pages/Account/Profile.razor:331
- **Detail**: The `try/catch` at :319-329 covers only `DeleteCurrentAccountAsync`. `AuthCookie.SignOutAsync` at :331 sits outside it. If it throws, the account is gone and the cookie is not cleared — leaving the user authenticated over a deleted account, where every read returns nothing and every write throws from `StampOwners`. That is exactly the "indistinguishable from data loss" state `Program.cs:91-102` exists to prevent, and `IsPastSessionCap` will not retire it, so it persists for up to the 14-day sliding window. Unlikely to throw, but it follows the only irreversible operation in the product.
- **Fix A ⭐ Recommended**: Invert the order — `SignOutAsync` before the delete.
  - Strength: Makes the bad direction the safe one. `SignOutAsync` only queues response headers and does not mutate `HttpContext.User`, so `db.CurrentUserId` is still populated for the delete. A delete failure then leaves the user signed out over intact data — recoverable by signing in again.
  - Tradeoff: Reverses the order the plan specified, so the plan's Critical Implementation Details need an addendum.
  - Confidence: HIGH — cookie sign-out does not touch the request principal.
  - Blind spot: Not exercised against a mid-request failure; the reasoning is from the framework's behaviour, not a test.
- **Fix B**: Keep the order, wrap `SignOutAsync` in its own `try/catch` that logs `Critical` and redirects to `/login` regardless.
  - Strength: Leaves the plan's stated order intact; the redirect still gets the user off the authenticated shell.
  - Tradeoff: The stale cookie survives on that browser until the sliding window closes.
  - Confidence: MEDIUM — mitigates the symptom rather than the state.
  - Blind spot: A user who navigates back before the redirect still holds the cookie.
- **Decision**: FIXED via Fix A — SignOutAsync now runs before the delete; a delete failure leaves the user signed out over intact data. Copy updated to say so.

### F3 — The display-name save is the one write path on this page with no error handling

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Components/Pages/Account/Profile.razor:214
- **Detail**: `SetDisplayNameAsync` calls `SaveChangesAsync`, which throws `DbUpdateException`/`DbException` when Postgres is unreachable and `InvalidOperationException` from `StampOwners` when the session lapsed between render and submit. Nothing catches it, so it unwinds to the stock error page. Inconsistent with this page's own load path (:189 catches `DbException or InvalidOperationException`), with the delete handler (:323 catches `Exception`), and with `Materials/Import.razor:117-127`, whose comment calls out `StampOwners` as the failure that path produces. The existing Polish copy "Nie udało się zapisać nazwy. Spróbuj ponownie za chwilę." is currently reachable only via `ProfileMissing`.
- **Fix**: Wrap the call in `catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException)` and set `displayNameError` to the copy that already exists.
- **Decision**: SKIPPED — user decision.

### F4 — `CreateForAsync`'s widening of the ownership model is guarded only by a doc comment

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: Data/UserScopedDbContextFactory.cs:51, Profiles/ProfileService.cs:85
- **Detail**: Not reachable with attacker-controlled input today — audited every call site: `CreateForAsync` is called from `CreateAsync` (:27) and from `GetDisplayNameAtSignInAsync` (:89), which has one caller, `Login.razor:100`, passing GoTrue's verified `result.UserId`. The read is a single nullable string. The problem is enforcement asymmetry: this repo pins its other two ownership invariants *executably* — `DataAccessBoundaryTests` scans the assembly for `DbContext`/`IDbContextFactory<>` dependencies, and `AccountDeletionServiceTests` reflects over the delete signature — while both new members are `public`, take a bare `Guid`, and nothing fails if a future page passes `[SupplyParameterFromQuery] Guid userId` to either. That is the misuse the XML doc warns about, and the doc is the entire control.
- **Fix A ⭐ Recommended**: Add a guard-rail test in the `DataAccessBoundaryTests` style asserting the caller set of both members.
  - Strength: Matches how this repo already enforces its other two ownership rules; fails the build on misuse rather than relying on a reader noticing a comment.
  - Tradeoff: IL-level caller scanning is more involved than the existing dependency scan.
  - Confidence: MEDIUM — the pattern exists here, but for constructor parameters rather than call sites.
  - Blind spot: Have not established how cheaply callers can be enumerated with the reflection already in use.
- **Fix B**: Make the invariant structural — `CreateForAsync(VerifiedUserId id, …)` where `VerifiedUserId` is constructible only from an `AuthResult`.
  - Strength: The type system carries the guarantee; no test to remember to keep.
  - Tradeoff: A new type threaded through the auth path for one call site.
  - Confidence: MEDIUM — clean, but more code than the risk currently warrants.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — tests/10xNotes.Tests/OwnershipEscapeHatchTests.cs walks the IL of every method in the app assembly and pins the caller set of both members. Verified to bite: injecting a rogue caller failed the test naming it, then reverted.

### F5 — The delete proves by email but deletes by id, with no cross-check

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Components/Pages/Account/Profile.razor:306
- **Detail**: `AuthClient.SignInAsync(Email, …)` proves the password for whatever account GoTrue resolves that *email* to, while the DELETE targets the *id* claim. Same account in every reachable case — both claims are minted together in `AuthCookie.BuildPrincipal` and the cookie is DataProtection-signed. The one divergent case constructible (address deleted and re-registered while an old cookie survives) makes the DELETE hit an absent row: 0 rows, logged, no cross-account damage. Flagged because this is the only irreversible operation in the product and the check is one line.
- **Fix**: After a successful `proof`, assert `proof.UserId` equals the principal's `NameIdentifier` and treat a mismatch as a hard failure.
- **Decision**: SKIPPED — user decision; the divergent case hits an absent row.

### F6 — The password minimum counts UTF-16 units while the maximum counts UTF-8 bytes

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Auth/PasswordValidator.cs:59
- **Detail**: `password.Length < MinLength` counts code units, so four emoji (8 units, 4 characters) satisfies a rule whose message says "co najmniej 8 znaków" — while `ExcessCharacters` in the same file deliberately counts runes to avoid exactly this. Cosmetic in effect, but this change's own thesis, and the `lessons.md` rule it applies, is that unit mismatches between layers are the bug class being removed.
- **Fix**: Count runes for the minimum too, or concede the unit in the `MinLength` remarks.
- **Decision**: FIXED — PasswordLimits.CharacterCount added; the minimum now counts code points like ExcessCharacters. Two tests pin it.

### F7 — The register hint states only the minimum; the plan said both bounds

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Components/Pages/Account/Register.razor:31
- **Detail**: Plan phase 1 change #5 said "the `form-text` hint states both bounds in Polish". The hint states only the minimum. Deliberate, user-directed mid-implementation, and documented in `change.md`: the maximum is a byte bound, so no character number is both true and useful (72 latin / 36 polish / 18 emoji), and the over-long case is handled by a message that says exactly how many characters to remove. Enforcement is still byte-based, so the defect the plan set out to fix is fixed. Recorded so the plan and the code are not silently divergent.
- **Fix**: None needed — the plan text is the thing that is now out of date, not the code.
- **Decision**: FIXED — addendum added to plan.md phase 1 change #5 recording the deliberate contract change.

### F8 — `AccessToken` is populated more widely than its own documentation claims

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Auth/AuthResult.cs:57
- **Detail**: The doc says the token "is a local in that flow", but `ParseSuccess` populates `AccessToken` for every `token?grant_type=password` response, so a live bearer token also reaches `Login.razor:63` and `Profile.razor:306`, where nothing uses it. Never logged (`PrintMembers` redacts it), never cookie'd, never persisted — hygiene rather than a leak.
- **Fix**: Narrow the token to the change-password path, or relax the doc to match what the type does.
- **Decision**: FIXED — AuthResult.AccessToken remarks relaxed to state what is actually guaranteed.

### F9 — Unplanned files, all documented

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: Auth/PasswordRequirementAttribute.cs, Components/Layout/NavMenu.razor.css, tests/10xNotes.Tests/SqliteTestContext.cs
- **Detail**: Three files outside the plan's list. `PasswordRequirementAttribute` is the mechanism delivering the planned "validates through `PasswordValidator`" on both forms. `NavMenu.razor.css` fixes a pre-existing truncation bug surfaced by 200-character names (documented in `change.md`). `SqliteTestContext` gained an opt-in `logParameterValues` flag, required by plan criterion 4.4 to prove *whose* id the DELETE binds — test-project only, defaults off, single caller. No "What We're NOT Doing" boundary was crossed: no migration, no `service_role`, no email change, no statistics, no session revocation, no field beyond `DisplayName`.
- **Fix**: None needed — recorded for the archive trail.
- **Decision**: ACCEPTED — no action; recorded for the archive trail.

## Verified as well handled (no findings)

- **The account delete cannot be aimed at another user.** Id traced end to end: `db.CurrentUserId` ← `CreateAsync` ← `ICurrentUserAccessor` ← the DataProtection-signed cookie principal, with a session-cap re-check. No user parameter, `Guid.Empty` fails closed before any SQL, `ExecuteSqlInterpolatedAsync` binds `@p0`. Four tests pin it including the parameter value. FK `ON DELETE CASCADE` to `auth.users` verified on all five tables; `notes → source_materials` is deliberately `NO ACTION` so the cascade does not trip on it.
- **`PUT /user` cannot be issued without a proven current password.** `PutPasswordAsync` is private with one caller that returns early on failed re-auth and again on an empty token. Body carries only `password`.
- **Static-SSR correctness.** No `@rendermode`; the cascaded `HttpContext` is non-null; cookie re-issue and sign-out both run before the response starts; all three form models are assigned synchronously so `EditForm` never sees a null `Model`. Antiforgery wired; `[Authorize]` plus deny-by-default `FallbackPolicy`.
- **`ExcessCharacters` and `CharacterNoun` are correct.** Verified against ASCII, Polish and astral input, and the Polish declension checked at 1, 2, 5, 12, 14, 22, 102, 111, 112.
- **`DisplayNameValidator` errs in the safe direction.** UTF-16 units against a `varchar(200)` Postgres counts in code points; unit count is always ≥ code-point count, so the validator can only be stricter than the column.
- **Pattern consistency.** Both services take `UserScopedDbContextFactory` by primary constructor like `NoteService`/`SourceMaterialService`; validators mirror `NoteValidator`/`NoteValidationResult`; `PasswordLimits` follows `HubWireLimits`. `IgnoreQueryFilters()` appears nowhere in app code. `NavMenu` interpolates the display name with `@`, so it is HTML-encoded — no XSS.
- **No migration in this change**, so the "every new `public` table needs RLS" rule does not apply. Supabase security advisor reports nothing new: seven `rls_enabled_no_policy` INFO entries are the intended deny-all posture, and the `handle_new_user` and leaked-password warnings are pre-existing.

## Success criteria

All automated criteria across the four phases were re-run: `dotnet build` clean (0 warnings), `dotnet test` 346/346, `DataAccessBoundaryTests` passing. Per-criterion suites present: PasswordValidator 34, DisplayNameValidator 13, ProfileService 18, AccountDeletion 7, SupabaseAuthClient 32.

Manual criteria: 10 of 21 were verified directly in a browser against the real Supabase project during implementation (1.9, 2.6–2.9, 2.11, 3.8, 3.10, 3.11, 4.10), with database-level confirmation for the display-name null/empty distinction and for the delete refusal leaving all row counts unchanged. The remaining 11 — including 4.8 and 4.9, the deletion cascade — were confirmed by the user and leave no evidence in the diff by their nature; the cascade in particular is unprovable by the test suite, as the plan and `AccountDeletionServiceTests` both state.
