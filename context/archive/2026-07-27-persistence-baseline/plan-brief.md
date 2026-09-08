# F-01 Persistence Baseline — Plan Brief

> Full plan: `context/changes/persistence-baseline/plan.md`

## What & Why

10xNotes currently stores nothing — it's a stateless Blazor Server skeleton that happens to be deployed. Roadmap F-01 asks for the minimum trwałość contract: a connected database, a working migration mechanism, and a way to scope records to one user. Every other roadmap item (F-02 auth, S-01 the north-star generation loop) is blocked on it, and the PRD's privacy guardrail — one user's material is never visible to another — has to have a home in the data layer before any data exists.

## Starting Point

The app builds clean with **zero NuGet packages**, `Program.cs` registers only Razor components and a health check, and there is no connection string, no `DbContext`, no `dotnet-ef`, and no test project. The database, however, already exists: Supabase project `mahhzmgejamftvbcyrul` runs Postgres 17.6 with an empty `public` schema and a provisioned but unused GoTrue `auth` schema (0 users). The app is live at `https://10xdevs3.coolify.pajewski.dev`, deployed by a workflow that asserts `/health` returns the literal body `Healthy`.

## Desired End State

The deployed container holds a live connection to Supabase, applies its EF migrations automatically at boot, and owns one table — `public.profiles` — whose rows are keyed to `auth.users`, invisible to other users through an EF global query filter, and unreachable from Supabase's Data API through deny-all RLS. `/health` still reports only process liveness; a new `/health/ready` reports database reachability. A test project proves the scoping rule mechanically, giving the repo its first automated verification path.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Database host | Existing Supabase project | Already provisioned and empty, keeps the DB off the Coolify box per `infrastructure.md:64`, and the MCP server gives an out-of-band way to verify schema changes. |
| Local dev database | Same remote database | No container to run and zero dev/prod schema drift, accepted with the risk that local migrations mutate the real schema (see risks). |
| Identity store | Supabase Auth (`auth.users`) | Supabase already owns identity, so no ASP.NET Identity tables and no `IdentityDbContext` — F-02 wires the sign-in flow instead. |
| Isolation mechanism | EF global query filter + `OwnerId` | Idiomatic, testable without a database, and fail-closed: forgetting a `.Where()` cannot leak rows. |
| Data API exposure | RLS enabled, zero policies | `public` is reachable with the anon key; `anon`/`authenticated` don't bypass RLS but the app's `postgres` role does (verified live), so this closes the hole at zero cost to EF. |
| First migration content | `public.profiles` → `auth.users` | A real, non-throwaway record F-02 and S-01 build on, which makes the isolation contract provable today rather than theoretical. |
| Migration execution | Auto-apply at startup | Deploy and schema always move together — mitigated by running it in a hosted service that degrades readiness instead of crash-looping. |
| Health checks | Split liveness / readiness | A failing `/health` previously made Coolify de-route a healthy container; liveness must never depend on Postgres. |
| Connection mode | Session pooler (`:5432`) | Direct connection is IPv6-only on the free tier and the transaction pooler (`:6543`) breaks Npgsql's prepared statements. |
| Verification | xUnit project + isolation tests | Closes `health-check.md` fix #1 and turns the privacy guardrail into a permanently re-run assertion. |
| Hygiene scope | dotnet-ef tool manifest only | `dotnet ef` isn't installed at all today; lockfile and SDK pin were deliberately deferred to keep the diff focused. |

## Scope

**In scope:** EF Core 10 + Npgsql wiring · pinned `dotnet-ef` tool manifest · `AppDbContext` with snake_case conventions · `IOwnedByUser` + `ICurrentUserAccessor` + global query filter + insert stamping · `profiles` table with `auth.users` FK and deny-all RLS · startup migration runner · `/health` vs `/health/ready` split · `.sln` + xUnit test project · Coolify env var and live deploy verification · `AGENTS.md` and deployment-runbook updates.

**Out of scope:** authentication behavior of any kind (F-02) · domain entities `Note` / `SourceMaterial` (S-01) · RLS *policies* · local Postgres container · CI migration step or `deploy.yml` changes · backups, Supabase branching, IPv4 add-on · `global.json`, `.editorconfig`, NuGet lockfile.

## Architecture / Approach

```
Blazor Server (Interactive Server circuit)
        │
        ├─ ICurrentUserAccessor ──► auth.users.id (null until F-02)
        │
        └─ AppDbContext ──► global query filter: OwnerId == currentUser
                 │          SaveChanges: stamp OwnerId on insert
                 │
                 ▼  Npgsql, session pooler :5432
        Supabase Postgres 17.6
                 ├─ public.profiles   (RLS on, no policies → Data API closed)
                 │        └─ owner_id ──FK──► auth.users.id  ON DELETE CASCADE
                 └─ __EFMigrationsHistory   (applied by a startup hosted service)
```

The isolation contract is generic on purpose: S-01's entities inherit scoping by implementing a single interface.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Data layer wiring | Packages, EF tool, `AppDbContext`, connection string, health split | Wrong connection mode — IPv6-only direct endpoint or the prepared-statement-hostile `:6543` pooler |
| 2. Migration & isolation | `profiles`, FK to `auth.users`, deny-all RLS, query filter, startup migration | Migration code placed inline in `Program.cs` would run against the live DB during `ef migrations add` |
| 3. Tests | `.sln`, xUnit project, owner-scoping tests | Tests that pass vacuously and don't actually exercise the filter |
| 4. Deploy verification | Env var in Coolify, live deploy, readiness confirmed in production | A DB check leaking into `/health` and repeating the documented de-routing outage |

**Prerequisites:** Supabase database password and the session-pooler host/region from the project's Connect dialog; Coolify admin access to set an env var; the existing deploy workflow green.
**Estimated effort:** ~2–3 after-hours sessions across 4 phases; Phase 2 is the largest.

## Open Risks & Assumptions

- **Dev and prod share one database.** A local `dotnet ef database update` mutates the live schema. Harmless today (0 users, 0 rows), but this must be revisited before launch — a separate dev database or Supabase branch is the eventual answer.
- **Auto-migration at startup was chosen over manual apply.** A bad migration reaches production the moment the container boots. Mitigated by never rethrowing (readiness degrades instead of crash-looping) and by the standing rule that migrations stay backward-compatible, since a Coolify rollback does not reverse them.
- **Supabase Auth for Blazor Server is unproven here.** A SignalR circuit can't hold a JWT in browser storage, so F-02 will likely need to exchange a GoTrue sign-in for an ASP.NET cookie. F-01 only commits to the owner-key *shape* (`Guid` = `auth.users.id`), so that work stays cheap to change.
- **The `profiles` shape deviates from Supabase's canonical `id = auth.users.id`** in favour of a surrogate `Id` plus `OwnerId`, to keep one uniform ownership convention across all future tables.
- **Free-tier connection assumptions.** Session-pooler availability and the region host must be read from the project's Connect dialog; if the host network turns out to be IPv6-capable, the direct connection also works.

## Success Criteria (Summary)

- The deployed app reaches Supabase: `/health/ready` returns `Healthy` in production while `/health` keeps returning the exact body the deploy workflow asserts on.
- A record can be written and read back scoped to one user, and no query returns another user's rows — proven by `dotnet test`, not by inspection.
- `public.profiles` exists with a live FK to `auth.users` and RLS enabled, so nothing in the app's schema is readable through the Supabase Data API.
