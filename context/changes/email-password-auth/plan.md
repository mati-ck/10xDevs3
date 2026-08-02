# F-02 Email + Password Authentication Implementation Plan

## Overview

Give 10xNotes real accounts: registration, login, and logout backed by Supabase Auth (GoTrue), with an ASP.NET cookie carrying the session, every route protected by default, and the two identity defects F-01 knowingly left behind repaired. This closes FR-001, FR-002 and the PRD's Access Control section, and unblocks S-01 (the north star), where "save = accept" must bind a note to an account.

## Current State Analysis

F-01 shipped the persistence layer and deliberately stopped short of identity.

- **No authentication anywhere.** `Program.cs` has no `AddAuthentication`, no `UseAuthentication`/`UseAuthorization`. `Components/Routes.razor:1-7` has a bare `Router` with no `AuthorizeRouteView` and no `CascadingAuthenticationState`. `Components/Layout/NavMenu.razor` has no auth-aware links.
- **The identity source is knowingly broken.** `Data/HttpContextCurrentUserAccessor.cs` reads claims from `IHttpContextAccessor`, which is invalid across a Blazor Server circuit — after F-02 it would return `null` during interactive updates, making `CurrentUserId` = `Guid.Empty` and every owner-scoped query return zero rows. Recorded as the HIGH finding in F-01's review.
- **DbContext is circuit-scoped.** `AddDbContext<AppDbContext>` plus `CurrentUserId` captured in the constructor means a context created early in a circuit keeps filtering by a stale identity after login or logout.
- **The isolation contract is ready and waiting.** `Data/IOwnedByUser.cs`, the convention-applied global query filter and `SaveChanges` stamping in `Data/AppDbContext.cs`, and `public.profiles` with a unique `owner_id` FK to `auth.users` all exist and are tested (6 passing tests in `tests/10xNotes.Tests/`).
- **Supabase Auth is provisioned but unused.** The GoTrue `auth` schema exists with **0 users**; `disable_signup: false`; **`mailer_autoconfirm: false`** — every signup currently requires an email confirmation.
- **DataProtection keys are not persisted.** Recorded as a follow-up in `context/changes/deployment/deployment-plan.md` — harmless for a stateless app, fatal for cookie auth.
- **Render mode is opt-in per component.** `Components/Pages/Counter.razor:2` carries `@rendermode InteractiveServer`; `Home.razor` has none. New pages are static SSR unless they ask otherwise.

## Desired End State

A visitor can register with email and password, log in, and log out. Logged out, they can reach only the login and register pages — every other route redirects to login, and no note or source material is reachable. Logged in, the app knows who they are on both the server-rendered and interactive paths, and the data layer scopes every query to that user without any call site asking. Sessions survive a deploy.

Verify by:
1. `dotnet build` and `dotnet test` pass.
2. Register a new account → it appears in `auth.users` and a matching `public.profiles` row exists.
3. Log out, request `/` → redirected to `/login`. Request `/health` → still the literal body `Healthy`, no redirect.
4. Log in → the app shows the signed-in email; a query through `AppDbContext` returns that user's rows and no one else's.
5. Redeploy the container → the session cookie still authenticates (DataProtection keys survived).

### Key Discoveries:

- **A fallback authorization policy applies to every endpoint, including health checks and static assets.** If `/health` starts answering `302 → /login`, the Docker `HEALTHCHECK` in `Dockerfile:26` stops seeing `Healthy`, Coolify marks the container unhealthy and de-routes it — the exact outage recorded in `deployment-plan.md`. The allow-list is load-bearing.
- **`ICurrentUserAccessor` must become asynchronous.** `AuthenticationStateProvider.GetAuthenticationStateAsync()` is async while F-01's `UserId` is a sync property. This is a breaking interface change that also touches `tests/10xNotes.Tests/StubCurrentUserAccessor.cs`.
- **EF evaluates the query filter's `CurrentUserId` per query, not at model build.** Verified in F-01: referencing a context member lets EF parameterize it. So `CurrentUserId` can become settable and be assigned when a context is created, which is what makes the factory approach work.
- **`SignInAsync` needs a response that hasn't started.** Interactive Blazor Server components cannot sign in or out; the auth pages must be static SSR and post to server-side handling. Since interactivity here is opt-in per component, new pages are static SSR by default.
- **The trigger can attach to `auth.users`, but the function cannot live there.** Verified on the live project: `has_table_privilege('postgres','auth.users','TRIGGER')` is `true`, while `has_schema_privilege('postgres','auth','CREATE')` is `false`. So a `SECURITY DEFINER` function in `public` plus a trigger on `auth.users` — Supabase's canonical `handle_new_user` shape.
- **DataProtection's key table needs RLS.** `PersistKeysToDbContext` adds a table to `public`, and per `context/foundation/lessons.md` every such table gets `ENABLE ROW LEVEL SECURITY` in the same migration. Package `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` **10.0.10** matches our EF version exactly.

## What We're NOT Doing

- **No roles or permissions** — the PRD specifies a flat user model.
- **No password reset, email change, or account deletion** — not in FR-001/FR-002; add when a real user needs one.
- **No email confirmation flow** — auto-confirm is being turned on instead (see Migration Notes).
- **No OAuth or social login** — PRD non-goal.
- **No Supabase session persistence** — the GoTrue access and refresh tokens are discarded after sign-in; our cookie is the session.
- **No domain entities or note UI** — S-01.
- **No RLS policies** — deny-all stays; access control remains the application-level query filter.
- **No end-to-end browser tests** — unit tests at the seams plus manual verification.
- **Not removing `Counter.razor` / `Weather.razor`** — `/counter` is still the interactive smoke test the deploy runbook relies on.

## Implementation Approach

Four phases, ordered so the app is never half-protected. Phase 1 builds the machinery with no visible change. Phase 2 adds the flows, still on a public app. Phase 3 flips protection on, at which point login already exists. Phase 4 verifies.

Supabase Auth is used strictly as a credential store: we call GoTrue to create and verify credentials, then mint our own cookie containing the user id and email, and forget the Supabase tokens. That keeps one session concept in the app and means `OwnerId` — already `auth.users.id` from F-01 — needs no translation.

## Critical Implementation Details

**Timing & lifecycle — the allow-list must land in the same commit as the fallback policy.** The moment `SetFallbackPolicy` is registered, `/health`, `/health/ready`, static assets, and the auth pages must already carry `AllowAnonymous`. A deploy where `/health` redirects will de-route the container, and because the app then serves a parked page, the failure looks unrelated to auth.

**State sequencing — sign-in must complete before any redirect.** `HttpContext.SignInAsync` writes the cookie to the response; issuing a redirect first, or attempting either from an interactive component, silently produces an authenticated-looking flow with no cookie.

**Debug & observability — verify identity end to end, not just at the form.** After login, confirm the *data layer* sees the user (a query returns that user's rows), not merely that the UI shows an email. F-01's HIGH finding is precisely a case where the UI looks signed in while every query returns nothing.

---

## Phase 1: Auth foundation

### Overview

Persist DataProtection keys, register the cookie scheme, and repair the identity plumbing — with no user-visible change and the app still fully public.

### Changes Required:

#### 1. DataProtection key storage

**File**: `10xnotes.csproj`, `Data/AppDbContext.cs`, `Program.cs`

**Intent**: Keep auth cookies and antiforgery tokens valid across deploys by storing the key ring in the Postgres we already have, instead of the container's ephemeral filesystem.

**Contract**: Add `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.10. `AppDbContext` additionally implements `IDataProtectionKeyContext` with `DbSet<DataProtectionKey> DataProtectionKeys`. `Program.cs` calls `AddDataProtection().PersistKeysToDbContext<AppDbContext>()` and sets a stable application name so the key ring is found after a rename.

#### 2. Key table migration

**File**: `Migrations/<timestamp>_AddDataProtectionKeys.cs` (new)

**Intent**: Create the key table and close it to the Data API in the same migration, per the recorded lesson.

**Contract**: Generated by `dotnet ef migrations add AddDataProtectionKeys`, then hand-extended with `ALTER TABLE public."DataProtectionKeys" ENABLE ROW LEVEL SECURITY;` (no policies — the app's `postgres` role bypasses RLS). Confirm the exact table name from the generated migration before writing the SQL.

#### 3. Cookie authentication scheme

**File**: `Program.cs`

**Intent**: Establish the session mechanism the auth pages will use, without protecting anything yet.

**Contract**: `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(...)` configured with `LoginPath = "/login"`, `LogoutPath = "/logout"`, `ExpireTimeSpan = TimeSpan.FromDays(14)`, `SlidingExpiration = true`, `Cookie.HttpOnly = true`, `Cookie.SameSite = SameSiteMode.Lax`, and `Cookie.SecurePolicy = CookieSecurePolicy.Always`. Add `AddAuthorization()`, `AddCascadingAuthenticationState()`, and `app.UseAuthentication()` / `app.UseAuthorization()` before `UseAntiforgery()`.

Note the app sits behind Coolify's TLS-terminating proxy: with `SecurePolicy.Always` the cookie is only sent over https, which is correct in production and works locally over the https profile.

#### 4. Current-user accessor rework

**File**: `Data/ICurrentUserAccessor.cs`, `Data/HttpContextCurrentUserAccessor.cs` (deleted), `Data/AuthenticationStateCurrentUserAccessor.cs` (new)

**Intent**: Replace the circuit-invalid `IHttpContextAccessor` source with `AuthenticationStateProvider`, which is valid on both the server-rendered and interactive paths — the HIGH finding from F-01's review.

**Contract**: `ICurrentUserAccessor` becomes `ValueTask<Guid?> GetUserIdAsync(CancellationToken ct = default)`. The implementation resolves `AuthenticationStateProvider.GetAuthenticationStateAsync()`, reads `ClaimTypes.NameIdentifier`, and parses it as a `Guid`, returning `null` when unauthenticated or unparseable. `IHttpContextAccessor` registration is removed if nothing else uses it.

#### 5. Per-operation DbContext creation

**File**: `Data/AppDbContext.cs`, `Data/UserScopedDbContextFactory.cs` (new), `Program.cs`

**Intent**: Stop freezing identity for the lifetime of a circuit; create a context per operation with the *current* user applied.

**Contract**: `AppDbContext.CurrentUserId` becomes settable, and the constructor drops its `ICurrentUserAccessor` dependency. `UserScopedDbContextFactory` wraps `IDbContextFactory<AppDbContext>`, exposing `Task<AppDbContext> CreateAsync(CancellationToken ct = default)` which resolves the current user and assigns `CurrentUserId` before returning. `Program.cs` swaps `AddDbContext` for `AddDbContextFactory<AppDbContext>(..., lifetime: ServiceLifetime.Scoped)` — scoped so the scoped accessor can participate — and registers the wrapper.

The existing health check must follow: `AddDbContextCheck<AppDbContext>` requires a resolvable `AppDbContext`, so either keep a scoped `AddDbContext` registration alongside the factory or replace that check with one that creates a context from the factory. Whichever is chosen, `/health/ready` must still report database reachability.

**Contract note for the query filter**: `Data/AppDbContext.cs` keeps `HasQueryFilter(e => e.OwnerId == CurrentUserId)` unchanged — EF re-reads the property per query, which is exactly why a settable property is safe here.

### Success Criteria:

#### Automated Verification:

- Build is clean: `dotnet build` → 0 warnings, 0 errors
- Existing tests still pass after the interface change: `dotnet test`
- Migration applies: `dotnet ef database update`
- App starts; `/health` → `Healthy` and `/health/ready` → `Healthy`

#### Manual Verification:

- Supabase MCP shows the DataProtection key table with `rls_enabled: true`
- Startup no longer logs the "keys are not persisted" DataProtection warning
- The app's existing pages still render and `/counter` still increments

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation before proceeding.

---

## Phase 2: Supabase Auth integration

### Overview

Add the GoTrue client and the three flows — register, login, logout — plus automatic profile provisioning. The app is still public; these pages simply exist.

### Changes Required:

#### 1. GoTrue client

**File**: `Auth/SupabaseAuthClient.cs`, `Auth/SupabaseAuthOptions.cs` (new), `Program.cs`, `appsettings.json`

**Intent**: Talk to Supabase Auth over its REST API for exactly two operations — create a credential, verify a credential — without taking on an SDK whose session model we don't use.

**Contract**: A typed client registered via `IHttpClientFactory` with `Task<AuthResult> SignUpAsync(string email, string password, CancellationToken ct)` and `Task<AuthResult> SignInAsync(...)`, calling `POST /auth/v1/signup` and `POST /auth/v1/token?grant_type=password` with the `apikey` header. `AuthResult` exposes the authenticated user's id and email on success, and a **classified failure reason** (invalid credentials / already registered / weak password / unavailable) on failure — never raw GoTrue text, which is English and would leak backend detail into a Polish UI.

Configuration comes from `Supabase:Url` and `Supabase:AnonKey` in `appsettings.json`. The anon key is publishable by design (it already ships in client apps), so it may be committed; the connection string rule is unchanged.

#### 2. Sign-in / sign-out handling

**File**: `Auth/AuthenticationHandlers.cs` or equivalent (new), `Program.cs`

**Intent**: Convert a successful GoTrue result into an ASP.NET cookie, and clear it on logout — the one place `HttpContext` is still writable.

**Contract**: Server-side handling that builds a `ClaimsPrincipal` with `ClaimTypes.NameIdentifier` = `auth.users.id` and `ClaimTypes.Email`, calls `HttpContext.SignInAsync` with `IsPersistent = true`, then redirects to the return URL (validated as a local URL) or `/`. Logout calls `SignOutAsync` and redirects to `/login`. Both must be reachable without authentication.

#### 3. Register and login pages

**File**: `Components/Pages/Account/Register.razor`, `Components/Pages/Account/Login.razor` (new)

**Intent**: The two forms, in Polish, rendered statically so the cookie can be written.

**Contract**: Routable at `/register` and `/login`. Static SSR — **do not** add `@rendermode`. Each uses `<EditForm Model="..." method="post" OnValidSubmit="...">` with `[SupplyParameterFromForm]`, which wires antiforgery automatically. Register validates email format and a **minimum password length of 8**; login validates presence only.

**Error copy is generic by decision** — no message distinguishes "wrong password" from "no such account", and registration must not confirm whether an address already exists. To stay usable within that constraint, the registration failure message should point at recovery without asserting existence, e.g. *"Nie udało się utworzyć konta. Jeśli masz już konto, zaloguj się."* All copy is Polish, per the PRD.

#### 4. Profile provisioning trigger

**File**: `Migrations/<timestamp>_AddHandleNewUserTrigger.cs` (new)

**Intent**: Guarantee that every account has a `profiles` row, whatever creates the account — including the Supabase dashboard.

**Contract**: Hand-written SQL in the migration. The function must live in `public` (verified: `postgres` cannot create in the `auth` schema) and be `SECURITY DEFINER` so it can insert past RLS; the trigger attaches to `auth.users` (verified: `postgres` holds `TRIGGER` on it):

```sql
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  INSERT INTO public.profiles (owner_id) VALUES (NEW.id)
  ON CONFLICT (owner_id) DO NOTHING;
  RETURN NEW;
END;
$$;

CREATE TRIGGER on_auth_user_created
  AFTER INSERT ON auth.users
  FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();
```

`Down` drops the trigger then the function. `SET search_path` is not optional on a `SECURITY DEFINER` function — without it the function is vulnerable to search-path manipulation. `ON CONFLICT DO NOTHING` keeps the trigger idempotent against the unique `owner_id` index.

### Success Criteria:

#### Automated Verification:

- Build is clean: `dotnet build` → 0 warnings, 0 errors
- Tests pass: `dotnet test`
- Migration applies: `dotnet ef database update`
- `/register` and `/login` return 200 while logged out

#### Manual Verification:

- Registering a new account creates a row in `auth.users` **and** a matching `public.profiles` row (verified via MCP)
- Logging in sets an auth cookie and lands on `/`
- Logout clears the cookie and returns to `/login`
- A duplicate registration and a wrong password both produce Polish, non-revealing messages
- A password shorter than 8 characters is rejected before any GoTrue call

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation before proceeding.

---

## Phase 3: Route protection and auth-aware UI

### Overview

Turn on deny-by-default protection — the change that actually enforces the PRD privacy guardrail — and reflect auth state in the shell.

### Changes Required:

#### 1. Fallback authorization policy and allow-list

**File**: `Program.cs`

**Intent**: Make every route require authentication unless explicitly opened, so a page added by a future slice is protected without anyone remembering to mark it.

**Contract**: Register a fallback policy requiring an authenticated user. Then explicitly allow anonymous access to: both health endpoints, `MapStaticAssets()`, and the auth pages/handlers.

The health endpoints are the dangerous case — `app.MapHealthChecks("/health", ...).AllowAnonymous()` and the same for `/health/ready`. Without this, the Docker `HEALTHCHECK` receives a redirect instead of `Healthy` and Coolify de-routes the container.

Static assets must also be anonymous or the login page renders unstyled. Verify the Blazor `/_blazor` endpoint is reachable for any anonymous interactive page.

#### 2. Router authorization

**File**: `Components/Routes.razor`

**Intent**: Make the Blazor router honour authorization and send unauthenticated users to login rather than rendering an empty page.

**Contract**: Replace `RouteView` with `AuthorizeRouteView` (`DefaultLayout` unchanged), supplying a `NotAuthorized` fragment that redirects to `/login` with a return URL. `CascadingAuthenticationState` comes from the `AddCascadingAuthenticationState()` service registered in Phase 1, so no wrapper component is needed.

#### 3. Auth-aware navigation

**File**: `Components/Layout/NavMenu.razor`

**Intent**: Show the signed-in user and a way out; hide app navigation from anonymous visitors.

**Contract**: Wrap the existing nav items in `<AuthorizeView>`: `Authorized` shows the current links plus the user's email and a logout control (a form posting to the logout handler, since sign-out needs a real request); `NotAuthorized` shows links to login and register. Polish copy.

### Success Criteria:

#### Automated Verification:

- Build is clean: `dotnet build` → 0 warnings, 0 errors
- Tests pass: `dotnet test`
- Anonymous `GET /` → 302 to `/login`
- Anonymous `GET /health` → 200 with body exactly `Healthy` (no redirect)
- Anonymous `GET /health/ready` → 200 `Healthy`
- Anonymous `GET /login` → 200

#### Manual Verification:

- The login page renders with styling while logged out (static assets are anonymous)
- After login, the nav shows the signed-in email and app links; after logout it shows login/register
- A logged-in user's `AppDbContext` query returns their rows — verified against the data layer, not just the UI
- `/counter` still increments after login (the circuit still works with auth in the pipeline)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation before proceeding.

---

## Phase 4: Tests and deploy verification

### Overview

Cover the seams that break silently, then confirm the whole thing in production.

### Changes Required:

#### 1. GoTrue client tests

**File**: `tests/10xNotes.Tests/SupabaseAuthClientTests.cs` (new)

**Intent**: Pin the request shape and the failure classification without touching the network.

**Contract**: A stubbed `HttpMessageHandler` asserts the signup and token requests hit the right paths and carry the `apikey` header, and that each GoTrue error response maps to the intended classified reason — including that no raw GoTrue text reaches the caller.

#### 2. Identity and scoping tests

**File**: `tests/10xNotes.Tests/OwnerScopingTests.cs` (updated), `tests/10xNotes.Tests/StubCurrentUserAccessor.cs` (updated), `tests/10xNotes.Tests/CurrentUserAccessorTests.cs` (new)

**Intent**: Keep F-01's isolation guarantee executable across the interface change, and prove claims actually reach the data layer — the exact path F-01's review flagged as the HIGH risk.

**Contract**: The stub implements the new async `ICurrentUserAccessor`; existing scoping tests are updated to construct contexts with an explicitly assigned `CurrentUserId` and must continue to pass unchanged in intent. New tests assert that a `ClaimsPrincipal` carrying `NameIdentifier` yields that `Guid`, that an unauthenticated principal yields `null`, and that a context created through `UserScopedDbContextFactory` filters by the authenticated user.

#### 3. Deploy

**File**: no repo change — merge to `main` triggers `.github/workflows/deploy.yml`

**Intent**: Ship it and confirm the endpoints Coolify depends on are unaffected.

**Contract**: The workflow is unmodified. Its `/health` assertion is the canary for the allow-list: if the fallback policy leaked onto health checks, this run fails rather than silently de-routing the app.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- Tests pass: `dotnet test` → all green
- No vulnerable packages: `dotnet list 10xnotes.sln package --vulnerable --include-transitive`
- Container build succeeds: `docker build -t 10xnotes:local .`
- Deploy workflow run is green
- Public `/health` → `Healthy`; public `/health/ready` → `Healthy`

#### Manual Verification:

- Register, log in, and log out against the deployed app
- A second registered account cannot see the first account's profile row
- Redeploy the container, then reload with the same browser session — still logged in (DataProtection keys persisted)
- Supabase security advisor reports no `rls_disabled` findings
- `ConnectionStrings__Postgres` and the Supabase config are set in Coolify

**Implementation Note**: This is the final phase; after manual confirmation the change is ready for `/10x-archive`.

---

## Testing Strategy

### Unit Tests:

- GoTrue client: request shape, `apikey` header, and error-to-reason classification
- Claims mapping: `NameIdentifier` → `Guid`; unauthenticated → `null`; unparseable → `null`
- Owner scoping (carried over from F-01, updated for the async interface)
- Context creation through `UserScopedDbContextFactory` filters by the authenticated user

### Integration Tests:

None automated, by decision. The form-post-to-cookie round trip, redirects, and antiforgery are verified manually in Phases 2–4.

### Manual Testing Steps:

1. Register a new account; confirm rows in `auth.users` and `public.profiles` via MCP.
2. Log out; request `/` and confirm the redirect to `/login`; request `/health` and confirm the body is exactly `Healthy`.
3. Log in; confirm the nav shows the email and that a data-layer query returns that user's rows.
4. Submit a wrong password and a duplicate registration; confirm both messages are Polish and reveal nothing.
5. Redeploy; confirm the existing session still authenticates.

## Performance Considerations

Two GoTrue calls per registration and one per login — negligible at MVP scale, but both are outbound network calls in the request path, so failures must classify as "unavailable" rather than hang. Cookie authentication adds no per-request database work; the DataProtection key ring is read once and cached. `AddDbContextFactory` creates a context per operation rather than per circuit, which is cheap and avoids the long-lived-context memory growth the scoped registration risked.

## Migration Notes

**Prerequisite — turn auto-confirm on in Supabase before Phase 2 is verified.** GoTrue currently reports `mailer_autoconfirm: false`, so registrations would sit unconfirmed and unusable. Enable auto-confirm in the dashboard's Auth settings. This is a deliberate MVP trade: nobody's address is verified, so revisit before real users and before adding password reset.

Two migrations land here (DataProtection keys, `handle_new_user` trigger), both additive and backward-compatible — required, since a Coolify rollback restores the previous image but leaves the newer schema. The trigger is idempotent (`ON CONFLICT DO NOTHING`), so it is safe against accounts that somehow already have a profile.

Existing data: `auth.users` has 0 rows and `profiles` has 0 rows, so there is no backfill.

## References

- Roadmap item: `context/foundation/roadmap.md` → F-02 "Uwierzytelnianie e-mail + hasło"
- GitHub issue: [#7](https://github.com/mati-ck/10xDevs3/issues/7)
- Change identity: `context/changes/email-password-auth/change.md`
- Brief: `context/changes/email-password-auth/plan-brief.md`
- Predecessor: `context/changes/persistence-baseline/plan.md` (F-01) — the isolation contract this consumes
- Recurring rules: `context/foundation/lessons.md` (RLS on every new `public` table)
- Deployment constraints: `context/changes/deployment/deployment-plan.md`, `context/foundation/infrastructure.md:72`
- Current wiring: `Program.cs`, `Components/Routes.razor:1-7`, `Data/AppDbContext.cs`, `Data/HttpContextCurrentUserAccessor.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Auth foundation

#### Automated

- [x] 1.1 Build is clean: `dotnet build` → 0 warnings, 0 errors — 85e9799
- [x] 1.2 Existing tests still pass after the interface change: `dotnet test` — 85e9799
- [x] 1.3 Migration applies: `dotnet ef database update` — 85e9799
- [x] 1.4 App starts; `/health` → `Healthy` and `/health/ready` → `Healthy` — 85e9799

#### Manual

- [x] 1.5 DataProtection key table shows `rls_enabled: true` — 85e9799
- [x] 1.6 Startup no longer logs the DataProtection "keys are not persisted" warning — 85e9799
- [x] 1.7 Existing pages render and `/counter` still increments — 85e9799

### Phase 2: Supabase Auth integration

#### Automated

- [x] 2.1 Build is clean: `dotnet build` → 0 warnings, 0 errors — 6bc6c68
- [x] 2.2 Tests pass: `dotnet test` — 6bc6c68
- [x] 2.3 Migration applies: `dotnet ef database update` — 6bc6c68
- [x] 2.4 `/register` and `/login` return 200 while logged out — 6bc6c68

#### Manual

- [x] 2.5 Registration creates rows in `auth.users` and `public.profiles` — 6bc6c68
- [x] 2.6 Login sets an auth cookie and lands on `/` — 6bc6c68
- [x] 2.7 Logout clears the cookie and returns to `/login` — 6bc6c68
- [x] 2.8 Duplicate registration and wrong password give Polish, non-revealing messages — 6bc6c68
- [x] 2.9 Passwords shorter than 8 characters are rejected before any GoTrue call — 6bc6c68

### Phase 3: Route protection and auth-aware UI

#### Automated

- [x] 3.1 Build is clean: `dotnet build` → 0 warnings, 0 errors
- [x] 3.2 Tests pass: `dotnet test`
- [x] 3.3 Anonymous `GET /` → 302 to `/login`
- [x] 3.4 Anonymous `GET /health` → 200 body exactly `Healthy`
- [x] 3.5 Anonymous `GET /health/ready` → 200 `Healthy`
- [x] 3.6 Anonymous `GET /login` → 200

#### Manual

- [x] 3.7 Login page renders with styling while logged out
- [x] 3.8 Nav reflects auth state before and after login
- [x] 3.9 A logged-in user's data-layer query returns their rows
- [x] 3.10 `/counter` still increments after login

### Phase 4: Tests and deploy verification

#### Automated

- [ ] 4.1 Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- [ ] 4.2 Tests pass: `dotnet test` → all green
- [ ] 4.3 No vulnerable packages
- [ ] 4.4 Container build succeeds: `docker build -t 10xnotes:local .`
- [ ] 4.5 Deploy workflow run is green
- [ ] 4.6 Public `/health` → `Healthy`; public `/health/ready` → `Healthy`

#### Manual

- [ ] 4.7 Register, log in, and log out against the deployed app
- [ ] 4.8 A second account cannot see the first account's profile row
- [ ] 4.9 Session survives a redeploy
- [ ] 4.10 Supabase security advisor reports no `rls_disabled` findings
- [ ] 4.11 Supabase config and connection string set in Coolify
