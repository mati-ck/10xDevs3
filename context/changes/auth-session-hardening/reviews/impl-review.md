<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Session and Owner-Contract Hardening (F-03)

- **Plan**: `context/changes/auth-session-hardening/plan.md`
- **Scope**: All 4 phases (24/24 Progress items complete) — commits `62421b4..9aca39a`
- **Date**: 2026-08-02
- **Verdict**: NEEDS ATTENTION → after triage: 5 fixed, 2 consciously skipped (F1, F4)
- **Findings**: 0 critical, 2 warnings, 5 observations

## Automated verification (re-run at HEAD)

| Criterion | Command | Result |
|---|---|---|
| Build | `dotnet build 10xnotes.sln` | PASS — 0 warnings, 0 errors |
| Tests | `dotnet test` | PASS — 49 passed, 0 failed |
| Model | `dotnet ef migrations has-pending-model-changes` | PASS — no changes |
| Packages | `dotnet list package --vulnerable --include-transitive` | PASS — none in either project |
| Deployed liveness | `GET /health` | PASS — literal `Healthy` |
| Deployed readiness | `GET /health/ready` | PASS — `Healthy` |

## Plan adherence summary

Every planned change is present and matches intent. The three in-flight revisions — the abandoned
project split, the removed empty migration, and the reversed "no `deploy.yml` change" guardrail —
are each struck through in place with a dated reason, in the plan, the brief, and the commit
bodies. Plan and code agree; a later reader will not mistake a decision for drift.

Two guards were mutation-checked rather than merely asserted: removing `IsConcurrencyToken()` fails
exactly the attach-by-PK test, and a component injecting a `DbContext` or an `IDbContextFactory<>`
turns the suite red. That is stronger evidence than a green run.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Findings

### F1 — A genuine concurrency conflict on your own row is reported as an ownership violation

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Data/AppDbContext.cs:120-133` (`IsOwnershipViolation`)
- **Detail**: The XML doc asserts that with one owner per row and ownership frozen after insert,
  "there is no other way for an owned entity to produce" a concurrency conflict. That is not true.
  Any `UPDATE`/`DELETE` matching zero rows raises `DbUpdateConcurrencyException` — including the
  ordinary lost-update case where the row was removed or changed by someone else, or by the *same*
  user in another tab. `IsOwnershipViolation` only checks that the failed entries are
  `IOwnedByUser`, so it swallows that case too and rethrows a message that is factually wrong.

  Verified by probe, not by reasoning: seeding one profile for user A, loading it, deleting it
  through a second context **as the same user**, then saving the edit yields
  `InvalidOperationException: Cannot modify or delete a Profile owned by another user.`

  This is the same class of defect the translation was introduced to remove — a message that sends
  the next implementer down the wrong path. It inverts the intent: Phase 2 replaced a confusing
  concurrency message with a confident ownership message that is sometimes a lie. Not a security
  hole — the write is still correctly refused — but S-01 will meet it the first time two tabs edit
  one note.
- **Fix A ⭐ Recommended**: Reword to cover both causes — "…is owned by another user, or was changed
  or removed by someone else" — and correct the XML doc's claim.
  - Strength: Stops the message asserting something the code cannot know, costs nothing at runtime,
    and keeps the ownership case named first so the common suspicion is still surfaced.
  - Tradeoff: The message is vaguer than Phase 2 promised; a reader still has two hypotheses.
  - Confidence: HIGH — a pure wording change over a branch that is already reached and now proven.
  - Blind spot: None significant.
- **Fix B**: Distinguish the two in the catch — re-read the row by primary key with
  `IgnoreQueryFilters()`; a row that exists under a different owner is an ownership violation,
  anything else is genuine concurrency and rethrows untouched.
  - Strength: Restores exactly the precision Phase 2 claimed; each failure names its real cause.
  - Tradeoff: One extra query on the failure path, and reaching the primary key generically means
    going through `entry.Metadata.FindPrimaryKey()` — more machinery on an error path that has no
    test for the new branch yet.
  - Confidence: MEDIUM — the approach is sound; the generic key handling is the fiddly part.
  - Blind spot: Behaviour when the conflict involves several entries of mixed provenance.
- **Decision**: SKIPPED — conscious skip. Note the consequence: when S-01 adds note editing, an
  ordinary two-tab edit will report "owned by another user", and whoever debugs it will look for a
  permissions bug that does not exist. Revisit then.

### F2 — Past the cap the app looks signed in while every query returns nothing

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architecture
- **Location**: `Data/AuthenticationStateCurrentUserAccessor.cs:26-31`, `Components/App.razor:18`
- **Detail**: The plan documents the mechanism — a retired principal produces no redirect because
  `<Routes />` renders statically — and frames it as correct fail-closed behaviour. What it does not
  evaluate is the resulting user-visible state: the nav still shows the signed-in email, every page
  still renders, and every owner-scoped query returns zero rows. That is indistinguishable from
  data loss, and it is precisely the "the database is empty" symptom Phase 1 exists to prevent —
  now reachable through an ordinary code path rather than a mistake.

  Observed on production during Phase 4: a cookie issued before this change carries no cap claim,
  the accessor therefore returns `null`, and the deployed app still renders as signed in. Today it
  costs nothing because no component reads data. S-01 is the slice where it starts mattering, and
  the plan names S-01 as the reason this change was made now.
- **Fix A ⭐ Recommended**: Sign the user out server-side when a past-cap principal is seen on a
  path that still has `HttpContext`, so the UI catches up on the next request instead of showing an
  authenticated shell over an empty app.
  - Strength: Turns a silent inconsistency into the one thing the user can act on — a login page;
    the static-SSR path has the `HttpContext` that `AuthCookie.SignOutAsync` needs, so no new
    mechanism is required.
  - Tradeoff: Puts a write (cookie deletion) on a read path, and needs care not to fire during the
    sign-in request itself.
  - Confidence: MEDIUM — the pieces exist, but the interaction with the auth pages is unverified.
  - Blind spot: Whether an interactive circuit can reach a path holding a usable `HttpContext`.
- **Fix B**: Accept and record it as a known state, adding the consequence to `change.md` next to
  the mechanism, and re-open it when S-01 adds the first data-reading screen.
  - Strength: Honest and free; the condition needs 30 days of one continuous sign-in to arise
    naturally, so the practical exposure today is one stale cookie.
  - Tradeoff: S-01 inherits a state that reads as data loss, with nothing in the code to warn them.
  - Confidence: HIGH — nothing to build, and the exposure assessment is straightforward.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — `Program.cs` now handles `OnValidatePrincipal`: a past-cap
  principal is rejected and signed out on the first request that carries it. That event fires only
  when an *existing* cookie is validated, never when `SignInAsync` writes one, so a fresh sign-in
  cannot trip over its own cap — which removes the blind spot the option was recorded with.
  Verified in a browser: with the cap forced past, the app redirects to `/login?ReturnUrl=%2F` and
  the nav shows the anonymous links, where the same condition previously rendered an authenticated
  shell over an empty app.

### F3 — The accessor is the only thing making the data layer depend on the auth layer

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: `Data/AuthenticationStateCurrentUserAccessor.cs:2`
- **Detail**: `grep` over both folders: nothing in `Auth/` references `_10xnotes.Data`, and exactly
  one file in `Data/` references `_10xnotes.Auth` — this one, since Phase 3 added the cap check. The
  file is an authentication adapter that happens to live under `Data/`; it depends on Blazor's
  `AuthenticationStateProvider` and now on `AuthCookie`, and implements an interface it does not own.
  The abandoned project split had already identified this and moved it to `Auth/`. Moving it back is
  the one piece of that work worth keeping.
- **Fix**: Move the file to `Auth/` and change its namespace to `_10xnotes.Auth`; the `using` in the
  test that exercises it moves with it.
- **Decision**: FIXED — moved to `Auth/AuthenticationStateCurrentUserAccessor.cs`, namespace
  `_10xnotes.Auth`. `Data/` no longer references `_10xnotes.Auth` at all.

### F4 — The deploy path now depends on an Ubuntu package mirror

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `.github/workflows/deploy.yml` — "Ensure libsqlite3 is loadable"
- **Detail**: The gating `test` job runs `apt-get update` + `apt-get install libsqlite3-dev` on every
  run where the symlink is absent, and `deploy` declares `needs: test`. A transient failure at
  `archive.ubuntu.com` therefore blocks a production deploy for a reason unrelated to the code. The
  guard makes it a no-op once the package is present, but the runner is a container: a recreation
  puts the apt call back on the critical path. Before this change the deploy path had no package-
  mirror dependency at all.
- **Fix**: Bake `libsqlite3-dev` into the runner image so the step becomes permanently a no-op, and
  record that requirement wherever the runner is provisioned.
- **Decision**: SKIPPED — closing it needs access to the runner image, which lives outside this
  repository. The guard keeps the apt call a no-op while the package survives; it returns to the
  critical path if the container is recreated.

### F5 — The DI registration casts, so a later re-registration fails at runtime rather than at build

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `Program.cs:82-83`
- **Detail**: `IHostEnvironmentAuthenticationStateProvider` is resolved as
  `(SessionCapAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>()`. The
  cast is what ties the two names to one instance, and the comment explains why that matters — but
  it also means any later registration that replaces `AuthenticationStateProvider` turns this into
  an `InvalidCastException` on the first request, not a compile error. The failure would look like a
  broken app rather than a wiring mistake.
- **Fix**: Register the concrete type once and map both interfaces to it —
  `AddScoped<SessionCapAuthenticationStateProvider>()` plus two factory registrations resolving that
  type — which removes the cast and keeps the single-instance guarantee explicit.
- **Decision**: FIXED — `SessionCapAuthenticationStateProvider` is registered as the concrete type
  once, with both `AuthenticationStateProvider` and `IHostEnvironmentAuthenticationStateProvider`
  resolving that registration. No cast, and the single-instance guarantee survives a later
  registration of a different provider.

### F6 — Phase 4's Progress rows carry no commit SHA

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `context/changes/auth-session-hardening/plan.md:361-374`
- **Detail**: The Progress convention at the top of the section says to append ` — <commit sha>`
  when a step lands. Phases 1–3 do. All eleven Phase 4 rows do not, although the phase produced two
  commits (`9aad04a`, `da43b9a`). The criteria were genuinely verified — the re-run above confirms
  every automated one — so this is a traceability gap, not a rubber-stamp: a later reader cannot
  tell from the plan which commit closed which criterion.
- **Fix**: Append `9aad04a` to the Phase 4 rows it closed and `da43b9a` to 4.11.
- **Decision**: FIXED — every Phase 4 row now carries the commit that closed it: `da43b9a` for
  4.1-4.3, 4.10 and 4.11; `9aad04a` for 4.5, 4.6, 4.8 and 4.9; `9aca39a` for 4.4 and 4.7.

### F7 — Tests now run against whatever SQLite the host happens to ship

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `tests/10xNotes.Tests/10xNotes.Tests.csproj:14-27`
- **Detail**: Swapping `bundle_e_sqlite3` for `.Core` + `bundle_sqlite3` was the right call for the
  runner's glibc, and the vulnerability scan is clean — but it is a dependency change the plan never
  mentions in any phase, recorded only in a commit body. Its consequence is that CI exercises
  Ubuntu 20.04's SQLite 3.31 while a developer machine uses whatever the OS provides, so the suite
  no longer pins one engine version. Concurrency tokens and query filters are stable across those
  versions, so nothing here is at risk today; the point is that the property changed silently.
- **Fix**: Note the swap and its consequence in the plan's Testing Strategy so the next reader knows
  the engine is host-supplied.
- **Decision**: FIXED — the plan's Testing Strategy now records the bundle swap, why it was needed
  (the runner's glibc) and its consequence (the SQLite engine is host-supplied, so the suite no
  longer pins one version).
