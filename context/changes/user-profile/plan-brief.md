# Profil użytkownika — Plan Brief

> Full plan: `context/changes/user-profile/plan.md`

## What & Why

Add a `/profile` page giving the signed-in user three account capabilities that do not exist anywhere in the product today: set a display name, change their password, and delete their account. The display name lands on a `profiles` table F-01 built and nothing has ever used; the other two each need a path through Supabase Auth that the app deliberately closed when it decided the cookie would be its only session concept.

This change is **outside the PRD and the roadmap** — no roadmap item carries `Change ID: user-profile`, and the PRD does not mention a profile. It is additive to the MVP, not a slice of it.

## Starting Point

`Data/Entities/Profile.cs` exists with a nullable `DisplayName`, a unique `OwnerId` FK to `auth.users`, and a Postgres trigger guaranteeing a row for every account — but no application code reads or writes it; the only references outside migrations are in tests. There is no `/profile` route and no account-settings surface at all: `Components/Pages/Account/` holds only `Login`, `Logout` and `Register`. The nav shows the account email. `SupabaseAuthClient` calls exactly two GoTrue endpoints and discards the access and refresh tokens by design.

## Desired End State

A signed-in user reaches `/profile` from the nav and can set or clear a display name that then replaces the email in the nav; change their password by supplying the current one, with GoTrue's rejections rendered as Polish copy; and delete their account by re-entering their password, landing signed-out on `/login` with no row belonging to them left in any table.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Scope | Display name, password change, account deletion | Usage/account statistics were explicitly cut. |
| Delete mechanism | `DELETE FROM auth.users` on the existing connection | Verified the app's `postgres` role has the privilege, so no `service_role` key is needed and the existing FK cascades do the rest. |
| Password change | Re-authenticate with the current password | Proves the old password and yields a token used and discarded in one method, so the cookie stays the only session concept. |
| Page layout | One `/profile` page, three sections | One route to protect and test; matches how small account-settings surfaces are built. |
| Delete confirmation | Re-enter the password | The only confirmation that defends against a stolen cookie or a walk-up, and it reuses the re-auth call the password change already needs. |
| Display name lookup | Cookie claim, re-issued on edit | No database read on any page render; `AuthCookie.SignInAsync` already owns the claim set. |
| Display name rules | Optional, trimmed, clearable, ≤200 | Matches the nullable column and the trigger-created empty profile with no migration. |
| Password limit | One byte-measured constant, both forms fixed | Applies the `lessons.md` rule; fixing the register form is nearly free once the constant exists. |
| Post-delete | Sign out, silent redirect to `/login` | Leaves no authenticated shell over a user row that no longer exists. |
| Failure posture | Per-form Polish errors | Follows `Login.razor`; a GoTrue outage must not block display-name editing. |
| `note_events` on delete | Keep the cascade | Delete means delete — the ledger is the user's data. |
| Testing | Services, validators, and the limit contract | No bUnit in the project, so logic lives in testable services and pages stay thin. |

## Scope

**In scope:** display name (view, set, clear, nav integration); password change via re-authentication; account deletion with password confirmation and cascade; the shared byte-measured password bound; fixing `Register.razor`'s limit mismatch; the new GoTrue failure codes.

**Out of scope:** usage/account statistics; changing the account email; "log out everywhere" / session revocation; soft delete or undo; preserving `note_events` past deletion; forgot-password for signed-out users; any schema migration; the `service_role` key; avatars or any field beyond `DisplayName`.

## Architecture / Approach

One static-SSR page — forced, not preferred: re-issuing the cookie and `SignOutAsync` both need an `HttpContext` whose response has not started. Three `EditForm`s with distinct `FormName`s and independent error state, following `Login.razor`. Behind them, two new services (`ProfileService`, `AccountDeletionService`) taking `UserScopedDbContextFactory` as `DataAccessBoundaryTests` requires, and two new calls on `SupabaseAuthClient`. The password change is `token?grant_type=password` → `PUT /user` with the resulting bearer token, discarded on return. The deletion is one parameterised statement whose id comes from `AppDbContext.CurrentUserId` and never from the form.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Limit contract & auth seams | Shared password bound, new failure codes, token no longer discarded; register form fixed | Touching a working auth path for a bug no real password has hit yet |
| 2. Profile page & display name | `/profile`, `ProfileService`, name rules, claim, nav fallback | Re-issuing the cookie resets the 30-day session cap as a side effect |
| 3. Change password | Re-auth → `PUT /user` and the second form | Two round trips; a failed re-auth must never reach the `PUT` |
| 4. Delete account | Password-confirmed owner-scoped delete, sign out, redirect | Irreversible, and the cascade is **not** provable by the test suite — manual only |

**Prerequisites:** a Supabase project the developer can query directly (to verify the cascade), and a throwaway account holding at least one material and one saved note.
**Estimated effort:** ~3–4 sessions across four phases; Phase 1 is the smallest, Phase 4 is the shortest to write and the longest to verify.

## Open Risks & Assumptions

- The deletion cascade cannot be automated-tested: it lives in Postgres FKs against `auth.users`, and the SQLite harness has no such table. Phase 4's real proof is manual, against a throwaway account.
- Direct SQL against `auth.users` skips GoTrue's `user_deleted` audit-log entry, and reaches into a schema the app otherwise treats as Supabase's.
- A password change does not invalidate cookies already issued — there is no server-side session store, so `AuthCookie.SessionCap` (30 days) stays the only outer bound.
- Re-issuing the cookie on a display-name edit resets that session cap.
- Deleting an account removes its `note_events`, so the PRD's 75%-acceptance metric is computed only over surviving accounts.
- The 72-byte password cap is GoTrue's current documented behavior; if Supabase changes it, the pinned constant is where that surfaces.

## Success Criteria (Summary)

- A user can set a display name and see it in the nav, and clear it back to their email.
- A user can change their password only by proving the current one, with every rejection readable in Polish.
- A user can delete their account and verifiably nothing of theirs remains — while another account's data is untouched.
