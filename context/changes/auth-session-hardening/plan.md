# Session and Owner-Contract Hardening Implementation Plan

## Overview

Close the write half of the owner-scoping contract, bound a live SignalR circuit with an absolute session cap, and guard the data-access boundary with a test. No new tables, no server-side session state, no change to where identity comes from, and no change to the project layout.

Three of the five items come from the read-only review of the archived F-02 plan (2026-08-02); two are carried forward from the F-01 implementation review. S-01 is the first slice that will actually write user-owned entities, which makes this the last cheap moment to fix all five.

## Current State Analysis

Identity already flows from the token, not the database. `Auth/AuthenticationStateCurrentUserAccessor.cs:20-31` reads `ClaimTypes.NameIdentifier` off the `ClaimsPrincipal` and parses it; `Data/UserScopedDbContextFactory.cs:26-28` copies that `Guid` into a freshly created context. The database is only ever *filtered* by that id. Nothing here needs to change.

What is incomplete:

- **The circuit's principal is fixed for the life of the circuit.** `Program.cs:70` registers `AddCascadingAuthenticationState()` against the default `ServerAuthenticationStateProvider`, which seeds the principal once from the initial HTTP request and never re-checks it. There is no `RevalidatingServerAuthenticationStateProvider` anywhere in the repo. A circuit therefore outlives the cookie that created it — indefinitely, as long as the connection holds.
- **`AppDbContext` is resolvable straight from DI.** `Program.cs:29` registers it scoped, with `CurrentUserId` left at `Guid.Empty`. Three consumers need it — DataProtection's key store, `AddDbContextCheck` at `Program.cs:91`, and `DatabaseMigrationHostedService.cs:33` — but nothing prevents a component injecting it instead of going through the factory. Such a context reads zero rows and throws on write. Fail-closed, but it presents as "the database is empty".
- **`OwnerId` is guarded in the change tracker, not in the SQL.** `Data/AppDbContext.cs:137-141` rejects a tracked entity whose `OwnerId` differs from `CurrentUserId`, but EF's `UPDATE`/`DELETE` still target the row by primary key alone. An entity attached by PK with a forged `OwnerId` passes the check and writes another user's row. This is the residual gap recorded in the F-01 review's F1 decision.
- **The write guards have no test.** `tests/10xNotes.Tests/OwnerScopingTests.cs` covers insert stamping, the unauthenticated refusal, and the read filter — not `Modified`, not `Deleted`, not a caller-supplied `OwnerId`. F-01's review recorded this as F6 and consciously skipped it; a refactor can drop both guards silently.
- **The rule that application code goes through the factory is documentation only.** `Data/UserScopedDbContextFactory.cs:10-11` calls itself "the only sanctioned way", and `Program.cs:26-28` explains why the bare registration exists — but nothing checks either claim. Today no component touches the database at all, so the guard can be added before there is anything to fix.

## Desired End State

A type that takes a `DbContext` instead of going through `UserScopedDbContextFactory` fails the test suite, with a message naming the factory. A tracked or attached entity belonging to another user cannot be updated or deleted, because `owner_id` is part of the `WHERE` clause and a mismatch surfaces as a message naming ownership rather than concurrency. A circuit stops being authenticated once the session's absolute cap passes, instead of running indefinitely.

Verify by:

1. `dotnet build 10xnotes.sln` and `dotnet test` pass; the suite has grown by the boundary guard, the adversarial write cases, and the session-cap cases.
2. Temporarily injecting a `DbContext` into a component makes `dotnet test` fail.
3. The deployed app answers `/health` with the literal `Healthy`.
4. Logging in, using the app, and redeploying behave exactly as before this change.

### Key Discoveries:

- **Within one project, `internal` enforces nothing.** `Components/` and `Data/` compile into the same assembly, so the only compile-time guarantee would come from splitting the data layer into its own project. That was attempted and backed out — see "What We're NOT Doing" — leaving a test as the enforcement mechanism.
- **Reflection sees Blazor's injections.** A `.razor` component's `@inject` compiles to a property carrying `[Inject]`, so a test can walk the web assembly and find every injected member without parsing Razor source. Checking against `DbContext` rather than `AppDbContext` keeps the guard working for any future context type.
- **`ApplyOwnerFilter<T>` is already the single place ownership rules are applied** (`Data/AppDbContext.cs:70-81`) — it sets the query filter and `PropertySaveBehavior.Throw` for every `IOwnedByUser`. The concurrency token belongs in the same three lines, so S-01's entities inherit it without touching them.
- **`RevalidatingServerAuthenticationStateProvider` only sees the principal.** Its `ValidateAuthenticationStateAsync` receives an `AuthenticationState`, not `AuthenticationProperties`, so the ticket's real expiry is unreachable. The expiry has to travel as a claim.
- **SQLite honours concurrency tokens**, so the adversarial write tests run in the existing in-memory fixture with no Postgres and no credentials.

## What We're NOT Doing

- **No session revocation.** A stateless session cannot be invalidated before it expires. Logging out in one tab will not terminate a circuit in another. Accepted deliberately — see "Open Risks" in the brief.
- **No mid-circuit observation of the cookie.** Sliding expiration stays on, and a sliding cookie's real expiry cannot be read from a circuit — the two are mutually exclusive. A cookie deleted or expired while a circuit is open goes unnoticed until the absolute cap or the connection drops.
- **No change to the cookie's 14-day sliding window.** Only the new cap claim is added; `ExpireTimeSpan`, `SlidingExpiration` and every other cookie option stay exactly as F-02 set them.
- **No email confirmation.** `Register.razor` continues to sign the user in directly off the signup response. MVP decision: confirmation is not wanted, so the finding that it is unenforceable is moot.
- **No new tables and no new columns.** Every mechanism here is claims, model metadata, or a test.
- **No rate limiting** on login or registration — a real finding from the F-02 review, but an HTTP-layer concern; it belongs in its own change.
- **No change to where identity comes from** — `ICurrentUserAccessor` keeps reading claims, never the database.
- **No repository or store types yet.** The first store lands with S-01, which is when there is something to query; until then the guard simply asserts nobody reaches past the factory.
- **No project split.** Extracting the data layer into its own assembly was planned, implemented, verified working (`error CS0122` on `@inject AppDbContext`), and then **backed out on 2026-08-02**. It worked, but it cost a second project, a re-added EF tooling package on the startup project, three `<Using>` items replacing the ASP.NET implicit usings, a `Dockerfile` `COPY` line, and a `DefaultItemExcludes` entry — friction disproportionate to an MVP with one owned entity. The lighter guard is a test. Revisit if the data layer grows enough to justify the boundary on its own merits.
- **No RLS policies**, no change to the `postgres` connection role.
- ~~**No `deploy.yml` change.**~~ **Reversed during Phase 1 triage (2026-08-02).** The review found that no workflow ran `dotnet test` at all, so every guard this change adds — the boundary test here, the adversarial write tests in Phase 2, the session-cap tests in Phase 3 — would only ever have fired when somebody chose to run them locally. A test suite that does not gate `main` is advisory, which undercuts the purpose of the whole change. `deploy.yml` gained a `test` job that the `deploy` job now `needs`. The deployment steps themselves are untouched.
- **Not revisiting F-02's decision to discard GoTrue tokens.** Recorded as the alternative that would make revocation possible, deferred as its own change.

## Implementation Approach

Three independent, small phases in ascending order of blast radius. Phase 1 adds a test and touches no production code. Phase 2 changes model metadata and the write path. Phase 3 changes what a sign-in stamps and how a circuit revalidates. None depends on another, so any one can be dropped without stranding the others. Phase 4 confirms the deployed app is unaffected.

## Critical Implementation Details

**State sequencing — the claim is an absolute cap, not a mirror of the cookie's expiry.** `Program.cs:47` keeps `SlidingExpiration = true`, so the cookie's real expiry moves with activity — and a circuit cannot observe that, because it holds a snapshot of the principal taken when it opened and never re-reads the cookie. A claim stamped at sign-in therefore cannot represent "when the cookie expires"; it can only represent "the latest instant this sign-in may still be trusted". The two numbers are deliberately different: the cookie slides on a 14-day window, the claim caps the session at 30 days from sign-in. The cap is what bounds circuit life; the sliding window continues to govern ordinary HTTP requests, unchanged.

The gap this leaves is explicit: a cookie deleted or expired mid-circuit is still not observed by that circuit. Sliding expiration and observing the cookie from a circuit are mutually exclusive, and sliding was chosen.

---

## Phase 1: Guard the data-access boundary with a test

### Overview

Make "application code goes through `UserScopedDbContextFactory`, never a `DbContext`" an executable rule instead of a comment. No production code changes.

### Changes Required:

#### 1. Boundary guard test

**File**: `tests/10xNotes.Tests/DataAccessBoundaryTests.cs` (new)

**Intent**: Fail the build the moment a component or service takes a `DbContext` directly, since such a context has no `CurrentUserId` — it reads zero rows and throws on write, which presents as "the database is empty".

**Contract**: Walk the web assembly (reached via a public type in it, since top-level statements make `Program` internal) and collect every constructor parameter and every property carrying Blazor's `[Inject]` whose type is assignable to `DbContext`. Assert the collection is empty; on failure the message names the offending members and points at `UserScopedDbContextFactory`. Checking against `DbContext` rather than `AppDbContext` keeps the guard valid for any context type added later.

#### 2. Record the rule where agents read it

**File**: `AGENTS.md`

**Intent**: Put the rule next to the other persistence rules, so it is read before the test has to fire.

**Contract**: A bullet under "Data & persistence rules" stating that application code obtains data through `UserScopedDbContextFactory`, that a directly-resolved `AppDbContext` is fail-closed and looks like an empty database, and that `DataAccessBoundaryTests` enforces it.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- Tests pass with the new guard: `dotnet test` → 34 passed
- The guard actually guards: temporarily injecting a `DbContext` into a component makes `dotnet test` fail, and the failure message names `UserScopedDbContextFactory`

#### Manual Verification:

- None. This phase touches no production code and no runtime behaviour.

**Implementation Note**: Automated verification is sufficient for this phase; proceed once it passes.
---

## Phase 2: Freeze ownership on the write path

### Overview

Put `owner_id` into the `WHERE` clause of every `UPDATE` and `DELETE` on an owned entity, translate the resulting failure into a message about ownership, and cover both write guards with the adversarial tests F-01's review identified and skipped.

### Changes Required:

#### 1. Ownership as a concurrency token

**File**: `Data/AppDbContext.cs`

**Intent**: Make the database, not the change tracker, the last line of the ownership check — closing the attach-by-PK path where a caller supplies both the victim's primary key and their own `OwnerId`.

**Contract**: In `ApplyOwnerFilter<TEntity>`, alongside the existing query filter and `PropertySaveBehavior.Throw`, mark `OwnerId` as a concurrency token. Applying it in the convention rather than on `Profile` is what makes S-01's entities inherit it. Effect: EF appends `AND owner_id = @original` to `UPDATE`/`DELETE`, so a forged row targets zero rows and EF raises `DbUpdateConcurrencyException`.

#### 2. Translate the concurrency failure

**File**: `Data/AppDbContext.cs`

**Intent**: Give both write guards one message, so a future implementer debugging S-01 does not chase a phantom concurrency problem.

**Contract**: `SaveChanges` and `SaveChangesAsync` catch `DbUpdateConcurrencyException` and, when every failed entry is an `IOwnedByUser`, rethrow as `InvalidOperationException` with the same wording `StampOwners` already uses for the tracked case, preserving the original as the inner exception. A failure involving any non-owned entity propagates untouched — that one really is concurrency.

#### 3. No migration — verified, not assumed

**File**: none

**Intent**: Establish that the change needs no schema work, rather than carrying an empty migration to look thorough.

**Contract**: ~~Generated by `dotnet ef migrations add FreezeOwnerIdConcurrencyToken`.~~ **Revised during implementation (2026-08-02).** EF reports `No changes have been made to the model since the last migration` *before* any migration is generated, and generating one anyway produces empty `Up()` and `Down()` bodies — a concurrency token is model metadata with no DDL behind it. The migration was generated, read, and removed with `dotnet ef migrations remove`; an empty migration would sit in `__EFMigrationsHistory` forever implying a schema change that never happened. The token is enforced from `OnModelCreating`, not from the snapshot, which the mutation test in change 4 proves. The snapshot picks the annotation up at the next real migration.

#### 4. Adversarial write tests

**File**: `tests/10xNotes.Tests/OwnerScopingTests.cs`

**Intent**: Cover the two guards that shipped in the F-01 remediation with no test at all, plus the gap this phase closes.

**Contract**: Four cases added to the existing SQLite fixture — a caller-supplied `OwnerId` on insert is overwritten with the current user; modifying a tracked row owned by another user throws; deleting one throws; and an entity **attached by primary key** with a forged `OwnerId` throws rather than writing, with a message naming ownership. One positive case guards against over-blocking: updating one's own row still succeeds.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- Tests pass, including the five new cases: `dotnet test` → 39 passed
- No schema work is owed: `dotnet ef migrations has-pending-model-changes` reports none
- The guard is not vacuous: removing `IsConcurrencyToken()` fails exactly the attach-by-PK test and nothing else

#### Manual Verification:

- None. Nothing reaches the database: no migration, no DDL, no new table — so the RLS rule in `context/foundation/lessons.md` and the Supabase advisor have nothing new to look at.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation before proceeding.

---

## Phase 3: Bound the circuit with an absolute session cap

### Overview

Carry an absolute cap on the sign-in as a claim and have the circuit revalidate against it, so an authenticated circuit can no longer run indefinitely. Entirely stateless — no storage, no I/O, no lookup — and the cookie's sliding window is left exactly as it is.

### Changes Required:

#### 1. The two session windows, named in one place

**File**: `Auth/AuthCookie.cs`, `Program.cs`

**Intent**: Make it impossible to read the cookie's window and the circuit's cap as the same number, since they deliberately are not.

**Contract**: `AuthCookie` exposes two constants: the sliding cookie window (14 days, feeding `Program.cs`'s unchanged `ExpireTimeSpan`) and the absolute session cap (30 days, feeding the claim below). `SlidingExpiration` stays `true` — ordinary HTTP behaviour is unchanged by this phase. Both constants carry a comment explaining that the cap is an outer bound on how long one sign-in may be trusted, not a prediction of when the cookie expires; a circuit cannot observe the latter.

#### 2. Session-cap claim at sign-in

**File**: `Auth/AuthCookie.cs`

**Intent**: Put the only thing the revalidation loop can see — the principal — in possession of the cap.

**Contract**: `SignInAsync` adds a claim carrying `sign-in time + absolute cap` as Unix seconds, alongside the existing `NameIdentifier`, `Email` and `Name`. A companion static predicate reports whether a given `ClaimsPrincipal` is past its cap at a supplied instant; taking "now" as a parameter is what makes it testable without an `HttpContext`. A principal with no cap claim is treated as past it — cookies issued before this change carry none, and failing closed retires those sessions once rather than exempting them permanently.

Because the cap (30 days) is longer than the sliding window (14 days), a user who reaches it does so only by holding one circuit open across a month of activity. They are sent to `/login`; their cookie may still be valid, in which case signing in again is a single form submit that issues a fresh cap.

#### 3. Revalidating authentication state

**File**: `Auth/SessionCapAuthenticationStateProvider.cs` (new), `Program.cs`

**Intent**: Make the circuit re-check the cap on a timer instead of trusting the principal it was born with.

**Contract**: A `RevalidatingServerAuthenticationStateProvider` subclass with a revalidation interval of 5 minutes whose `ValidateAuthenticationStateAsync` returns the predicate from change 2. Registered as `AuthenticationStateProvider` so `AuthenticationStateCurrentUserAccessor` and every `AuthorizeView` consume it. It derives from `ServerAuthenticationStateProvider`, so the static-SSR path is unaffected; the revalidation loop only runs for a live circuit. When validation fails the circuit's principal becomes anonymous, `AuthorizeRouteView` falls through to `NotAuthorized`, and the existing `RedirectToLogin` handles the rest — the component whose comment already describes this scenario finally has a way to be reached.

#### 4. Belt-and-braces on the accessor

**File**: `Auth/AuthenticationStateCurrentUserAccessor.cs`

**Intent**: Ensure a principal past its cap yields no user id even on a path that never revalidates.

**Contract**: Before reading `NameIdentifier`, return `null` when the cap predicate says the principal is past it. Keeps the data layer's fail-closed property aligned with the session's.

#### 5. Session tests

**File**: `tests/10xNotes.Tests/SessionCapTests.cs` (new)

**Intent**: Pin the cap semantics without needing an `HttpContext` or a circuit.

**Contract**: The predicate returns valid before the cap instant and past-cap after it; a principal with no cap claim is past it; a principal with an unparseable claim is past it. Plus one case through the existing accessor: a past-cap principal carrying a valid `NameIdentifier` yields `null`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- Tests pass, including the new session cases: `dotnet test` → 49 passed
- Anonymous `GET /` → 302 to `/login`; anonymous `GET /health` → 200 with body exactly `Healthy`
- Revalidation decides correctly: past-cap principal → invalid, live principal → valid, anonymous principal → valid

#### Manual Verification:

- Log in, then confirm the app still shows the signed-in email and `/counter` still increments — the revalidation loop does not disturb a healthy session

**Not verifiable by clicking, and not for the reason the plan first assumed.** The original criterion said a retired circuit "falls back to the login redirect". It does not, and cannot: `Components/App.razor:18` renders `<Routes />` with no `@rendermode`, so the router, `AuthorizeRouteView` and `RedirectToLogin` all render statically per HTTP request rather than inside the circuit. Retiring the circuit's principal has nothing in the circuit to react to it. Measured on 2026-08-02 with a temporary 15-second interval and a forced past-cap principal: the loop ran, reported `pastCap=True`, returned invalid — and the page did not move.

What the cap does deliver is the substance of the finding: after it passes, the circuit's principal is anonymous, so `ICurrentUserAccessor` resolved in that circuit returns `null` and every owner-scoped query returns nothing. A circuit can no longer serve data for as long as its connection happens to survive. The user is not *visibly* signed out until their next HTTP request, where the cookie's own sliding window governs. Accepted.

Making the redirect work would mean an interactive router, which would make `Login`, `Register` and `Logout` interactive too — and `HttpContext.SignInAsync` needs a response that has not started, which F-02 recorded as a discovery before implementation. That is a separate change, not a fix here.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation before proceeding.

---

## Phase 4: Build, deploy and verification

### Overview

Confirm the paths Phase 1 disturbed — container build, EF tooling, and the endpoints Coolify depends on — still work against the deployed app.

### Changes Required:

#### 1. Deploy

**File**: no repo change — merge to `main` triggers `.github/workflows/deploy.yml`

**Intent**: Ship it and confirm nothing in the deploy path moved.

**Contract**: The workflow is unmodified, and so are `Dockerfile` and the project files. Its `/health` assertion remains the canary: a health endpoint that started redirecting fails the run rather than silently de-routing the app.

#### 2. Record the accepted risk

**File**: `context/changes/auth-session-hardening/change.md`

**Intent**: Leave the revocation gap written down as a decision, not as an oversight a later review rediscovers.

**Contract**: A "Risk accepted" note stating that a stateless session cannot be revoked, that logging out in one tab does not end a circuit in another, that the absolute window is 14 days, and that the alternative — carrying and refreshing the GoTrue token so Supabase is the session authority — was deferred as its own change.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- Tests pass: `dotnet test` → all green
- No vulnerable packages: `dotnet list 10xnotes.sln package --vulnerable --include-transitive`
- Container builds: `docker build -t 10xnotes:local .` — note this change touches neither `Dockerfile` nor any project file, so Coolify's own build in the next criterion exercises the same path
- Deploy workflow run is green
- Public `/health` → `Healthy`; public `/health/ready` → `Healthy`

#### Manual Verification:

- Register, log in and log out against the deployed app
- ~~A second account still cannot see the first account's profile row~~ **Revised 2026-08-02.** Not observable in the deployed app: no component reads the database yet, so there is no screen on which one account could see another's profile. The property is data-layer and is covered by 11 tests — F-01's six scoping cases plus Phase 2's five adversarial write cases — which now run in CI on every push. Re-verify through the UI when S-01 adds one.
- Session survives a redeploy — the DataProtection key ring is still found
- Supabase security advisor reports no `rls_disabled` findings

**Implementation Note**: This is the final phase; after manual confirmation the change is ready for `/10x-impl-review`, then `/10x-archive`.

---

## Testing Strategy

### Unit Tests:

- Data-access boundary: no constructor parameter and no `[Inject]` property in the web assembly is a `DbContext`
- Owner scoping, carried unchanged from F-01 — the six existing cases must keep passing untouched
- Write guards: caller-supplied `OwnerId` on insert; modify and delete of another user's tracked row; attach-by-PK with a forged `OwnerId`; a legitimate self-update that must still succeed
- Session cap: before the cap, after it, the cap instant itself, missing claim, unparseable claim, a past-cap principal through `ICurrentUserAccessor`, and the revalidation hook's three outcomes (past-cap, live, anonymous)

### Integration Tests:

None automated, consistent with F-02's decision. The cookie round trip, the revalidation loop and the container build are verified manually in Phases 3–4.

### Manual Testing Steps:

1. Add `@inject AppDbContext Db` to a scratch component and confirm `dotnet test` fails; remove it.
2. Read the Phase 2 migration before applying it; confirm no table creation and no destructive DDL.
3. Log in; confirm the email shows in the nav and `/counter` still increments.
4. Redeploy; confirm an existing session still authenticates.

## Performance Considerations

The revalidation loop is a timer plus claim arithmetic per circuit — no I/O, no allocation of consequence. The concurrency token adds one predicate to the `WHERE` clause of writes that already target a single row by primary key. Cookie handling is untouched, so per-request cost is unchanged.

## Migration Notes

No migration lands here. The concurrency token turned out to need none — verified by generating one, finding both bodies empty, and removing it again. Nothing about the database changes, so the Coolify rollback story is untouched.

Cookie behaviour is unchanged — the 14-day sliding window stays exactly as it is. The one live effect for anyone already signed in is that their cookie carries no cap claim, so the predicate treats it as past its cap and retires that session once on the next circuit. With no real users yet this is theoretical, but it is the reason the missing-claim case fails closed rather than being exempted.

Existing data is untouched — no column is added, moved or dropped.

## References

- Change identity and finding detail: `context/changes/auth-session-hardening/change.md`
- GitHub issue: [#22](https://github.com/mati-ck/10xDevs3/issues/22)
- Source review (read-only, F-02): `context/archive/2026-07-27-email-password-auth/plan.md`
- Carried-forward findings: `context/changes/persistence-baseline/reviews/impl-review.md` (F1 residual, F6)
- Recurring rules: `context/foundation/lessons.md`
- Current wiring: `Program.cs:18-32`, `Data/AppDbContext.cs:70-143`, `Data/UserScopedDbContextFactory.cs`, `Auth/AuthCookie.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Guard the data-access boundary with a test

#### Automated

- [x] 1.1 Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors — 62421b4
- [x] 1.2 Tests pass with the new guard: `dotnet test` → 34 passed — 62421b4
- [x] 1.3 The guard actually guards: injecting a `DbContext` into a component fails `dotnet test` with a message naming `UserScopedDbContextFactory` — 62421b4

### Phase 2: Freeze ownership on the write path

#### Automated

- [x] 2.1 Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors — 3aa4da7
- [x] 2.2 Tests pass, including the five new cases: `dotnet test` → 39 passed — 3aa4da7
- [x] 2.3 No schema work is owed: `has-pending-model-changes` reports none — 3aa4da7
- [x] 2.4 The guard is not vacuous: removing `IsConcurrencyToken()` fails exactly the attach-by-PK test — 3aa4da7

### Phase 3: Bound the circuit with an absolute session cap

#### Automated

- [x] 3.1 Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors — 7669a05
- [x] 3.2 Tests pass, including the new session cases: `dotnet test` → 49 passed — 7669a05
- [x] 3.3 Anonymous `GET /` → 302 to `/login`; anonymous `GET /health` → 200 body exactly `Healthy` — 7669a05
- [x] 3.4 Revalidation decides correctly: past-cap → invalid, live → valid, anonymous → valid — 7669a05

#### Manual

- [x] 3.5 A healthy session is undisturbed: email shows in nav, `/counter` increments — 7669a05

### Phase 4: Build, deploy and verification

#### Automated

- [x] 4.1 Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- [x] 4.2 Tests pass: `dotnet test` → all green
- [x] 4.3 No vulnerable packages
- [x] 4.4 Container builds: `docker build -t 10xnotes:local .`
- [x] 4.5 Deploy workflow run is green
- [x] 4.6 Public `/health` → `Healthy`; public `/health/ready` → `Healthy`

#### Manual

- [x] 4.7 Register, log in and log out against the deployed app — confirmed by the user
- [x] 4.8 Owner isolation holds — covered by 11 tests in CI; not observable in the deployed UI, which reads no data yet
- [x] 4.9 Session survives a redeploy
- [x] 4.10 Supabase security advisor reports no `rls_disabled` findings
- [x] 4.11 Accepted risk recorded in `change.md`
