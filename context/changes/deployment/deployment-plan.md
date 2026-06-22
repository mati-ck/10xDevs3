# Deployment Plan — 10xNotes → Coolify (self-hosted)

Baseline decision: `@context/foundation/infrastructure.md` (self-hosted Coolify). This runbook is the executable, re-runnable version of the first-deployment work. Platform = **Coolify v4** on the user's **TrueNAS** host, with a co-located self-hosted GitHub Actions runner (`truenas-runner`, labels `self-hosted, coolify`). Topology: **the runner gates on a build; Coolify owns the build + release** (no external CI deploys — see `@.claude/prompts/m1l5-2-constrain-approach.md`).

## Prerequisites

- [x] Self-hosted runner online with labels `self-hosted, coolify`.
- [x] Coolify application already created (referenced by `COOLIFY_APP_UUID`).
- [x] GitHub repo secrets set: `COOLIFY_URL`, `COOLIFY_TOKEN`, `COOLIFY_APP_UUID`.
- [ ] Coolify app build pack = **Dockerfile**, exposed port **8080**, a domain (FQDN) assigned, HTTPS enabled (Traefik + Let's Encrypt).
- [ ] Coolify app HTTP health-check path set to **`/health`** (optional; the workflow already verifies `/health` over the public FQDN).
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
- [x] Build gate on the runner (`docker build -t 10xnotes:ci .`) before triggering.
- [x] Trigger `POST /api/v1/deploy?uuid=$APP` → capture `.deployments[0].deployment_uuid`.
- [x] Poll `GET /api/v1/deployments/{uuid}` until `.status` is terminal: success on `finished`; fail on `failed` / `cancelled-by-user`; 15-minute timeout.
- [x] Confirm the app serves: read `.fqdn` from `GET /api/v1/applications/$APP`, curl `<fqdn>/health` until 200 (≤30 tries). JSON parsed with `grep`/`cut` (no `jq` dependency).

## Phase 4 — Ship & verify live
- [ ] Open PR `chore/sync-m1l5-and-coolify-deploy` → `main`.
- [ ] Merge to `main` → `deploy.yml` runs on `truenas-runner` (build gate → Coolify deploy → poll → `/health`).
- [ ] Actions run is **green** = Coolify `finished` AND app returns 200 on `/health`.
- [ ] Browser check: home loads; `/counter` increments (interactive SignalR circuit over TLS).
- [ ] Idle-circuit check: tab idle > 5 min with no reconnect drop (Traefik idle-timeout; raise proxy read/idle timeout if it drops).

## Rollback
- Coolify UI rollback covers **locally-built images only**, not CI/CD-pushed — redeploy a known-good commit by re-running the workflow on that ref, or revert the merge on `main` (auto-redeploys). EF migrations are **not** auto-reversed (n/a until a DB is added).

## Follow-ups (not blocking the stateless skeleton)
- [ ] Container runs as **root** — add `USER $APP_UID` to the Dockerfile when hardening.
- [ ] **DataProtection keys** aren't persisted (warning on startup) — mount a volume / external key store before auth + antiforgery state matter.
- [ ] Reconcile `infrastructure.md` (it still describes a Hetzner VPS; reality is TrueNAS).
- [ ] Coolify v4 is beta — disable instance auto-update; configure off-box backups (host-level, operator responsibility).
