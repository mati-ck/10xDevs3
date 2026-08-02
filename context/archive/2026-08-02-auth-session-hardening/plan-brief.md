# Session and Owner-Contract Hardening — Plan Brief

> Full plan: `context/changes/auth-session-hardening/plan.md`

## What & Why

F-01 and F-02 closed the read path and the login flow, but left the *write* half of the owner-scoping contract and the *lifetime* of a live session unfinished. S-01 is the first slice that will actually write user-owned entities and add data-access call sites, which makes this the last cheap moment to fix both — afterwards every fix means touching code that already exists.

## Starting Point

Identity already flows from the token: `AuthenticationStateCurrentUserAccessor` reads `NameIdentifier` off the principal and `UserScopedDbContextFactory` copies it into a per-operation context. The database is only ever filtered by that id, never asked for it. What is missing sits either side of that: `AppDbContext` can be resolved straight from DI with no user (reads return nothing, writes throw, and it looks like an empty database); `UPDATE`/`DELETE` target rows by primary key alone, so an entity attached with a forged `OwnerId` writes another user's row; the write guards have no test; and a SignalR circuit keeps the principal it was born with forever, because nothing revalidates it.

## Desired End State

Injecting a `DbContext` into application code fails the test suite with a message naming the factory, so the misuse surfaces at `dotnet test` rather than as a debugging session weeks later. Writing another user's row is refused by the database, and the failure says so instead of reporting a phantom concurrency conflict. A circuit can no longer stay authenticated indefinitely — an absolute 30-day cap on each sign-in retires it. Nothing about logging in, using the app or deploying it changes visibly; the cookie keeps its 14-day sliding window untouched.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Session revalidation | Stateless — cap claim + revalidating provider | No tables, no server state, no I/O; the user's constraint was explicit | Plan |
| Session revocation | Not solved, accepted | A stateless session cannot be invalidated early — the same property every stateless JWT has | Plan |
| Sliding expiration | Stays on, untouched | Explicit instruction; cookie behaviour is left exactly as F-02 set it | Plan |
| What the claim means | Absolute cap (30 days), not the cookie's expiry | A sliding cookie's expiry moves and a circuit cannot re-read it — the cap is the only thing a stamped claim can honestly express | Plan |
| Guarding `AppDbContext` | A test over the app assembly | The separate-assembly split was built, verified and backed out on 2026-08-02: it worked, but the tooling friction outweighed the benefit for an MVP with one owned entity | Plan (revised) |
| Ownership on writes | `IsConcurrencyToken()` on `OwnerId`, applied in the convention | Puts `owner_id` in the `WHERE` clause; S-01's entities inherit it without being touched | Plan |
| Ownership failure message | Translate `DbUpdateConcurrencyException` | One message for both guards, so S-01 debugs ownership once rather than chasing concurrency | Plan |
| Email confirmation | Out of scope | Not wanted in the MVP, so "confirmation is unenforceable" is moot rather than a defect | Plan |
| Rate limiting | Out of scope | Real finding, but an HTTP-layer concern — its own change | Plan |

## Scope

**In scope:** a test guarding the data-access boundary · `OwnerId` as a concurrency token plus a translated failure · adversarial tests for both write guards · session-cap claim and revalidating authentication state.

**Out of scope:** splitting the data layer into its own assembly (built, verified, backed out — see Key Decisions) · session revocation · observing the cookie mid-circuit · any change to the cookie's sliding window · email confirmation · rate limiting · any new table or column · repository/store types (they arrive with S-01) · RLS policies · revisiting F-02's decision to discard GoTrue tokens.

## Architecture / Approach

```
Data/                    AppDbContext — query filter + write guards + concurrency token
                         UserScopedDbContextFactory — the only sanctioned way in
Auth/                    AuthCookie — stamps the 30-day session-cap claim
                         SessionCapAuthenticationStateProvider — revalidates it per circuit
tests/                   DataAccessBoundaryTests — fails if anything takes a DbContext
```

Project layout is unchanged. Three small independent phases in ascending order of blast radius: Phase 1 adds a test and touches no production code, Phase 2 changes model metadata and the write path, Phase 3 changes what a sign-in stamps. Any one can be dropped without stranding the others.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Boundary guard | A test that fails if anything in the app assembly takes a `DbContext` | Catches the mistake at `dotnet test`, not at compile time — a service outside the app assembly would slip through |
| 2. Freeze ownership on writes | `owner_id` in the `WHERE` clause, translated failure, five adversarial tests | The migration should carry no DDL; if EF emits any, it must be read before applying |
| 3. Absolute session cap | Cap claim + 5-minute revalidation, stateless | Retiring a circuit has no visible effect — the router renders statically, so nothing in the circuit reacts. The guarantee is data-layer, not UI |
| 4. Build, deploy, verify | Green deploy, accepted risk recorded | Nothing in the build path moved, so this is confirmation rather than proof — `/health` stays the canary |

**Prerequisites:** F-01 (#6) and F-02 (#7), both closed. No Coolify or Supabase configuration change required.
**Estimated effort:** ~1–2 sessions; all three phases are small and independent.

## Open Risks & Assumptions

- **A stateless session cannot be revoked.** Logging out in one tab leaves a circuit in another authenticated until its cap closes. Accepted; the alternative (carry and refresh the GoTrue token so Supabase owns the session) is deferred as its own change.
- **With sliding expiration on, a circuit cannot observe its cookie.** A cookie deleted or expired mid-circuit goes unnoticed until the cap or a disconnect. The two are mutually exclusive and sliding was chosen deliberately; the cap is what keeps circuit life bounded at all.
- **The concurrency-token migration is assumed to be metadata-only.** If EF emits real DDL, Phase 2 gains a review step before it is applied.
- **The boundary guard is a test, not a compile error.** It runs over the app assembly, so a violating type in some future separate assembly would not be seen. Accepted: today there is only one assembly.
- **Passing the cap does not sign the user out visibly.** `App.razor` renders `<Routes />` statically, so `AuthorizeRouteView` and `RedirectToLogin` live outside the circuit and cannot react to a retired principal — measured, not assumed. What the cap does deliver is that the circuit's `ICurrentUserAccessor` returns `null`, so owner-scoped queries return nothing. The UI catches up on the next HTTP request. Making the redirect work needs an interactive router, which would break the static-SSR requirement `HttpContext.SignInAsync` imposes on the auth pages — a separate change.

## Success Criteria (Summary)

- A component that injects a `DbContext` fails `dotnet test`, with a message naming the factory.
- Writing a row owned by another user fails, with a message naming ownership.
- A circuit cannot serve data past its sign-in's absolute cap — the data layer sees nobody, though the UI only catches up on the next HTTP request.
- Everything a user can see — login, logout, navigation, `/counter`, the sliding 14-day session, survival across a redeploy — behaves exactly as before.
