# F-01 Persistence Baseline — Implementation Plan

## Overview

Give 10xNotes a real, per-user-scoped persistence layer: EF Core 10 + Npgsql connected to the already-provisioned Supabase Postgres, a working migration mechanism, and an owner-scoping contract that every later slice (F-02, S-01…) inherits for free. This is the minimal trwałość contract from roadmap F-01 — connection + migrations + isolation — **not** the domain model.

## Current State Analysis

The app is a stateless Blazor Server skeleton that is already live in production.

- **No data layer at all.** `10xnotes.csproj:1-14` declares zero `PackageReference` (framework reference only). `Program.cs:1-33` registers Razor components and `AddHealthChecks()` and nothing else. `appsettings.json` has no `ConnectionStrings` section.
- **No EF tooling.** `dotnet ef` is not installed (`dotnet tool list --global` is empty, there is no `.config/dotnet-tools.json`). Local SDK is 10.0.301.
- **The database already exists and is empty.** Supabase project `mahhzmgejamftvbcyrul` (`.mcp.json:4`) runs **Postgres 17.6**. `public` has no tables; `supabase_migrations.schema_migrations` is empty. The GoTrue `auth` schema is provisioned with **0 users**.
- **The app is deployed and healthy.** `Dockerfile:19-27` runs `HEALTHCHECK curl -fsS http://localhost:8080/health`; `.github/workflows/deploy.yml` triggers Coolify, polls to a terminal state, and asserts the public `/health` body is literally `Healthy`. Live at `https://10xdevs3.coolify.pajewski.dev`.
- **No verification path beyond the compiler.** `context/foundation/health-check.md:65-90` flags the missing test runner as the highest-impact gap; `AGENTS.md:21` states `dotnet build` is the only verification path. There is no `.sln`.

## Desired End State

The deployed app holds a live connection to Supabase Postgres, applies its EF migrations automatically at startup, and exposes a `public.profiles` table whose rows are keyed to `auth.users` and are unreachable both from other users (application-level query filter) and from the Supabase Data API (deny-all RLS). A test project proves the scoping rule mechanically.

Verify by:
1. `dotnet build` and `dotnet test` both pass locally.
2. `curl https://10xdevs3.coolify.pajewski.dev/health` → body `Healthy` (unchanged liveness).
3. `curl https://10xdevs3.coolify.pajewski.dev/health/ready` → `Healthy`, meaning the deployed container reached Supabase and migrations are applied.
4. Supabase MCP confirms `public.profiles` exists, `rls_enabled = true`, the `auth.users` FK is present, and `__EFMigrationsHistory` contains the initial migration.

### Key Discoveries:

- **`public` is exposed through Supabase's Data API.** Any table created there is readable by anyone holding the project's anon key. Verified role attributes: `anon` and `authenticated` have `rolbypassrls = false`, while `postgres` (the EF connection role) has `rolbypassrls = true`. Therefore **RLS enabled with zero policies** blocks the Data API entirely and is completely invisible to EF Core. This is hardening beneath the chosen app-level filter, not a substitute for it.
- **The `auth.users` FK is permitted.** Verified live: `has_table_privilege('postgres','auth.users','REFERENCES')` → `true`.
- **Connection mode is a trap.** Supabase's direct connection (`db.<ref>.supabase.co:5432`) is **IPv6-only** without the paid IPv4 add-on, and the transaction pooler (`:6543`) does **not support prepared statements**, which Npgsql uses by default. The **session pooler** (`aws-<region>.pooler.supabase.com:5432`, user `postgres.<project-ref>`) is IPv4, supports prepared statements, and is migration-safe — it is the correct choice for a long-lived Blazor Server process.
- **A failing `/health` de-routes the app.** `context/changes/deployment/deployment-plan.md` records a real outage: enabling a health probe the container could not satisfy made Coolify mark it unhealthy and stop routing, serving a parked page while the app was fine. Liveness must therefore never depend on Postgres.
- **Migrations are not covered by rollback.** `context/foundation/infrastructure.md:72` — a Coolify rollback does not reverse EF migrations; migrations must be backward-compatible.
- **Package versions verified on nuget.org** (2026-07-27): `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3; `Microsoft.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`, `dotnet-ef` all 10.0.10; `EFCore.NamingConventions` 10.0.1.

## What We're NOT Doing

- **No authentication behavior** — no registration, login, logout, auth middleware, route protection, or Supabase GoTrue client wiring. That is F-02. F-01 only establishes the *shape* of the owner key.
- **No ASP.NET Core Identity** — Supabase Auth owns identity, so no `IdentityDbContext` and no `AspNetUsers*` tables.
- **No domain entities** — `Note`, `SourceMaterial` and their relationships belong to S-01.
- **No RLS policies** — RLS is enabled deny-all purely to close the Data API. Policy-based access control was explicitly declined in favour of the EF query filter.
- **No local Postgres container** — development points at the remote Supabase database by decision.
- **No CI migration step** — migrations self-apply at app startup; `deploy.yml` is not modified.
- **No backup/DR work, no Supabase branching, no IPv4 add-on purchase.**
- **No `global.json`, `.editorconfig`, or NuGet lockfile** — only the dotnet-ef tool manifest was accepted from the hygiene list.

## Implementation Approach

Four phases, each independently verifiable. Phase 1 gets a connection and proves the deployed process can reach Supabase, without creating a single table. Phase 2 introduces the one table plus the isolation contract that all later work inherits. Phase 3 makes that contract executable as tests. Phase 4 carries it to production.

The isolation contract is deliberately generic: an `IOwnedByUser` marker with a `Guid OwnerId`, a global query filter applied by convention to **every** entity implementing it, and a `SaveChanges` override that stamps `OwnerId` on insert. `Profile` is simply the first entity to use it, so S-01's `Note` and `SourceMaterial` inherit isolation by implementing one interface.

## Critical Implementation Details

**Timing & lifecycle — migration code must not run at design time.** `dotnet ef migrations add` builds the application host to discover the `DbContext`. If `Database.Migrate()` is called inline in `Program.cs`, EF executes it against the live database during scaffolding. Putting it in an `IHostedService` avoids this: hosted services are constructed but never started by the EF design-time host. Coolify runs a single container, so no cross-instance migration race exists.

**State sequencing — liveness must not depend on Postgres.** `/health` (what `Dockerfile:26` probes) must stay a bare liveness check. The DB check and the migration-state check both go on `/health/ready` under a `ready` tag. When migration fails, the app must still **start** — logging critical and reporting readiness unhealthy — because a crash-loop would fail the Docker HEALTHCHECK and repeat the documented de-routing outage.

**Debug & observability** — schema changes are verified out-of-band via the Supabase MCP server (`list_tables`, `execute_sql`), not by trusting EF's own output.

---

## Phase 1: Data layer wiring

### Overview

Add EF Core + Npgsql, pin the EF tool, register a `DbContext` with no entities yet, and split health endpoints — proving the process can reach Supabase before any schema exists.

### Changes Required:

#### 1. Package references

**File**: `10xnotes.csproj`

**Intent**: Introduce the project's first NuGet dependencies: the Npgsql EF provider, EF design-time tooling, the first-party EF health check, and snake_case naming so Postgres identifiers stay unquoted and legible in the Supabase SQL editor.

**Contract**: Add to the existing `<PropertyGroup>`'s sibling scope a new `<ItemGroup>` with `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Microsoft.EntityFrameworkCore.Design` 10.0.10 (`PrivateAssets="all"`), `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.10, and `EFCore.NamingConventions` 10.0.1. Do not change `TargetFramework`, `RootNamespace`, or any existing property.

#### 2. EF tool manifest

**File**: `.config/dotnet-tools.json` (new)

**Intent**: Pin `dotnet-ef` locally so migration commands are reproducible for you, for an agent, and in CI — it is not installed on this machine today.

**Contract**: Created by `dotnet new tool-manifest` followed by `dotnet tool install dotnet-ef --version 10.0.10`. All migration commands in this plan are therefore invoked as `dotnet tool restore` once, then `dotnet ef …`.

#### 3. Database context

**File**: `Data/AppDbContext.cs` (new)

**Intent**: The application's single EF entry point. In this phase it has no entity sets — its only job is to prove configuration and connectivity.

**Contract**: `namespace _10xnotes.Data;` (leading underscore per the hard rule in `AGENTS.md:8`). `public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)`. No `DbSet` yet; entity sets and `OnModelCreating` arrive in Phase 2.

#### 4. Service registration and health-endpoint split

**File**: `Program.cs`

**Intent**: Register the context against the `Postgres` connection string with snake_case conventions, and separate liveness from readiness so a database outage can never de-route the container.

**Contract**: `AddDbContext<AppDbContext>` using `UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))` chained with `.UseSnakeCaseNamingConvention()`. Health checks gain `.AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"])`. The existing `app.MapHealthChecks("/health")` keeps its current behavior but must now exclude tagged checks, and a new `/health/ready` runs only `ready`-tagged ones — the predicate is the load-bearing part:

```csharp
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });      // liveness only
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
```

`Predicate = _ => false` is what keeps `/health` returning the literal body `Healthy` that `deploy.yml` and `Dockerfile:26` both assert on.

#### 5. Connection string configuration

**File**: `appsettings.json`, plus user-secrets (not committed)

**Intent**: Document the configuration key in source control while keeping the credential out of the repository.

**Contract**: `appsettings.json` gains `"ConnectionStrings": { "Postgres": "" }` as a documented empty placeholder. The real value goes into user-secrets locally and the `ConnectionStrings__Postgres` environment variable in Coolify (Phase 4). Use the **session pooler**, not the direct connection and not port 6543:

```
Host=aws-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.mahhzmgejamftvbcyrul;Password=<db-password>;SSL Mode=Require;Trust Server Certificate=true
```

Initialize with `dotnet user-secrets init` (adds a `UserSecretsId` to the csproj) and `dotnet user-secrets set "ConnectionStrings:Postgres" "<value>"`.

#### 6. Configuration note in the README

**File**: `README.md`

**Intent**: Record the one environment variable the app now requires, closing part of `health-check.md` fix #5.

**Contract**: A short "Configuration" section naming `ConnectionStrings__Postgres`, stating that the session pooler must be used, and pointing at user-secrets for local development. No secret values.

### Success Criteria:

#### Automated Verification:

- Tool restore succeeds: `dotnet tool restore`
- Build is clean: `dotnet build` → 0 warnings, 0 errors
- App starts locally: `dotnet run` reaches "Now listening on"
- Liveness unchanged: `curl -s http://localhost:5125/health` → body `Healthy`
- Readiness reaches Supabase: `curl -s http://localhost:5125/health/ready` → body `Healthy`

#### Manual Verification:

- With a deliberately wrong password in user-secrets, `/health` still returns `Healthy` while `/health/ready` returns `Unhealthy` — confirming a DB outage cannot de-route the container
- No credential appears in any tracked file (`git diff` review before commit)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: First migration and the owner-scoping contract

### Overview

Introduce the isolation contract and the single table that exercises it, then apply it to the database through a migration that self-applies at startup.

### Changes Required:

#### 1. Ownership marker

**File**: `Data/IOwnedByUser.cs` (new)

**Intent**: The one interface every user-owned entity implements from now on; the query filter and the insert-stamping logic both key off it.

**Contract**: `public interface IOwnedByUser { Guid OwnerId { get; set; } }` in `_10xnotes.Data`. `OwnerId` holds the Supabase `auth.users.id`.

#### 2. Current-user accessor

**File**: `Data/ICurrentUserAccessor.cs`, `Data/HttpContextCurrentUserAccessor.cs` (new)

**Intent**: Give the DbContext a single, replaceable source of "who is asking" without knowing anything about how authentication works — F-02 swaps the implementation without touching data code.

**Contract**: `public interface ICurrentUserAccessor { Guid? UserId { get; } }`. The implementation reads the `sub` (or `ClaimTypes.NameIdentifier`) claim from `IHttpContextAccessor.HttpContext?.User` and parses it as a `Guid`, returning `null` when absent or unparseable. Registered scoped in `Program.cs` alongside `AddHttpContextAccessor()`. Until F-02 lands, `UserId` is always `null` in the running app — which is correct: an unauthenticated request must see nothing.

#### 3. Profile entity

**File**: `Data/Entities/Profile.cs` (new)

**Intent**: The app-side user record — the first owned entity, and a real one that F-02 and S-01 will build on rather than a throwaway probe.

**Contract**: `public sealed class Profile : IOwnedByUser` with `Guid Id` (PK, database-generated), `Guid OwnerId` (FK → `auth.users.id`, unique), `string? DisplayName` (max length 200), and `DateTimeOffset CreatedAt`.

Note the deliberate deviation from Supabase's canonical `profiles.id = auth.users.id` pattern: a surrogate `Id` plus a separate `OwnerId` keeps **one** uniform ownership convention across every table in the codebase, so S-01's entities need no special-casing. The uniqueness constraint on `OwnerId` preserves the one-profile-per-user guarantee.

#### 4. Query filter and insert stamping

**File**: `Data/AppDbContext.cs`

**Intent**: Make owner scoping the default that must be explicitly opted out of, so forgetting a `.Where()` cannot leak another user's rows, and assigning ownership on insert is automatic rather than remembered.

**Contract**: The context takes `ICurrentUserAccessor` via constructor and exposes `DbSet<Profile> Profiles`. `OnModelCreating` iterates `modelBuilder.Model.GetEntityTypes()`, and for every CLR type assignable to `IOwnedByUser` applies a global query filter comparing `OwnerId` to the current user. `SaveChangesAsync`/`SaveChanges` are overridden to set `OwnerId` on `Added` entries whose `OwnerId` is still `Guid.Empty`, throwing `InvalidOperationException` when no user is present. The filter must compare against a **context instance field** refreshed per-instance, not a captured service call, so EF's compiled model stays valid:

```csharp
private readonly Guid _currentUserId;   // = currentUser.UserId ?? Guid.Empty, set in the constructor
// OnModelCreating, per owned entity type:
//   e => EF.Property<Guid>(e, nameof(IOwnedByUser.OwnerId)) == _currentUserId
```

With no authenticated user, `_currentUserId` is `Guid.Empty` and every owned query returns zero rows — fail-closed by construction.

#### 5. Initial migration

**File**: `Migrations/<timestamp>_InitialPersistenceBaseline.cs` (generated, then hand-edited)

**Intent**: Create `public.profiles`, tie it to Supabase's identity store, and close the Data API hole before the table ever holds data.

**Contract**: Generated with `dotnet ef migrations add InitialPersistenceBaseline`. Two things EF cannot express must be appended by hand inside `Up` — a foreign key into the `auth` schema (which this context does not manage) and the deny-all RLS switch:

```csharp
migrationBuilder.Sql("""
    ALTER TABLE public.profiles
      ADD CONSTRAINT fk_profiles_owner_id_auth_users
      FOREIGN KEY (owner_id) REFERENCES auth.users (id) ON DELETE CASCADE;
    ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;
    """);
```

`Down` must drop the constraint before the table. **No policies are created** — with zero policies, `anon`/`authenticated` (neither of which bypasses RLS) can read nothing through the Data API, while the app's `postgres` role (`rolbypassrls = true`) is unaffected. Verified against the live project.

#### 6. Startup migration runner

**File**: `Data/DatabaseMigrationHostedService.cs` (new), registered in `Program.cs`

**Intent**: Apply pending migrations automatically on boot, without ever turning a migration failure into a container crash-loop.

**Contract**: An `IHostedService` (**not** inline `Program.cs` code — see Critical Implementation Details) that resolves a scoped `AppDbContext` in `StartAsync`, calls `Database.MigrateAsync()`, and on success flips a shared singleton state object to "applied". On failure it logs at `Critical` and records the failure **without rethrowing**, so the host still starts. A small health check reads that state and is registered with the `ready` tag, so a failed migration surfaces as `/health/ready` → `Unhealthy` while `/health` stays green.

### Success Criteria:

#### Automated Verification:

- Build is clean: `dotnet build` → 0 warnings, 0 errors
- Migration is scaffolded and listed: `dotnet ef migrations list` shows `InitialPersistenceBaseline`
- Migration applies: `dotnet ef database update` completes without error
- App starts and logs "No pending migrations" / applied state; `curl -s http://localhost:5125/health/ready` → `Healthy`

#### Manual Verification:

- Supabase MCP `list_tables` shows `public.profiles` with `rls_enabled: true`
- `execute_sql` confirms the `fk_profiles_owner_id_auth_users` constraint references `auth.users`, and that `__EFMigrationsHistory` holds exactly one row
- Reading `profiles` through the Data API with the anon key returns no rows (Data API is closed)
- Deliberately breaking the connection string leaves the app **started** with `/health` green and `/health/ready` unhealthy — no crash-loop

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 3: Test project and isolation tests

### Overview

Turn the privacy guardrail into an executable assertion and give the repository its first test runner, closing `health-check.md` fix #1.

### Changes Required:

#### 1. Solution file

**File**: `10xnotes.sln` (new)

**Intent**: The repo has no `.sln`; a test project needs one so `dotnet build` / `dotnet test` cover both projects.

**Contract**: `dotnet new sln -n 10xnotes`, then add `10xnotes.csproj` and the test project. `AGENTS.md:13` currently states "there is no `.sln`" and must be corrected in change 3 below.

#### 2. Test project

**File**: `tests/10xNotes.Tests/10xNotes.Tests.csproj` (new)

**Intent**: A fast, network-free place to assert data-layer behavior.

**Contract**: `dotnet new xunit`, targeting `net10.0`, referencing `10xnotes.csproj` and `Microsoft.EntityFrameworkCore.Sqlite` 10.0.10. SQLite in-memory is used because query-filter and change-tracker behavior are provider-independent; Postgres-specific SQL is verified against the real database in Phase 2/4 instead.

#### 3. Owner-scoping tests

**File**: `tests/10xNotes.Tests/OwnerScopingTests.cs` (new)

**Intent**: Prove the isolation contract mechanically, so a future refactor that removes the filter fails the build rather than leaking data silently.

**Contract**: A test fixture builds an `AppDbContext` over a SQLite in-memory connection with a stub `ICurrentUserAccessor`. Cases:
- a query returns only the current user's rows when two users' rows exist;
- `SaveChanges` stamps `OwnerId` from the accessor on insert;
- constructing the context as a different user changes which rows are visible;
- `IgnoreQueryFilters()` returns all rows — documenting the one deliberate escape hatch;
- saving with no current user throws `InvalidOperationException` rather than writing an unowned row.

#### 4. Agent documentation refresh

**File**: `AGENTS.md` (`CLAUDE.md` is a symlink to it)

**Intent**: Three statements become false with this change and would misdirect a future agent.

**Contract**: Update "Project Structure" (a `.sln` now exists; `Data/` and `Migrations/` are new top-level areas), "Build, Test & Development Commands" (add `dotnet test`, `dotnet tool restore`, and the `dotnet ef` workflow), and remove the "No test project exists yet" sentence at `AGENTS.md:21`. Do not touch the `<!-- BEGIN/END @przeprogramowani/10x-cli -->` block (hard rule, `AGENTS.md:9`).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- Tests pass: `dotnet test` → all green, 0 failures
- The app project still builds standalone: `dotnet build 10xnotes.csproj`

#### Manual Verification:

- Temporarily removing the global query filter makes the scoping tests **fail** — confirming they actually test the guarantee rather than passing vacuously
- `AGENTS.md` no longer contains a statement contradicted by the repo

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 4: Deploy verification

### Overview

Carry the persistence layer to the live Coolify deployment and confirm the running container reaches Supabase and self-applies its migration.

### Changes Required:

#### 1. Production connection string

**File**: Coolify application environment (not a repo file)

**Intent**: Supply the credential to the deployed container through the platform's secret store, never through the repository.

**Contract**: Add `ConnectionStrings__Postgres` (double underscore — ASP.NET Core's env-var nesting separator) to the Coolify resource's Environment tab, using the same session-pooler string shape as Phase 1. Per `infrastructure.md:71`, this value is readable by anyone with the Coolify admin login or an API token.

#### 2. Deploy and verify

**File**: no repo change — merge to `main` triggers `.github/workflows/deploy.yml`

**Intent**: Prove the whole path end to end in production.

**Contract**: The existing workflow is unmodified. Its `/health` assertion must still pass (this is why liveness excludes the DB check); readiness is verified separately by hand.

#### 3. Deployment runbook update

**File**: `context/changes/deployment/deployment-plan.md`

**Intent**: The runbook's rollback section states EF migrations are "n/a until a DB is added" — that is now false, and it is exactly the note a future deploy needs.

**Contract**: Update the Rollback section to record that migrations now exist, self-apply at startup, and are **not** reversed by a Coolify rollback — so migrations must stay backward-compatible (`infrastructure.md:72`). Add `/health/ready` to the documented endpoints.

### Success Criteria:

#### Automated Verification:

- Deploy workflow run is green (Coolify `finished` + public `/health` body `Healthy`)
- Liveness holds: `curl -s https://10xdevs3.coolify.pajewski.dev/health` → `Healthy`
- Readiness holds: `curl -s https://10xdevs3.coolify.pajewski.dev/health/ready` → `Healthy`

#### Manual Verification:

- Supabase MCP confirms `__EFMigrationsHistory` contains the initial migration applied by the **deployed** container (not only by the local run)
- Container logs show the migration hosted service completing without a `Critical` entry
- The app's existing pages still load and `/counter` still increments over the SignalR circuit (no regression from the new startup work)
- `ConnectionStrings__Postgres` is set in Coolify and absent from the repository

**Implementation Note**: This is the final phase; after manual confirmation the change is ready for `/10x-archive`.

---

## Testing Strategy

### Unit Tests:

- Global query filter scopes results to the current user across multiple users' rows
- `SaveChanges` stamps `OwnerId` on insert; refuses to write when no user is present
- Changing the current user changes the visible row set
- `IgnoreQueryFilters()` bypasses scoping (documented escape hatch)

### Integration Tests:

None automated in this change. Postgres-specific behavior (the `auth.users` FK, RLS state, migration SQL) is verified against the live database via the Supabase MCP server as manual criteria in Phases 2 and 4. A Testcontainers-based integration suite was explicitly declined.

### Manual Testing Steps:

1. Set the session-pooler connection string in user-secrets; run `dotnet run`; confirm `/health` = `Healthy` and `/health/ready` = `Healthy`.
2. Corrupt the password; restart; confirm the app **still starts**, `/health` stays `Healthy`, `/health/ready` turns `Unhealthy`.
3. Run `dotnet ef database update`; via MCP confirm `profiles` exists with `rls_enabled: true` and the `auth.users` FK.
4. Query `profiles` through the Supabase Data API with the anon key; confirm no rows are returned.
5. After deploy, confirm both endpoints on the public URL and that migration history shows the deployed container's work.

## Performance Considerations

The session pooler adds a small latency hop versus a direct connection but is required for IPv4 reachability and prepared-statement support. Npgsql's built-in application-side pooling is sufficient for a persistent single-container backend — per Supabase's guidance, no additional server-side pooling configuration is needed at this scale (`target_scale.qps: low` in the PRD). The global query filter costs one extra `WHERE owner_id = …` predicate; the unique index on `owner_id` makes it an index lookup.

## Migration Notes

The database is empty and the application has no users, so the initial migration carries no data-migration risk. From this change forward, every migration must be **backward-compatible** — a Coolify rollback restores the previous image but leaves the newer schema in place (`infrastructure.md:72`).

One consequence of pointing development at the production database: `dotnet ef database update` run locally mutates the live schema. That is acceptable now (zero users, zero rows) but must be revisited before launch — recorded as an open risk in the brief.

Two migration ledgers coexist in this database: `__EFMigrationsHistory` (owned by this app, authoritative for the `public` app schema) and `supabase_migrations.schema_migrations` (Supabase's own, currently empty). Do not apply app schema changes through the Supabase MCP `apply_migration` tool — it would write to the wrong ledger and drift from EF's model snapshot.

## References

- Roadmap item: `context/foundation/roadmap.md` → F-01 "Trwała warstwa danych per użytkownik"
- Change identity: `context/changes/persistence-baseline/change.md`
- Brief: `context/changes/persistence-baseline/plan-brief.md`
- Deployment constraints: `context/foundation/infrastructure.md:64`, `:71`, `:72`
- Health-check outage precedent: `context/changes/deployment/deployment-plan.md` → "Lessons from the first deploy"
- Gaps this closes: `context/foundation/health-check.md:65-90` (fix #1), `:182-187` (fix #5, partial)
- Current wiring: `Program.cs:1-33`, `10xnotes.csproj:1-14`, `Dockerfile:19-27`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Data layer wiring

#### Automated

- [x] 1.1 Tool restore succeeds: `dotnet tool restore` — 663cb8a
- [x] 1.2 Build is clean: `dotnet build` → 0 warnings, 0 errors — 663cb8a
- [x] 1.3 App starts locally: `dotnet run` reaches "Now listening on" — 663cb8a
- [x] 1.4 Liveness unchanged: `/health` → body `Healthy` — 663cb8a
- [x] 1.5 Readiness reaches Supabase: `/health/ready` → body `Healthy` — 663cb8a

#### Manual

- [x] 1.6 Wrong password leaves `/health` green while `/health/ready` reports unhealthy — 663cb8a
- [x] 1.7 No credential appears in any tracked file — 663cb8a

### Phase 2: First migration and the owner-scoping contract

#### Automated

- [x] 2.1 Build is clean: `dotnet build` → 0 warnings, 0 errors
- [x] 2.2 `dotnet ef migrations list` shows `InitialPersistenceBaseline`
- [x] 2.3 `dotnet ef database update` completes without error
- [x] 2.4 App starts with migrations applied; `/health/ready` → `Healthy`

#### Manual

- [x] 2.5 MCP `list_tables` shows `public.profiles` with `rls_enabled: true`
- [x] 2.6 FK to `auth.users` exists and `__EFMigrationsHistory` holds one row
- [x] 2.7 Data API read of `profiles` with the anon key returns no rows
- [x] 2.8 Broken connection string does not crash-loop the app

### Phase 3: Test project and isolation tests

#### Automated

- [ ] 3.1 Solution builds: `dotnet build 10xnotes.sln` → 0 warnings, 0 errors
- [ ] 3.2 Tests pass: `dotnet test` → all green
- [ ] 3.3 App project still builds standalone: `dotnet build 10xnotes.csproj`

#### Manual

- [ ] 3.4 Removing the query filter makes the scoping tests fail
- [ ] 3.5 `AGENTS.md` contains no statement contradicted by the repo

### Phase 4: Deploy verification

#### Automated

- [ ] 4.1 Deploy workflow run is green
- [ ] 4.2 Public `/health` → `Healthy`
- [ ] 4.3 Public `/health/ready` → `Healthy`

#### Manual

- [ ] 4.4 Migration history shows the deployed container applied the migration
- [ ] 4.5 Container logs show no `Critical` migration entry
- [ ] 4.6 Existing pages and `/counter` still work over the SignalR circuit
- [ ] 4.7 `ConnectionStrings__Postgres` set in Coolify, absent from the repo
