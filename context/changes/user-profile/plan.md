# Profil użytkownika — Implementation Plan

## Overview

Add a `/profile` page giving the signed-in user three account capabilities that do not exist today: setting a display name, changing their password, and deleting their account. The display name lands on the `profiles` table that F-01 created and nothing has ever used; the password change and the account deletion each need a path through Supabase Auth that the application deliberately closed off when it decided the cookie would be its only session concept.

The change also fixes a pre-existing defect it would otherwise inherit: the register form advertises a longer password than GoTrue will accept.

## Current State Analysis

**What exists.**

- `Data/Entities/Profile.cs` — `Id`, `OwnerId` (unique, FK → `auth.users` `ON DELETE CASCADE`), nullable `DisplayName` (max 200), `CreatedAt`. It implements `IOwnedByUser`, so it inherits the global query filter, the insert-time owner stamp, and the `OwnerId`-as-concurrency-token guard from `Data/AppDbContext.cs`.
- `Migrations/20260727200717_AddHandleNewUserTrigger.cs` — a `SECURITY DEFINER` trigger on `auth.users` inserts a `profiles` row for every account, including ones created in the Supabase dashboard. **Every account already has a profile row**; no code path has to create one.
- `Auth/SupabaseAuthClient.cs` — two GoTrue endpoints (`signup`, `token?grant_type=password`). `ParseSuccess` reads only `id` and `email`; the access and refresh tokens are parsed past and dropped.
- `Auth/AuthCookie.cs` — mints the cookie from four claims (`NameIdentifier`, `Email`, `Name`, `session_cap`). `SignInAsync` is the single place the claim set is built.
- `Components/Pages/Account/{Login,Register,Logout}.razor` — all static SSR, all `EditForm` + `[SupplyParameterFromForm]` + `DataAnnotationsValidator`, all mapping an `AuthFailureReason` to Polish copy.
- `Hosting/HubWireLimits.cs` — the project's worked example of deriving a bound from a named constant and pinning the arithmetic in a test rather than a comment.

**What is missing.** No `/profile` route. No account-settings surface of any kind. Nothing in the application reads or writes `Profiles` — the only references outside `Migrations/` are in `OwnerScopingTests` and `CurrentUserAccessorTests`. `Components/Layout/NavMenu.razor:34` shows `context.User.Identity?.Name`, which `AuthCookie.SignInAsync` sets to the email.

**Constraints discovered.**

- The app's `postgres` role has `DELETE` on `auth.users` — verified 2026-09-08 via `has_table_privilege('postgres','auth.users','DELETE')` → `true`. Account deletion therefore needs no `service_role` key.
- GoTrue has **no** self-service delete endpoint. `DELETE /admin/users/{id}` is admin-only; its hard-delete path is `tx.Destroy(user)`, i.e. exactly the row delete plus cascade that direct SQL performs.
- GoTrue's `PUT /user` requires a bearer access token (`requireAuthentication` middleware) and caps a password at 72 bytes.
- `tests/10xNotes.Tests/DataAccessBoundaryTests.cs` fails the build if any type in the app assembly takes a `DbContext` or an `IDbContextFactory<>`. `UserScopedDbContextFactory` is the only exemption.
- The SQLite test harness has no `auth.users` table, so the deletion cascade is not provable there.

## Desired End State

A signed-in user can reach `/profile` from the nav and:

- see the email their account is registered under, set or clear a display name, and see that name replace the email in the nav on the next page render;
- change their password by supplying the current one and a new one, with GoTrue's rejections rendered as Polish copy rather than a generic failure;
- delete their account by re-entering their password, after which they land signed-out on `/login` and no row belonging to them remains in any table.

Verified by: `dotnet test` green; `dotnet build` clean; and the manual checklist in each phase, run against the real Supabase project — the cascade in particular cannot be proven by the test suite.

### Key Discoveries:

- The `profiles` row always exists (`Migrations/20260727200717_AddHandleNewUserTrigger.cs:30-36`) — read paths need no "create if missing" branch.
- `Auth/SupabaseAuthClient.cs:110-130` (`ParseSuccess`) already parses the token-endpoint payload and discards `access_token`; returning it is an additive change to `AuthResult`, not a restructure.
- `Components/Pages/Account/Register.razor:79` validates `StringLength(100, MinimumLength = 8)` against GoTrue's 72-**byte** cap. Polish diacritics are 2 bytes each in UTF-8, so the real character budget is lower than 72 for realistic passwords, and `TextLimits.Truncate` counts UTF-16 units — a third unit that must not be conflated with the other two.
- `Auth/SupabaseAuthClient.cs:70-84` (`ClassifyFailure`) maps GoTrue's `error_code`. `weak_password` is already handled; `same_password` and the over-length validation failure are not, and today fall through to `Unavailable`.
- `tests/10xNotes.Tests/SqliteTestContext.cs:78-89` exposes a `log` hook on `Factory` that `ProjectionGuardTests` uses to assert on emitted SQL — the seam for proving the account delete is owner-scoped.
- `Data/AppDbContext.cs:35` exposes `CurrentUserId` on the context itself, so a service that needs the signed-in user's id can read it off the context `UserScopedDbContextFactory` handed it, without taking a second dependency.

## What We're NOT Doing

- **Usage or account statistics** — generation count against the daily limit, note/material counts, member-since. Explicitly cut from scope.
- **Changing the account email.** `PUT /user` supports it, but it triggers a magic-link confirmation flow the app has no way to complete.
- **"Log out everywhere" / session revocation.** Not implementable as the app stands: the cookie is self-contained and DataProtection-signed with no server-side session store. A password change does not invalidate cookies already issued; `AuthCookie.SessionCap` (30 days) remains the only outer bound.
- **Soft delete or an undo window** for account deletion.
- **Preserving `note_events` past account deletion.** The cascade stays as `Migrations/20260817200154_AddNoteAndNoteEvent.cs` specifies.
- **Password reset for a user who is signed out** (forgot-password). This change covers only a signed-in user changing a password they know.
- **Any schema migration.** All three features run against the existing tables.
- **Adding the `service_role` key** to configuration.
- **Avatars, bios, or any profile field beyond `DisplayName`.**

## Implementation Approach

Three capabilities on one static-SSR page, backed by two new services and two new calls on the existing GoTrue client.

**Static SSR is forced, not preferred.** Two of the three actions need an `HttpContext` whose response has not started: re-issuing the auth cookie so the nav picks up a changed display name, and `SignOutAsync` after deletion. Both are impossible from an interactive circuit — the same constraint `Login.razor` and `Logout.razor` already document. So `/profile` follows their shape exactly: three `EditForm`s with distinct `FormName`s, `[SupplyParameterFromForm]` inputs, `DataAnnotationsValidator`, and per-form error state.

**The password change re-authenticates rather than storing tokens.** `POST token?grant_type=password` with the current password proves the user knows it and yields an access token; that token is passed straight to `PUT /user` and discarded when the method returns. It is never written to the cookie, the database, or a field. This keeps `AuthCookie`'s "the cookie is the only session concept" true, needs no new secret, and makes proving the old password a structural property of the flow rather than a check someone could remove.

**Account deletion is one SQL statement on the existing connection.** `DELETE FROM auth.users WHERE id = @currentUserId` cascades into `profiles`, `source_materials`, `notes`, `note_events`, `generation_quotas` and GoTrue's own `sessions`/`identities`/`refresh_tokens` in a single statement. The id comes from `AppDbContext.CurrentUserId` — never from the form — so the operation cannot be aimed at another account regardless of what is posted.

**One password bound, byte-measured, shared by both forms.** A new limits type owns the real GoTrue cap and both forms validate against it, with a test pinning the relationship. This is the `lessons.md` rule applied directly, and it is what makes fixing the register form nearly free.

## Critical Implementation Details

**Bytes, not characters.** GoTrue's 72 is a bcrypt byte limit. `TextLimits.Truncate` counts UTF-16 code units and `StringLength` counts characters — three different units for one value. The password rule must measure `Encoding.UTF8.GetByteCount(password)`; a Polish password of 50 characters can exceed 72 bytes, and a character-based check would pass it through to a GoTrue rejection the user cannot act on.

**Ordering inside the display-name save.** The database write must succeed before the cookie is re-issued. Re-issuing first leaves the nav showing a name that was never stored, which is worse than the write failing visibly — and `SignInAsync` must run before the response starts, so it cannot be deferred to after a render.

**`OnValidatePrincipal` does not fire on a fresh sign-in.** `Program.cs:88-100` rejects a past-cap principal on cookie *validation*, never on `SignInAsync`. Re-issuing the cookie for a display-name change therefore resets the 30-day session cap as a side effect. Accepted, but do not add a cap check to the re-issue path expecting it to preserve the original cap — preserving it would require carrying the old claim forward deliberately.

**The delete must sign out in the same request.** Once `auth.users` is gone, the cookie still authenticates a principal whose every query returns nothing and whose every write throws. That is the "looks exactly like data loss" failure `Program.cs:88` already guards against for expired sessions; deletion must not reintroduce it by leaving the cookie in place.

---

## Phase 1: Password limit contract & auth-client seams

### Overview

Establish the shared foundations Phases 2–4 consume: one byte-measured password bound used by both forms, the GoTrue failure codes the new flows produce, and an access token the client stops discarding. Fixes the register-form limit mismatch as a consequence.

### Changes Required:

#### 1. Password bound

**File**: `Auth/PasswordLimits.cs` (new)

**Intent**: Own the real password bound in one place, measured in the unit GoTrue actually applies, so the register form and the change-password form cannot disagree about what a valid password is. Follows the shape of `Hosting/HubWireLimits.cs` — a named constant with the reasoning recorded beside it.

**Contract**: A `MaxBytes` constant of 72 (bcrypt's limit, as enforced by GoTrue's `PUT /user` and `signup`), a `MinLength` of 8 matching the current register rule, and a validation entry point that measures the password with `Encoding.UTF8.GetByteCount` rather than `.Length`. The byte-vs-character distinction is the load-bearing part and must be stated in the doc comment.

#### 2. Password rule

**File**: `Auth/PasswordValidator.cs` (new)

**Intent**: Turn a candidate password into a pass/fail with a classified reason, so both forms and the change-password service apply identical rules and the rules are testable without a form.

**Contract**: A static `Validate(string? password)` returning a result carrying a failure reason distinguishing "too short", "too many bytes", and "missing". Mirrors the shape of `Notes/NoteValidator.cs` and its `NoteValidationResult`.

#### 3. New GoTrue failure reasons

**File**: `Auth/AuthResult.cs`

**Intent**: Give the UI reasons it can word in Polish for the two rejections the change-password flow can produce and which today collapse into `Unavailable`.

**Contract**: Add `SamePassword` to `AuthFailureReason`. Add an `AccessToken` property to `AuthResult`, populated only by the token-grant path and empty elsewhere.

#### 4. Classify the new codes; return the token

**File**: `Auth/SupabaseAuthClient.cs`

**Intent**: Map GoTrue's `same_password` error code to the new reason, and stop discarding the access token on the token-grant path so the change-password flow can use it.

**Contract**: `ClassifyFailure` gains a `"same_password"` arm returning `AuthFailureReason.SamePassword`. `ParseSuccess` reads a root-level `access_token` when present and passes it to `AuthResult.Success`; absence stays valid, because the signup response may carry none. No change to the two existing endpoints or to the discarding of the refresh token.

#### 5. Align the register form

**File**: `Components/Pages/Account/Register.razor`

**Intent**: Replace the `StringLength(100, MinimumLength = 8)` attribute — which advertises 28 characters more than GoTrue accepts — with the shared rule, so an over-long password is refused in Polish by the form instead of returning "Nie udało się utworzyć konta" from a misclassified GoTrue error.

**Contract**: The password field validates through `PasswordValidator`; the `form-text` hint states both bounds in Polish. Registration behavior for every currently-valid password is unchanged.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- A test pins `PasswordLimits.MaxBytes` to GoTrue's documented bcrypt bound, so the constant cannot drift silently
- A test proves the rule measures UTF-8 bytes, not characters: a password under 72 characters but over 72 bytes (Polish diacritics) is rejected
- `PasswordValidator` tests cover too-short, too-many-bytes, empty, and valid
- `SupabaseAuthClientTests` covers `same_password` → `AuthFailureReason.SamePassword`
- `SupabaseAuthClientTests` covers the token-grant path returning a non-empty `AccessToken`, and the signup path tolerating a payload with none

#### Manual Verification:

- Registering with a valid password still works end-to-end against the real Supabase project
- Registering with a 100-character password now shows a Polish validation message on the form rather than a generic failure

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: Profile page & display name

### Overview

Stand up `/profile` as a static-SSR page and deliver the first of its three sections: view the account email, set or clear a display name, and have the nav show it.

### Changes Required:

#### 1. Display-name rule

**File**: `Auth/DisplayNameValidator.cs` (new)

**Intent**: Settle the empty-versus-null question in one place so a blank submission can never store `""` and render an invisible nav entry.

**Contract**: `Normalize(string? input)` trims and returns `null` for anything that is empty or whitespace after trimming; a validation entry point rejects anything over 200 characters after trimming, matching the column width declared in `Data/AppDbContext.cs:56`.

#### 2. Profile service

**File**: `Profiles/ProfileService.cs` (new)

**Intent**: Read and update the signed-in user's profile without any page touching the database, following `Notes/NoteService.cs`.

**Contract**: Takes `UserScopedDbContextFactory` (never a `DbContext` or `IDbContextFactory<>` — `DataAccessBoundaryTests` fails the build otherwise). A read returning the current display name, and an update applying a normalized name. No owner filter is written by hand; the global query filter and `StampOwners` supply it. The update targets the single row the trigger guarantees, so a missing row is an error condition rather than a create.

#### 3. Display name in the cookie

**File**: `Auth/AuthCookie.cs`

**Intent**: Carry the display name on the principal so `NavMenu` needs no query on any render, and give the profile page a way to refresh it after an edit.

**Contract**: `SignInAsync` gains an optional display-name parameter and writes it as a claim when present. A separate entry point re-issues the cookie for the already-signed-in user with a new display name, reusing the same claim-building code so the two cannot diverge. Note the side effect recorded in Critical Implementation Details: re-issuing resets the session cap.

#### 4. Read the name at sign-in

**File**: `Components/Pages/Account/Login.razor`

**Intent**: Populate the display-name claim when a session starts, so a user who set a name sees it after signing in again.

**Contract**: After a successful `SignInAsync` against GoTrue and before `AuthCookie.SignInAsync`, read the profile's display name and pass it through. A read failure must not block sign-in — the claim is a convenience, and falling back to the email is already the defined behavior.

#### 5. Nav shows the name

**File**: `Components/Layout/NavMenu.razor`

**Intent**: Render the display name when the principal carries one, the email when it does not.

**Contract**: The `nav-user` block reads the display-name claim with `context.User.Identity?.Name` as the fallback. No data access is added to the component.

#### 6. The page

**File**: `Components/Pages/Account/Profile.razor` (new)

**Intent**: The `/profile` route and its first section. Built to hold three independent forms from the start, so Phases 3 and 4 add sections rather than restructure the page.

**Contract**: `@page "/profile"`, `@attribute [Authorize]`, static SSR (no `@rendermode`) — see Critical Implementation Details for why this is forced. Shows the account email read-only. One `EditForm` with `FormName="display-name"` and its own error/success state, distinct from the slots Phases 3 and 4 will add. Polish copy throughout. Save order: persist, then re-issue the cookie, then redirect to `/profile`.

#### 7. Registration

**File**: `Program.cs`

**Intent**: Register `ProfileService` alongside the existing scoped services.

**Contract**: `builder.Services.AddScoped<ProfileService>();` beside the `NoteService` / `SourceMaterialService` registrations.

#### 8. Nav entry

**File**: `Components/Layout/NavMenu.razor`

**Intent**: Give the page a way in for authenticated users.

**Contract**: A `NavLink` to `profile` inside the `<Authorized>` block, above the logout entry.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- `DataAccessBoundaryTests` still passes — proves `ProfileService` took the sanctioned seam
- `DisplayNameValidator` tests cover trimming, whitespace-only → null, empty → null, the 200-character boundary, and a name containing an emoji
- `ProfileService` tests against the SQLite harness cover reading a name, setting one, clearing one back to null, and a second user being unable to see or modify the first user's profile

#### Manual Verification:

- `/profile` renders for a signed-in user and shows the correct account email
- Setting a display name persists it and the nav shows it after the redirect
- Clearing the name reverts the nav to the email
- A 200-character name is accepted; a longer one is refused in Polish
- Signing out and back in still shows the display name
- `/profile` redirects an anonymous visitor to `/login`

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 3: Change password

### Overview

Add the second section of `/profile`: change the password by proving the current one, via a re-authentication round trip that keeps GoTrue's tokens out of the session.

### Changes Required:

#### 1. Change-password call

**File**: `Auth/SupabaseAuthClient.cs`

**Intent**: Perform the two-step change — re-authenticate with the current password to obtain an access token, then `PUT /user` with the new password — as one operation, so no caller can perform the second step without the first.

**Contract**: A method taking email, current password and new password, returning an `AuthResult`. Internally: `POST token?grant_type=password`, and on failure return that result unchanged so a wrong current password surfaces as `InvalidCredentials`; on success `PUT user` with an `Authorization: Bearer <access token>` header, the project `apikey` header the client already sends, and a body carrying only `password`. The token is a local and is never returned to the caller. Failure classification goes through the existing `ClassifyFailure`, so `weak_password` and `same_password` arrive as reasons the page can word.

#### 2. Password section on the page

**File**: `Components/Pages/Account/Profile.razor`

**Intent**: The form and its Polish copy, failing independently of the other two sections.

**Contract**: A second `EditForm` with `FormName="change-password"`, fields for current password and new password with the appropriate `autocomplete` values (`current-password`, `new-password`), client-side validation through `PasswordValidator`, and its own error/success slot. Reason-to-copy mapping covers `InvalidCredentials` (wrong current password), `WeakPassword`, `SamePassword`, and `Unavailable`. The email for the re-authentication comes from the principal's email claim, never from a form field.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- `SupabaseAuthClientTests` proves the change hits `token?grant_type=password` first and `user` second, with the project key on both
- A test proves the `PUT user` request carries the bearer token returned by the re-authentication step
- A test proves a failed re-authentication returns `InvalidCredentials` and **never issues the `PUT`**
- A test proves the request body carries only the new password

#### Manual Verification:

- Changing the password with the correct current password succeeds, and the new password works on the next sign-in
- A wrong current password shows the Polish "wrong password" message and the password is unchanged
- Reusing the current password as the new one shows the `SamePassword` copy
- A password below the minimum, and one over the byte bound, are both refused in Polish
- The display-name form still works while the password form is showing an error
- The user remains signed in after a successful change (the known, accepted limitation)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 4: Delete account

### Overview

Add the third section: password-confirmed, irreversible account deletion that removes the `auth.users` row and lets the existing FK cascades take everything else, then signs the user out.

### Changes Required:

#### 1. Deletion service

**File**: `Profiles/AccountDeletionService.cs` (new)

**Intent**: Delete the signed-in user's identity row so the database cascades remove every trace of them, with the target id sourced from the context rather than from any caller.

**Contract**: Takes `UserScopedDbContextFactory`. Issues a parameterised `DELETE FROM auth.users WHERE id = {currentUserId}` where the id is read from `AppDbContext.CurrentUserId`, never from a parameter — the method takes no id at all. Refuses to run when `CurrentUserId` is `Guid.Empty`, matching the fail-closed posture `StampOwners` takes. Uses interpolated-SQL execution so the id is a parameter and not concatenated. Returns whether exactly one row was removed. Note that raw SQL bypasses the global query filter by construction, which is precisely why the id must not be a parameter.

#### 2. Danger zone on the page

**File**: `Components/Pages/Account/Profile.razor`

**Intent**: The confirmation form, visually separated so it cannot be mistaken for the other two, and its Polish copy stating plainly what is destroyed.

**Contract**: A third `EditForm` with `FormName="delete-account"`, one password field (`autocomplete="current-password"`), and its own error slot. Copy names what is deleted — notes, source materials and the account itself — and that it cannot be undone. On submit: re-authenticate the password against GoTrue; on failure show the wrong-password message and stop; on success call the deletion service, then `AuthCookie.SignOutAsync`, then redirect to `/login` with no message (per the scope decision).

#### 3. Registration

**File**: `Program.cs`

**Intent**: Register `AccountDeletionService`.

**Contract**: `builder.Services.AddScoped<AccountDeletionService>();` beside the other scoped services.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Test suite passes: `dotnet test`
- `DataAccessBoundaryTests` still passes — proves `AccountDeletionService` took the sanctioned seam
- A test using `SqliteTestContext.Factory`'s `log` hook proves the emitted `DELETE` targets `auth.users`, carries the current user's id **as a parameter**, and that the id equals `CurrentUserId`
- A test proves the service refuses to execute when no user is signed in (`CurrentUserId` is `Guid.Empty`)
- A test proves the service's signature exposes no way to supply a different user's id

#### Manual Verification:

- **Run against a throwaway account on the real project — the cascade is not provable by the test suite.**
- Deleting with the correct password signs the user out and lands them on `/login`
- After deletion, querying the database directly shows zero rows for that owner in `profiles`, `source_materials`, `notes`, `note_events` and `generation_quotas`, and no row in `auth.users`
- A second account's data is completely untouched by the first account's deletion
- A wrong password shows the Polish message and the account still exists
- Signing in with the deleted account's credentials fails
- A pre-existing browser tab holding the deleted account's cookie no longer functions and does not show an authenticated shell over empty data
- The Supabase security advisor reports nothing new

**Implementation Note**: This is the only irreversible operation in the product. Do not run the manual checklist against an account holding real data.

---

## Testing Strategy

### Unit Tests:

- `PasswordValidator` — too short, over the byte bound, empty, valid; and the byte-versus-character case using Polish diacritics, which is the failure the shared constant exists to prevent
- `PasswordLimits` — the constant pinned to GoTrue's documented bound
- `DisplayNameValidator` — trimming, whitespace-only and empty collapsing to null, the 200-character boundary, a name containing an emoji
- `SupabaseAuthClient` — endpoint, headers and body for the change-password flow; the ordering guarantee that a failed re-authentication issues no `PUT`; the new `same_password` classification; the access token surviving `ParseSuccess`

### Integration Tests:

- `ProfileService` against the SQLite harness — read, set, clear, and cross-account isolation
- `AccountDeletionService` against the SQLite harness via the SQL log hook — statement shape, parameterisation, id provenance, and the unauthenticated refusal

### Manual Testing Steps:

1. Sign in, open `/profile`, confirm the email shown is the account's.
2. Set a display name; confirm the nav shows it after the redirect and after a full page reload.
3. Clear the name; confirm the nav reverts to the email.
4. Attempt a 201-character name; confirm the Polish validation message.
5. Change the password with a wrong current password; confirm the message and that the old password still works.
6. Change the password correctly; sign out; sign in with the new password.
7. Attempt to register a new account with a 100-character password; confirm a form-level Polish message.
8. On a **throwaway** account with at least one material and one saved note: delete it with a wrong password (confirm refusal), then with the correct one.
9. Confirm the redirect to `/login`, then query the database for the deleted owner id across all five tables and `auth.users` — expect zero rows everywhere.
10. Confirm a second account's materials and notes are intact.

## Performance Considerations

The display name travels as a cookie claim, so the nav costs no query on any render — the reason that decision was taken. `Login.razor` gains one indexed single-row read per sign-in, against the unique index on `profiles.owner_id`. The password change costs two GoTrue round trips instead of one, on a page used rarely. The account delete is a single statement whose cascade is proportional to the user's own data, which the PRD puts at "small". Nothing here is on a hot path.

## Migration Notes

**No schema migration.** All three features run against tables and constraints that already exist, so there is nothing to roll back and nothing that breaks a Coolify rollback's backward-compatibility requirement.

One behavioral note for a rollback: display-name claims written into cookies by this version are simply ignored by the previous version, which reads `ClaimTypes.Name`. No cookie becomes invalid.

## References

- Roadmap: no item — this change is outside the PRD and roadmap; see `context/changes/user-profile/change.md`
- Lessons applied: `context/foundation/lessons.md` — "An advertised limit must be one every layer beneath it can carry"
- Limit-constant precedent: `Hosting/HubWireLimits.cs`
- Service precedent: `Notes/NoteService.cs`, `SourceMaterials/SourceMaterialService.cs`
- Static-SSR form precedent: `Components/Pages/Account/Login.razor`, `Components/Pages/Account/Logout.razor`
- Ownership model: `Data/AppDbContext.cs`, `Data/IOwnedByUser.cs`
- Cascade definitions: `Migrations/20260817200154_AddNoteAndNoteEvent.cs`, `Migrations/20260817162727_AddSourceMaterial.cs`, `Migrations/20260817174948_AddGenerationQuota.cs`, `Migrations/20260727173434_InitialPersistenceBaseline.cs`
- Profile row guarantee: `Migrations/20260727200717_AddHandleNewUserTrigger.cs`
- GoTrue API: `PUT /user` (auth required, 72-byte password cap, `same_password` / `weak_password` codes); `DELETE /admin/users/{id}` hard delete is `tx.Destroy(user)` — verified via Context7 against `/supabase/auth`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Password limit contract & auth-client seams

#### Automated

- [ ] 1.1 Solution builds: `dotnet build`
- [ ] 1.2 Test suite passes: `dotnet test`
- [ ] 1.3 A test pins `PasswordLimits.MaxBytes` to GoTrue's documented bcrypt bound
- [ ] 1.4 A test proves the rule measures UTF-8 bytes, not characters
- [ ] 1.5 `PasswordValidator` tests cover too-short, too-many-bytes, empty, and valid
- [ ] 1.6 `SupabaseAuthClientTests` covers `same_password` → `AuthFailureReason.SamePassword`
- [ ] 1.7 `SupabaseAuthClientTests` covers the token-grant returning an `AccessToken`, and signup tolerating none

#### Manual

- [ ] 1.8 Registering with a valid password still works end-to-end
- [ ] 1.9 Registering with a 100-character password shows a Polish validation message on the form

### Phase 2: Profile page & display name

#### Automated

- [ ] 2.1 Solution builds: `dotnet build`
- [ ] 2.2 Test suite passes: `dotnet test`
- [ ] 2.3 `DataAccessBoundaryTests` still passes
- [ ] 2.4 `DisplayNameValidator` tests cover trimming, whitespace/empty → null, the 200-char boundary, an emoji
- [ ] 2.5 `ProfileService` tests cover read, set, clear, and cross-account isolation

#### Manual

- [ ] 2.6 `/profile` renders for a signed-in user and shows the correct account email
- [ ] 2.7 Setting a display name persists it and the nav shows it after the redirect
- [ ] 2.8 Clearing the name reverts the nav to the email
- [ ] 2.9 A 200-character name is accepted; a longer one is refused in Polish
- [ ] 2.10 Signing out and back in still shows the display name
- [ ] 2.11 `/profile` redirects an anonymous visitor to `/login`

### Phase 3: Change password

#### Automated

- [ ] 3.1 Solution builds: `dotnet build`
- [ ] 3.2 Test suite passes: `dotnet test`
- [ ] 3.3 A test proves the change hits `token?grant_type=password` then `user`, with the project key on both
- [ ] 3.4 A test proves the `PUT user` request carries the bearer token from the re-authentication
- [ ] 3.5 A test proves a failed re-authentication returns `InvalidCredentials` and issues no `PUT`
- [ ] 3.6 A test proves the request body carries only the new password

#### Manual

- [ ] 3.7 Changing the password succeeds and the new password works on the next sign-in
- [ ] 3.8 A wrong current password shows the Polish message and the password is unchanged
- [ ] 3.9 Reusing the current password shows the `SamePassword` copy
- [ ] 3.10 A too-short password and an over-byte-bound password are both refused in Polish
- [ ] 3.11 The display-name form still works while the password form shows an error
- [ ] 3.12 The user remains signed in after a successful change (accepted limitation)

### Phase 4: Delete account

#### Automated

- [ ] 4.1 Solution builds: `dotnet build`
- [ ] 4.2 Test suite passes: `dotnet test`
- [ ] 4.3 `DataAccessBoundaryTests` still passes
- [ ] 4.4 A test proves the emitted `DELETE` targets `auth.users` with the current user's id as a parameter
- [ ] 4.5 A test proves the service refuses to execute when no user is signed in
- [ ] 4.6 A test proves the signature exposes no way to supply a different user's id

#### Manual

- [ ] 4.7 Deleting with the correct password signs the user out and lands on `/login`
- [ ] 4.8 Zero rows remain for that owner across all five tables and `auth.users`
- [ ] 4.9 A second account's data is completely untouched
- [ ] 4.10 A wrong password shows the Polish message and the account still exists
- [ ] 4.11 Signing in with the deleted account's credentials fails
- [ ] 4.12 A pre-existing tab with the deleted account's cookie no longer shows an authenticated shell
- [ ] 4.13 The Supabase security advisor reports nothing new
