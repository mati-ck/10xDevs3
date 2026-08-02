<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Session and Owner-Contract Hardening

- **Plan**: `context/changes/auth-session-hardening/plan.md`
- **Scope**: Phase 1 of 4 — commit `62421b4`
- **Date**: 2026-08-02
- **Verdict**: NEEDS ATTENTION → after triage: 3 fixed, 0 outstanding
- **Findings**: 0 critical, 2 warnings, 1 observation

## Automated verification (re-run at HEAD)

| Criterion | Command | Result |
|---|---|---|
| 1.1 Solution builds | `dotnet build 10xnotes.sln` | PASS — 0 warnings, 0 errors |
| 1.2 Tests pass with the guard | `dotnet test` | PASS — 34 passed, 0 failed |
| 1.3 The guard actually guards | temporary `@inject AppDbContext` in a component | PASS — suite went red, message named `_10xnotes.Components.Pages.ScratchProof.Db` and `UserScopedDbContextFactory` |

Phase 1 declares no manual criteria, so there is nothing marked complete without evidence.

## Plan adherence summary

Both planned changes are present and match intent. `tests/10xNotes.Tests/DataAccessBoundaryTests.cs`
walks the app assembly for constructor parameters and `[Inject]` properties assignable to
`DbContext`, exactly as the contract specified, and checks against `DbContext` rather than
`AppDbContext` as the contract required. `AGENTS.md` carries the rule under "Data & persistence
rules". No MISSING items, no DRIFT.

The commit additionally carried `context/foundation/task-github.md`, which was surfaced by the
dirty-path prompt and explicitly approved by the user — not scope creep.

The plan itself was rewritten mid-phase: the originally planned separate-assembly split was
implemented, verified working, and backed out on user instruction. That reversal is recorded in
the plan's "What We're NOT Doing", in the brief's decision table as `Plan (revised)`, and in the
commit body. The plan and the code agree.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Findings

### F1 — The guard slips past `IDbContextFactory<AppDbContext>`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `tests/10xNotes.Tests/DataAccessBoundaryTests.cs:63`
- **Detail**: `IsDataContext` tests `typeof(DbContext).IsAssignableFrom(type)`, which catches a
  component injecting `AppDbContext` — but not one injecting `IDbContextFactory<AppDbContext>` and
  calling `CreateDbContext()`. That factory is registered at `Program.cs:20` and yields a context
  with `CurrentUserId` at `Guid.Empty`: identical failure mode, identical "the database is empty"
  symptom, and entirely invisible to the guard. It is also the *more* likely mistake of the two,
  because `IDbContextFactory<T>` is the pattern Blazor Server documentation recommends, so it is
  what anyone copying from docs will reach for. Latent today — no component touches the database —
  which is precisely why it is cheap to close now.
- **Fix**: Widen `IsDataContext` to also match a closed generic `IDbContextFactory<>`, and extend
  the failure message to name the factory case explicitly.
- **Decision**: FIXED — `IsDataContext` now matches `IDbContextFactory<>` as well, and the failure
  message names both. Widening it immediately caught `UserScopedDbContextFactory` itself, which
  must hold a factory — that is its job — so the test carries one explicit exemption, declared as
  `typeof(UserScopedDbContextFactory)` rather than a string so a rename cannot silently widen it.
  Verified: a component injecting `IDbContextFactory<AppDbContext>` now turns the suite red.

### F2 — Nothing runs the guard automatically

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `.github/workflows/deploy.yml:19-99`, `AGENTS.md` (Data & persistence rules)
- **Detail**: The only workflow in the repo triggers a Coolify deployment and polls until `/health`
  answers `Healthy`. There is no `dotnet build` and no `dotnet test` step anywhere in CI — the
  file's own header explains why ("the self-hosted runner has no Docker daemon access, so there is
  no local build step"). The boundary guard therefore fires only when a human or an agent chooses
  to run `dotnet test` locally; a violating commit pushed to `main` deploys with the guard never
  having executed. The `AGENTS.md` bullet added by this phase states the guard "fails the build",
  which is true locally and false in CI — the one place a reader would assume it means.
  This is not a defect introduced by Phase 1; it is a pre-existing gap that Phase 1's deliverable
  silently depends on. Worth naming now, because every test this change adds — the five adversarial
  write tests in Phase 2 and the session-cap tests in Phase 3 — inherits the same limitation.
- **Fix A ⭐ Recommended**: Correct the `AGENTS.md` wording to "fails `dotnet test`" and open a
  separate change for a CI test job.
  - Strength: Keeps this phase's scope honest and stops the documentation overstating a guarantee;
    the CI gap is a repo-wide concern that deserves its own change rather than riding along here.
  - Tradeoff: The guard stays advisory until that change lands.
  - Confidence: HIGH — the wording fix is one line and the CI gap is plainly visible in `deploy.yml`.
  - Blind spot: Whether the self-hosted runner can run `dotnet test` at all has not been verified;
    it lacks Docker, but the SDK requirement is a different question.
- **Fix B**: Add a `dotnet build` + `dotnet test` job to `deploy.yml` now, gating the deploy on it.
  - Strength: Closes the gap immediately and makes every test in this change load-bearing.
  - Tradeoff: Touches the deployment workflow, which the plan explicitly listed under "no
    `deploy.yml` change" — and a broken test job would block deploys, a failure mode the current
    workflow deliberately avoids.
  - Confidence: MEDIUM — depends on the runner having the .NET SDK, which is unverified.
  - Blind spot: Whether tests requiring no network really pass on that runner.
- **Decision**: FIXED via Fix B — `deploy.yml` gained a `test` job (checkout → `actions/setup-dotnet@v4`
  pinned to `10.0.x` → `dotnet test --configuration Release`) and the `deploy` job now declares
  `needs: test`. Deployment steps untouched. The plan's "no `deploy.yml` change" guardrail was
  reversed in place rather than quietly broken. **Unverified until the first push:** whether the
  self-hosted runner can run `actions/setup-dotnet` — if it cannot, the first deploy after this
  fails at the test job rather than deploying. `AGENTS.md` wording corrected in the same pass.

### F3 — `GetTypes()` fails loudly but unhelpfully on a partial load

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `tests/10xNotes.Tests/DataAccessBoundaryTests.cs:27`
- **Detail**: `applicationAssembly.GetTypes()` throws `ReflectionTypeLoadException` if any type in
  the app assembly cannot be loaded — a missing transitive dependency at test runtime, for example.
  The suite would then report a reflection error rather than either a pass or the intended
  ownership message, which reads as "the boundary test is broken" instead of "a dependency is
  missing". Not hit today; the assembly loads cleanly.
- **Fix**: Catch `ReflectionTypeLoadException` and fall back to its `Types` collection filtered to
  non-null, so a partial load still checks what did load.
- **Decision**: FIXED — extracted as `LoadableTypesOf`, which falls back to `ex.Types.OfType<Type>()`.
