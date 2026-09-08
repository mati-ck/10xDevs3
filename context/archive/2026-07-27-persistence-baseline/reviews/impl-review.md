<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: F-01 Persistence Baseline

- **Plan**: `context/changes/persistence-baseline/plan.md`
- **Scope**: All 4 phases (25/25 Progress items complete) — commit range `f29cd21..0028173`
- **Date**: 2026-08-02
- **Verdict**: NEEDS ATTENTION → after triage: 7 fixed, 2 consciously skipped, 1 closed informational
- **Carried forward to S-01** (not resolved here):
  1. F1 residual gap — attach-by-PK with a forged `OwnerId` still passes the new guard. Needs `OwnerId` as `IsConcurrencyToken()`.
  2. F6 skipped — the new F1/F2 guards have no test coverage, so a refactor can drop them silently.
  3. F3 — the `/health/ready` rollout gate is documented but not configured in Coolify.
- **Findings**: 1 critical, 4 warnings, 5 observations

## Scoping note

This review is **retrospective**. `persistence-baseline` merged on 2026-07-27, and a later change
(`email-password-auth`, now archived) landed on top and modified or replaced several of the same
files. All as-implemented judgments below were made against `git show 0028173:<path>`, not the
working tree; every finding records whether it is **still present at HEAD**. Findings that the
follow-on change already remediated are recorded for the record only (F10).

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

**Why NEEDS ATTENTION and not REJECTED**: the strict rubric maps a CRITICAL Safety & Quality
finding to REJECTED, which is a pre-merge gate. This code is already merged, deployed, and built
upon. F1 is latent — there are currently zero application call sites that write owner-scoped
entities (`grep` over `Components/`, `Data/`, `Auth/` returns none; profiles are created DB-side
by the `handle_new_user` trigger). The actionable framing is: **fix before S-01**, which is the
first slice that will actually write owned entities.

## Automated verification (re-run at HEAD, 2026-08-02)

| Criterion | Command | Result |
|---|---|---|
| 3.1 Solution builds | `dotnet build 10xnotes.sln` | PASS — 0 warnings, 0 errors |
| 3.2 Tests pass | `dotnet test` | PASS — 33 passed, 0 failed |
| 1.1 Tool restore | `dotnet tool restore` | PASS — `dotnet-ef` 10.0.10 restored |

Note: these verify HEAD, which includes the follow-on change. They confirm the persistence layer
still builds and its guarantees still hold, not the exact tree at `0028173`.

## Plan adherence summary

Every planned item was verified present and matching intent. No MISSING items, no DRIFT.

All "What We're NOT Doing" guardrails held: no auth behaviour, no ASP.NET Identity, no domain
entities, no RLS policies, `deploy.yml` unmodified, no `global.json` / `.editorconfig` / lockfile.

Justified additions the plan implied but did not name: `Data/DatabaseMigrationState.cs` and
`Data/DatabaseMigrationHealthCheck.cs` (Phase 2 item 6 required both in prose),
`10xnotes.csproj` `UserSecretsId` + `DefaultItemExcludes` (both forced by planned work), a
`SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 pin closing GHSA-2m69-gcr7-jv3q, and a sixth scoping test
covering the fail-closed read path.

## Findings

### F1 — Ownership contract enforces reads but not writes

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Data/AppDbContext.cs:91-112` (`StampOwners`)
- **Detail**: `StampOwners` inspects only `EntityState.Added`. `Modified` and `Deleted` entries are
  entirely unguarded, and `IOwnedByUser.OwnerId` has a public setter with no
  `IsModified = false` / `IsReadOnlyAfterSave` restriction. Two consequences: (1) a loaded entity
  can have its `OwnerId` reassigned to another user and EF will persist it; (2) more seriously,
  **global query filters do not apply to `SaveChanges` UPDATE/DELETE** — those target the row by
  primary key alone. So `context.Attach(new Profile { Id = <victim id> }).State = Modified` (or
  `Remove`) mutates another user's row without ever passing through the filter. The isolation
  guarantee is read-side only. This is a flaw in the **plan's design**, faithfully implemented —
  the plan's contract only ever specified a query filter plus insert stamping — but it undercuts
  the plan's own stated purpose: "an owner-scoping contract that every later slice (F-02, S-01…)
  inherits for free". S-01's `Note` and `SourceMaterial` would inherit a half-contract.
  Not exploitable today: no application code writes owned entities yet.
- **Fix A ⭐ Recommended**: Extend `StampOwners` to guard Modified/Deleted, and freeze `OwnerId` after insert.
  - Strength: Closes the class of problem at the single choke point every write already passes through; keeps the "one interface, isolation for free" promise intact for S-01.
  - Tradeoff: Needs a decision on the `IgnoreQueryFilters()` escape hatch — a legitimate admin path would now also need to bypass the write guard.
  - Confidence: HIGH — `StampOwners` is already the sole write interceptor; the change is additive and local.
  - Blind spot: Not verified whether `ExecuteUpdate`/`ExecuteDelete` (which bypass the change tracker entirely) are used anywhere later; those would need separate handling.
- **Fix B**: Enforce at the database instead — write real RLS policies keyed to `auth.uid()`.
  - Strength: Enforcement moves below the application, so no ORM path can bypass it; covers `ExecuteUpdate`/`ExecuteDelete` too.
  - Tradeoff: The app connects as `postgres`, which has `rolbypassrls = true`, so this requires re-architecting the connection role — a change the plan explicitly declined ("No RLS policies").
  - Confidence: MEDIUM — correct in principle, but reverses a documented decision and touches deployment.
  - Blind spot: Session-pooler behaviour with per-request `SET LOCAL role` / JWT claims is unverified on this project.
- **Decision**: FIXED via Fix A — guard added for Modified/Deleted in `StampOwners`; `OwnerId` given `PropertySaveBehavior.Throw` after insert. Build clean, 33/33 tests pass, `dotnet ef migrations has-pending-model-changes` reports no model drift (no migration required). **Residual gap**: the guard compares `entry.Entity.OwnerId` to `CurrentUserId`, which for a *detached-then-attached* entity is a caller-supplied value — an attacker who sets `Id` to a victim's row and `OwnerId` to their own still passes the check, because EF's UPDATE targets the row by primary key alone. Closing that needs `OwnerId` marked `IsConcurrencyToken()`, which pushes `owner_id` into the UPDATE/DELETE WHERE clause. Not done here: it changes the model snapshot and was outside the approved fix. Recommended follow-up before S-01.

### F2 — Caller-supplied `OwnerId` is honoured verbatim on insert

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Data/AppDbContext.cs:108`
- **Detail**: `added.Where(entry => entry.Entity.OwnerId == Guid.Empty)` stamps ownership *only*
  when `OwnerId` is still empty. Any code path that binds `OwnerId` from user input (a model-bound
  form or DTO round-trip is the classic vector) inserts a row owned by an arbitrary other user.
  The guard on line 102 rejects the *unauthenticated* case, not the *impersonating* one. With the
  unique index on `owner_id`, this also lets an attacker squat another user's profile slot.
  Still present at HEAD (byte-identical).
- **Fix**: Assign unconditionally for all Added entries — `entry.Entity.OwnerId = CurrentUserId;` — or throw when a non-empty `OwnerId != CurrentUserId` is presented.
- **Decision**: FIXED — `OwnerId` is now assigned unconditionally for every Added entry, so a caller-supplied value is overwritten rather than trusted.

### F3 — Migrations run after Kestrel starts accepting traffic

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Program.cs:86`
- **Detail**: `AddHostedService<DatabaseMigrationHostedService>()` is registered in user code, i.e.
  *after* `GenericWebHostService` (registered inside the `WebApplicationBuilder` constructor).
  Hosted services start in registration order, so Kestrel binds and serves requests **before**
  migrations run. During that window the app answers traffic against a possibly stale schema while
  `/health` returns `Healthy` (correctly — it runs no checks), so Coolify routes to it. Only
  `/health/ready` reports the gap, and nothing automated probes it (README: "Probed by humans").
  The plan's design correctly prioritised never crash-looping, but did not consider the ordering.
  Still present at HEAD.
- **Fix A ⭐ Recommended**: Gate the Coolify rollout on `/health/ready` before shifting traffic.
  - Strength: Keeps the deliberate never-crash-loop property intact — a failed migration still starts and degrades readiness rather than de-routing the container.
  - Tradeoff: Configuration lives in Coolify, outside the repo, so it is invisible to `git` and easy to lose on resource recreation.
  - Confidence: MEDIUM — matches the documented deployment posture, but the exact Coolify readiness-gate setting has not been verified on this resource.
  - Blind spot: Whether Coolify's health-gate supports a distinct readiness path separate from the container `HEALTHCHECK`.
- **Fix B**: Await migrations before the web host starts (run them in `Program.cs` before `app.Run()`, still non-throwing).
  - Strength: No traffic is ever served against a stale schema; entirely in-repo.
  - Tradeoff: Reintroduces exactly the design-time hazard the plan called out — `dotnet ef migrations add` builds the host, so inline migration code risks executing against the live database during scaffolding.
  - Confidence: LOW — the plan documented a concrete reason not to do this.
  - Blind spot: None significant; the plan's reasoning here is sound and this option mostly exists to be rejected explicitly.
- **Decision**: FIXED via Fix A — the startup window and the `/health/ready` rollout gate are documented in `context/changes/deployment/deployment-plan.md` under Endpoints. The Coolify-side gate itself is not yet configured; until it is, verify `/health/ready` by hand after any migration-carrying deploy.

### F4 — Progress item 2.6 marked complete but was superseded during the change

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `context/changes/persistence-baseline/plan.md:435`
- **Detail**: Item 2.6 reads "FK to `auth.users` exists and `__EFMigrationsHistory` holds one row"
  and is checked `- [x] — a087c3d`. A second migration, `20260727173924_HardenMigrationsHistoryRls`,
  was added in the same change, so the ledger holds **two** rows. The criterion as written became
  false and was checked anyway. The underlying work is correct and valuable — it closed a real
  Data API hole on `__EFMigrationsHistory`, and the gap is already recorded as an accepted rule in
  `context/foundation/lessons.md`. The finding is about the verification record, not the code:
  a criterion marked done without being re-read is the signature of rubber-stamping, and it is the
  one Progress entry in this change that does not describe what shipped.
- **Fix**: Amend 2.6 to state the actual end state (two migrations; both `public` tables RLS-enabled), and note the second migration in the Phase 2 "Changes Required" list.
- **Decision**: FIXED — criterion 2.6 reworded to the actual end state, and the second migration added to Phase 2 as change 5b.

### F5 — Every commit in this change contradicts the documented commit convention

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `AGENTS.md` → "Commit & Pull Request Guidelines"
- **Detail**: `AGENTS.md` states commit subjects are "short, imperative, sentence-case … **no
  Conventional Commits prefixes**". All four phase commits use Conventional Commits:
  `feat(persistence-baseline): …`, `test(persistence-baseline): …`, `chore(persistence-baseline): …`,
  as do all nine commits of the follow-on change. The convention in the file is the one nobody
  follows. This matters here specifically because Phase 3's contract was "`AGENTS.md` no longer
  contains a statement contradicted by the repo" (Progress 3.5, checked `[x]`) — that statement
  became contradicted by the very commits carrying the change.
- **Fix**: Update the `AGENTS.md` commit guidance to describe the convention actually in use (`<type>(<change-id>): <subject>`), since the repo has voted with 13 commits.
- **Decision**: FIXED — `AGENTS.md` commit guidance now documents the `<type>(<change-id>): <subject>` convention actually in use, including the phase marker and the rule that non-change commits use a bare type rather than being folded into a phase commit.

### F6 — Tests do not exercise the production wiring

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `tests/10xNotes.Tests/OwnerScopingTests.cs:117-126`
- **Detail**: The fixture constructs `new AppDbContext(options)` directly with a stub accessor, so
  it exercises the query-filter mechanism but none of the DI graph — not the context lifetime, not
  the accessor implementation. This is precisely why the two as-shipped criticals in F10 passed a
  green suite: in the real app at `0028173`, every one of these queries would have returned zero
  rows. The tests are **not** vacuous on their own terms (the `IgnoreQueryFilters` case asserts both
  rows genuinely exist, and the different-user case really does re-use a model first built under
  another user, which is a real regression guard). Separately, schema comes from `EnsureCreated()`
  rather than the migrations, so neither hand-written SQL block — the `auth.users` FK nor either
  `ENABLE ROW LEVEL SECURITY` — is executed by any test.
- **Fix**: Add one test that resolves `AppDbContext` through a `ServiceCollection` configured the way `Program.cs` configures it, plus adversarial cases for F1/F2 (caller-supplied `OwnerId`, attach-by-PK update of another user's row).
- **Decision**: SKIPPED — conscious skip. Note the consequence: the F1 and F2 guards now have no test coverage, so a future refactor can remove them silently.

### F7 — `HardenMigrationsHistoryRls.Down()` re-opens the migration ledger

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Migrations/20260727173924_HardenMigrationsHistoryRls.cs:27-29`
- **Detail**: `Down()` issues `ALTER TABLE public."__EFMigrationsHistory" DISABLE ROW LEVEL SECURITY`,
  re-opening to the public anon key the exact hole `Up()` exists to close. Reverting this migration
  is strictly a security regression, not a neutral rollback. The `Up()` paths of both migrations are
  correctly backward-compatible for a Coolify image rollback (purely additive), so this only bites
  on an explicit `dotnet ef database update <previous>`.
- **Fix**: Make the `Down()` body a no-op with a comment explaining that un-hardening is never the desired rollback.
- **Decision**: FIXED — `Down()` is now a no-op with a comment explaining that un-hardening is never the desired rollback.

### F8 — `Trust Server Certificate=true` is documented as the standard connection string

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `README.md:27`
- **Detail**: The documented string pairs `SSL Mode=Require` with `Trust Server Certificate=true`,
  which encrypts the link but disables server-certificate validation — the connection is
  unauthenticated and MITM-able. This is the string operators are told to paste into user-secrets
  and, by extension, into the Coolify env var, so it is the production posture. No actual credential
  is committed anywhere in the range (verified: `appsettings.json:3` carries an empty placeholder;
  a grep of the full range diff for password/secret/key patterns returns only `<db-password>`-style
  placeholders and prose). Still present at HEAD.
- **Fix**: Switch the documented string to `SSL Mode=VerifyFull` — Npgsql 8+ bundles the roots needed for AWS-hosted Supabase — and re-test the session-pooler connection.
- **Decision**: SKIPPED — `Trust Server Certificate=true` stays for now.

### F9 — Unrelated changes bundled into feature commits

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: commits `a087c3d`, `3c1c7c9`
- **Detail**: Two out-of-band items rode along. (1) `a087c3d` ("first migration and the
  owner-scoping contract") also carried the `<!-- BEGIN/END @przeprogramowani/10x-cli -->` block
  from Lesson 1 to Lesson 2, plus the manifest and six `.claude/skills/**` files — the Phase 3
  contract explicitly said not to touch that block. Mitigating: manifest + skills + block all moved
  together, which is the signature of a `10x-cli get` run, and the hard rule prohibits *hand*-editing.
  (2) `3c1c7c9` ("Update Dockerfile to publish specific project") changed `dotnet publish` →
  `dotnet publish 10xnotes.csproj`. That fix is *necessary* — adding `10xnotes.sln` in Phase 3 makes
  a bare `dotnet publish` ambiguous in `/src` — but it landed as a standalone commit outside any
  phase, with no plan amendment and no Progress entry. Phase 3's contract should have listed
  `Dockerfile` as a touched file.
- **Fix**: Record `Dockerfile` in the Phase 3 file list retrospectively; going forward, run toolchain updates (`10x-cli get`) as their own commit rather than folding them into a phase commit.
- **Decision**: FIXED — `Dockerfile` recorded in the Phase 3 file list as change 5, with the reason. The forward-looking half landed in `AGENTS.md` via the F5 fix.

### F10 — Two criticals shipped and were later remediated by F-02 (for the record)

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Data/AppDbContext.cs:20-24` and `Data/HttpContextCurrentUserAccessor.cs` (both at `0028173`)
- **Detail**: As shipped, this change had two critical defects. (1) `CurrentUserId` was captured in
  the DbContext **constructor** while `AddDbContext` registers scoped — and in Blazor Server the DI
  scope is the SignalR **circuit**, not the HTTP request, so one context instance would have served
  a whole circuit with a frozen identity. (2) `HttpContextCurrentUserAccessor` sourced identity from
  `IHttpContextAccessor`, which is invalid for interactive Blazor Server — `HttpContext` is null for
  the entire circuit lifetime after the initial render. The combined failure mode was fail-**closed**
  (zero rows, inserts throwing) rather than a leak, and nothing populated claims yet, so no data was
  ever exposed. **Both are fixed at HEAD**: `Program.cs:20` now uses `AddDbContextFactory` with
  `UserScopedDbContextFactory`, `CurrentUserId` is `{ get; set; }` assigned per operation, and
  `AuthenticationStateCurrentUserAccessor` replaced the `IHttpContextAccessor` version — its remarks
  name this as "the HIGH finding from the F-01 implementation review". No action needed; recorded so
  the review history is complete, and as the concrete evidence behind F6.
- **Fix**: None — already remediated. Close as informational.
- **Decision**: CLOSED — informational. Already remediated at HEAD by the follow-on change; no action.
