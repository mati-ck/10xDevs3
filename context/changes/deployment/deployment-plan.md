# Deployment Plan — 10xNotes → Coolify (self-hosted)

Baseline decision: `@context/foundation/infrastructure.md` (self-hosted Coolify). This runbook is the executable, re-runnable version of the first-deployment work. Platform = **Coolify v4** on the user's **TrueNAS** host, with a co-located self-hosted GitHub Actions runner (`truenas-runner`, labels `self-hosted, coolify`). Topology: **the runner triggers + verifies; Coolify owns the build + release** (no external CI deploys — see `@.claude/prompts/m1l5-2-constrain-approach.md`). Note: the runner has **no Docker daemon access**, so a runner-side build gate isn't possible — a broken build surfaces as Coolify deployment `status=failed`, which the poll catches.

## Prerequisites

- [x] Self-hosted runner online with labels `self-hosted, coolify`.
- [x] Coolify application already created (referenced by `COOLIFY_APP_UUID`).
- [x] GitHub repo secrets set: `COOLIFY_URL`, `COOLIFY_TOKEN`, `COOLIFY_APP_UUID`.
- [x] **`COOLIFY_TOKEN` must have `read` + `deploy` scopes** (or `root`). A `deploy`-only token triggers deploys but gets **HTTP 403** on `GET /api/v1/deployments` and `/applications` — confirmed blocker on run 27976949712. Recreate the token in Coolify with both scopes and update the secret.
- [x] Coolify app build pack = **Dockerfile**, exposed port **8080**, domain `10xdevs3.coolify.pajewski.dev`, served over **https** at the edge.
- [x] **Health check is now in the Dockerfile and works.** Earlier, enabling Coolify's check broke routing because the `aspnet:10.0` image has no `curl`/`wget`, so the probe failed → `unhealthy` → de-routed → parked page. Fixed by installing `curl` in the runtime image and adding a Docker `HEALTHCHECK CMD curl -fsS http://localhost:8080/health` (verified `healthy` locally and on deploy). A Coolify-level check is optional now that the image self-reports health.
- CLI/token config (only needed for manual Coolify API calls, not for the pipeline):
  - Token scopes: `deploy` (trigger) + `read` (poll). Bearer header form `Authorization: Bearer <id>|<secret>`.
  - `export COOLIFY_URL=https://<coolify-host>` and `export COOLIFY_TOKEN=<token>` in a local shell to inspect: `curl -H "Authorization: Bearer $COOLIFY_TOKEN" "$COOLIFY_URL/api/v1/applications/<uuid>"`.

## Phase 0 — Git reconciliation
- [x] Commit uncommitted m1l5 toolkit + `AGENTS.md` + `infrastructure.md`; make `CLAUDE.md` a symlink → `AGENTS.md`.
- [x] Branch `chore/sync-m1l5-and-coolify-deploy` off `origin/main`; merge `main` (brings `Dockerfile`, `deploy.yml`, csproj `RequiresAspNetWebAssets`).

## Phase 1 — Repo readiness (stateless skeleton)
- [x] `Dockerfile` (on `main`): multi-stage .NET 10, `ASPNETCORE_HTTP_PORTS=8080`, `ENTRYPOINT ["dotnet","10xnotes.dll"]` — verified output DLL is `10xnotes.dll`.
- [x] `.dockerignore` added (keeps build context lean).
- [x] `Program.cs`: `UseForwardedHeaders` (trust Traefik `X-Forwarded-Proto`) first in pipeline; dropped app-level HTTPS redirection (edge proxy owns it; avoids 307 on the internal `/health` probe); `MapHealthChecks("/health")`.
- [x] `dotnet build -c Release` → 0 warnings, 0 errors.

## Phase 2 — Local container smoke test
- [x] `docker build -t 10xnotes:local .` succeeds.
- [x] `docker run --rm -p 8080:8080 10xnotes:local` → `GET /health` = 200 "Healthy"; `GET /` = 200 with `blazor.web.js`; listens on `:8080`.

## Phase 3 — Truthful deploy verification (`deploy.yml`)
- [x] ~~Build gate on the runner~~ — removed: the runner has no Docker daemon access (`docker build` → "Cannot connect to the Docker daemon"). Coolify owns the build; a failed build is caught by the poll below.
- [x] Trigger `POST /api/v1/deploy?uuid=$APP` → capture `.deployments[0].deployment_uuid`.
- [x] Poll `GET /api/v1/deployments/{uuid}` until `.status` is terminal: success on `finished`; fail on `failed` / `cancelled-by-user`; 15-minute timeout.
- [x] Confirm the app serves: read `.fqdn` from `GET /api/v1/applications/$APP`, then assert `<scheme>://<host>/health` returns the **body `Healthy`** (not just HTTP 200 — an edge placeholder returns 200 for any path, which gave a false green until this was fixed). Probe **https first**, then http. JSON parsed with `grep`/`cut` (no `jq` dependency).

## Phase 4 — Ship & verify live  ✅ DONE
- [x] Merge to `main` → `deploy.yml` runs on `truenas-runner` (trigger → poll → `/health`).
- [x] Actions run **green** = Coolify `finished` AND `https://10xdevs3.coolify.pajewski.dev/health` returns body `Healthy` (run 27979789640).
- [x] App is live: **https://10xdevs3.coolify.pajewski.dev**
- [ ] Manual: home loads; `/counter` increments (interactive SignalR circuit over TLS); idle tab > 5 min with no reconnect drop.

## Rollback
- Coolify UI rollback covers **locally-built images only**, not CI/CD-pushed — redeploy a known-good commit by re-running the workflow on that ref, or revert the merge on `main` (auto-redeploys).
- **EF migrations are not auto-reversed by a rollback.** As of `persistence-baseline` (F-01) the app has a database, migrations exist, and they **self-apply at container startup** (`DatabaseMigrationHostedService`). Rolling the image back therefore leaves the newer schema in place — so every migration must be **backward-compatible** with the previous image. To actually undo a schema change, run `dotnet ef database update <PreviousMigration>` deliberately.
- A failed startup migration does **not** crash the container: it is logged `Critical` and reported on `/health/ready`, while `/health` stays green so Coolify keeps routing. Check the container logs and `/health/ready` after any deploy that carries a migration.

## Endpoints
| Endpoint | Reports | Probed by |
| --- | --- | --- |
| `/health` | process liveness only — never touches the database | Docker `HEALTHCHECK`, `deploy.yml` |
| `/health/ready` | readiness: database reachable + migrations applied | humans, post-deploy verification |

Keep the database out of `/health`: a failing probe makes Coolify de-route the container (see the lessons below).

**There is a startup window where `/health` is green but migrations have not run yet.** Hosted
services start in registration order, and `AddHostedService<DatabaseMigrationHostedService>()` is
registered after the web host — so Kestrel binds and serves requests while `DatabaseMigrateAsync`
is still working. `/health` correctly reports `Healthy` throughout (it runs no checks), so nothing
stops Coolify routing traffic at a container running against a not-yet-migrated schema.

Mitigation: **gate the rollout on `/health/ready`, not `/health`.** `/health` stays the container
`HEALTHCHECK` — it must, because a database-dependent liveness probe is what caused the documented
outage below — but traffic should not shift to a new container until `/health/ready` returns
`Healthy`. Until that gate exists, treat a migration-carrying deploy as having a brief window of
stale-schema requests, and verify `/health/ready` by hand immediately after deploying.

## Required environment
- `ConnectionStrings__Postgres` — Supabase **session pooler** (`aws-<region>.pooler.supabase.com`, port 5432, user `postgres.<project-ref>`). The direct endpoint is IPv6-only without the paid add-on, and the transaction pooler (`:6543`) breaks Npgsql's prepared statements. Set on the Coolify resource; never committed.

## Lessons from the first deploy
- **Coolify health check + minimal .NET image = silent outage.** Enabling Coolify's container health check made the running app `unhealthy` because the `aspnet` image has no `curl`/`wget` for the probe; the proxy then stopped routing to it and the public URL served a parked page while the app was fine internally. Removing the check restored routing. (See the prerequisite above for how to add one safely later.)
- **"HTTP 200" is not "the app is serving."** An edge placeholder returns 200 for any path. Verification must assert app-specific content (here, the `/health` body `Healthy`).
- **The public app is on https; Coolify's `fqdn` field still reads `http://`.** Probe https first.
- **Token scopes are split:** `deploy` triggers but cannot read; polling status/app needs `read`.

## Follow-ups (not blocking the stateless skeleton)
- [x] Container runs as non-root (`USER $APP_UID`, uid 1654) with a working Docker `HEALTHCHECK`.
- [ ] **DataProtection keys** aren't persisted (warning on startup) — mount a volume / external key store before auth + antiforgery state matter.
- [ ] Reconcile `infrastructure.md` (it still describes a Hetzner VPS; reality is TrueNAS).
- [ ] Coolify v4 is beta — disable instance auto-update; configure off-box backups (host-level, operator responsibility).
