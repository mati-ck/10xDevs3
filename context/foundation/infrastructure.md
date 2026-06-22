---
project: 10xnotes
researched_at: 2026-06-22
recommended_platform: Coolify (self-hosted on a VPS)
runner_up: Fly.io
context_type: mvp
tech_stack:
  language: C#
  framework: ASP.NET Core — Blazor Web App (Interactive Server render mode)
  runtime: .NET 10
---

## Recommendation

**Deploy on a self-hosted Coolify instance running on a small VPS (e.g. Hetzner CX22, 2 vCPU / 4 GB, ~€4–5/mo).** This is the developer's explicit choice and it aligns with the `deployment_target: self-host` hint already recorded in `tech-stack.md`. Coolify (MIT/Apache-2.0, open-source PaaS) deploys the app from a multi-stage Dockerfile, fronts it with Traefik which proxies the Blazor Server SignalR/WebSocket circuit natively, and exposes a token-based REST API + official `coolify-cli` for agent-driven deploy/logs/env. A single container sidesteps the sticky-session problem entirely (Blazor Server needs affinity only when scaled past one instance). The trade-off — accepted deliberately — is that you take on full sysadmin duty (OS patching, hardening, backups, Coolify upgrades) and run on Coolify's still-beta v4 line. **Fly.io is the managed fallback** if that operational burden proves too heavy for a solo, after-hours build.

## Platform Comparison

Three of the default candidates were **dropped before scoring on the hard tech-stack constraint**: Cloudflare Workers, Vercel, and Netlify run only JS/TS/Wasm/Go functions with execution-time limits and cannot host a long-lived .NET process holding a per-user SignalR circuit (each verified against official docs). The Blazor *WebAssembly* static export they can host is a different render mode than this project's Interactive Server stack.

| Platform | CLI-first | Managed | Agent docs | Stable deploy API | MCP / Integration | Cheapest always-on | Verdict |
|---|---|---|---|---|---|---|---|
| **Coolify (self-host)** | Pass (`coolify-cli` + REST API) | Partial (PaaS layer, but you own the VPS) | Pass (`llms.txt`) | Partial (deploy clean; **rollback weak**) | Partial (built-in MCP **beta, read-only**) | **~€4–5/mo VPS** | **Recommended** |
| **Fly.io** | Pass (`flyctl`) | Pass (managed machines) | Partial (no llms.txt; flyctl MCP) | Pass | Pass (flyctl MCP server) | ~$2–3.3/mo | **Runner-up** |
| Railway | Pass | Pass | Pass (`llms-full.txt`) | Pass | Partial (MCP beta) | ~$5/mo (Hobby) | Shortlisted |
| Render | Pass | Pass | Pass (`llms.txt`) | Pass | Pass (MCP GA) | $7/mo (free = spin-down) | Shortlisted |
| Azure App Service | Pass | Pass | Partial | Pass | Partial (MCP preview) | ~$13/mo (B1) | Not shortlisted (cost) |
| Cloudflare / Vercel / Netlify | — | — | — | — | — | — | **Dropped — no .NET runtime** |

### Shortlisted Platforms

#### 1. Coolify — self-hosted on a VPS (Recommended — developer's choice)

Strongest on cost-at-MVP and control: the platform software is free, so the only spend is a ~€4–5/mo VPS, and there is no vendor lock-in. **.NET 10** builds via a multi-stage Dockerfile (`mcr.microsoft.com/dotnet/sdk:10.0` → `aspnet:10.0`); Nixpacks also detects `.csproj` but lags new SDKs, so the Dockerfile path is the safe one. **WebSockets** are proxied natively by Traefik (default proxy), so the SignalR circuit holds without special config. A **single container** is the default, which removes the sticky-session question. Agent ops are well covered: official **`coolify-cli` v1.6.2** (`deploy`, `app logs`, `app env list/sync`), a **token-based REST API**, and per-resource **deploy webhooks**. Docs are markdown-backed with a published **`llms.txt`**. The costs: Coolify **v4 is still labeled beta**, rollback is UI-only and does not cover CI/CD-pushed images, and *you* own OS patching, firewall hardening, Coolify upgrades, and backups. TLS is automatic (Traefik + Let's Encrypt, auto-renew).

#### 2. Fly.io (Runner-up — managed fallback)

The cheapest *managed* option (~$2–3.3/mo for a single always-on `shared-cpu-1x`), and a single machine sidesteps Fly's lack of session affinity. `flyctl` is a fully scriptable ops loop with a native MCP server. The gap vs. Coolify: it isn't self-hosted (so it doesn't satisfy the stated preference for owning the box), the generated `fly.toml` ships an `auto_stop_machines = "stop"` footgun that would suspend a Blazor circuit, and there were intermittent WebSocket proxy regressions reported in late 2025. Pick this if running and securing a VPS proves too much overhead.

#### 3. Railway (Second managed alternative)

Best agent docs (`llms-full.txt`) and an explicit **"no WebSocket idle timeout"** guarantee — the cleanest fit for a persistent Blazor circuit among the managed PaaS options. Default single replica, sticky sessions available when scaled. ~$5/mo on Hobby (mostly absorbed by the included credit). The gap: slightly pricier than Fly, MCP server is still beta, and like Fly it's a hosted platform rather than your own server.

## Anti-Bias Cross-Check: Coolify (self-hosted)

### Devil's Advocate — Weaknesses

1. **Self-hosting shifts the entire ops burden to a solo, after-hours developer** — OS patching, SSH/firewall hardening, Coolify upgrades, monitoring, and backups. For an app that stores user accounts and uploaded personal material (a PRD privacy guardrail), one unhardened box or open port is a real exposure with no managed-platform safety net.
2. **Coolify v4 is still beta.** You'd run production on beta control-plane software; a bad auto-update or migration can take down the *deploy pipeline itself*, and you own the recovery.
3. **Rollback is weak and not agent-friendly.** Coolify's rollback is UI-only and limited to locally-built images — it does **not** roll back CI/CD-pushed images. The scriptable-deploy story is asymmetric: deploy is clean, rollback is a manual retag.
4. **Single VPS = single point of failure.** If the box dies (provider incident, disk full from Docker image bloat, OOM during a .NET build), the app *and* the Coolify control plane go down together — there's no platform to fail over to.
5. **Min-spec trap.** Coolify itself uses ~1 GB; a 2 GB VPS has no headroom and .NET builds are memory-hungry, so builds can OOM-kill. You realistically need 4 GB (Hetzner CX22), and you should not co-locate a heavy Postgres on the same small box.

### Pre-Mortem — How This Could Fail

The developer stood up Coolify on a €4 Hetzner box, deployed 10xNotes via Dockerfile, and it ran great for the demo. Six months later it was a mess. The VPS was never hardened past defaults; an exposed service plus a never-rotated SSH setup made the box a target — and because user material lived on that same server, the privacy guardrail was at risk. Coolify auto-updated one night into a beta with a migration bug; the dashboard wouldn't load and a weekend vanished into SSH recovery instead of feature work. A .NET build OOM-killed on the 2 GB instance after a new NuGet package, and Docker's accumulated images filled the 30 GB disk, taking the app offline. When a bad deploy shipped, the rollback button only covered local UI builds, not the GitHub Actions image, so they hand-retagged under pressure. Backups were "planned but never automated," so a botched Postgres migration had no clean restore. Root error: a solo after-hours developer took on full sysadmin duty for a stateful app and under-resourced the box.

### Unknown Unknowns

- **Coolify v4 is beta and its own auto-updater can change behavior under you.** Disable or pin auto-update and read the changelog before upgrading, or the control plane is a moving target on production.
- **Traefik idle timeouts vs. a quiet circuit.** "WebSockets work" doesn't guarantee a long-*idle* SignalR connection survives — confirm Traefik's proxy read/idle timeouts are generous enough, or a quiet tab gets silently dropped and Blazor shows the reconnect bar.
- **Backups are 100% your job.** Coolify does not back up your app DB or its own config by default. A server-loss event with no off-box backup means total data loss — and 10xNotes promises to preserve user-owned material.
- **Docker image/volume accumulation silently fills small disks.** Coolify won't aggressively prune by default; schedule pruning or the box wedges within weeks.
- **Keep the database off the Coolify host.** Co-locating Postgres on a 2–4 GB box competes with .NET + Coolify for RAM; your "external providers are fine" answer is actually the safer architecture — an external managed Postgres (Neon/Supabase) means a server rebuild doesn't take the data with it.

## Operational Story

How the chosen platform actually operates day to day for this stack.

- **Preview deploys**: Coolify creates per-pull-request preview deployments when the GitHub app integration is connected (each PR gets its own URL on a subdomain). Previews share the single VPS's resources, so on a small box keep them few and short-lived; protect non-public previews with basic auth via a Traefik middleware label.
- **Secrets**: Env vars and secrets live per-resource inside Coolify (encrypted in Coolify's own DB on the VPS), editable via UI, REST API, or `coolify-cli app env`. They are injected into the container at deploy time. Rotation = update the value and redeploy. Whoever has the Coolify admin login or an API token can read them — so the API token is itself a high-value secret; store it in GitHub Actions secrets, not the repo.
- **Rollback**: Coolify's built-in rollback redeploys a **previous local image** from the UI — **it does not cover CI/CD-pushed images**. Practical path: tag every image with an immutable digest/tag in CI and redeploy a known-good tag via `coolify-cli`/webhook. Time-to-revert is one container restart (seconds–minute). Caveat: a rollback does **not** reverse EF Core database migrations — write migrations to be backward-compatible.
- **Approval**: A human should approve production publishes, Coolify version upgrades, secret rotation, and any database migration/drop. An agent may safely run unattended: build, deploy a new image tag to the existing app, tail logs, read deployment status, and list/sync non-secret env keys. Anything that mutates the VPS host (OS packages, firewall, disk) stays human.
- **Logs**: Build and runtime logs are readable read-only by the agent via `coolify-cli app logs <app>` and `coolify-cli app deployments logs -f`, or the REST API (`GET /deployments`, per-resource log endpoints). No dashboard click required for the read path.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| VPS not hardened → user data exposure | Devil's advocate / Pre-mortem | M | H | Firewall to 80/443/SSH only; key-only SSH; fail2ban; keep DB off-host; never expose Coolify dashboard publicly without auth |
| Coolify v4 beta auto-update breaks control plane | Devil's advocate / Unknown unknowns | M | H | Disable auto-update; snapshot VPS before each Coolify upgrade; read changelog; keep an off-box backup of Coolify config |
| CI/CD-pushed image can't be rolled back via UI | Devil's advocate / Research finding | M | M | Tag images immutably in CI; script "redeploy known-good tag" via `coolify-cli`/webhook; make EF migrations backward-compatible |
| Single VPS is a single point of failure | Devil's advocate | M | H | Automated daily off-box backups (DB + Coolify config); documented rebuild runbook; monitor disk/RAM; size VPS at 4 GB |
| .NET build OOM / disk fills on small box | Devil's advocate / Unknown unknowns | M | M | Use 4 GB VPS (CX22); build images in GitHub Actions, not on the host where feasible; schedule `docker image prune` |
| Traefik idle timeout silently drops quiet SignalR circuit | Unknown unknowns | L | M | Verify/raise Traefik read/idle timeouts; rely on Blazor's auto-reconnect; test a long-idle session before launch |
| No automated backups → data loss on server failure | Pre-mortem / Unknown unknowns | M | H | Configure Coolify scheduled backups to external storage from day one; restore-test once before launch |
| Self-host overhead exceeds solo after-hours capacity | Pre-mortem | M | M | Fly.io is the pre-vetted managed fallback (this file's runner-up) — migrate the same Dockerfile if ops cost grows |

## Getting Started

Version-accurate for .NET 10 + Coolify v4 (verified 2026-06-22). Co-locate nothing heavy on the box; use an external managed Postgres.

1. **Provision the box**: create a Hetzner CX22 (2 vCPU / 4 GB / 40 GB, ~€4–5/mo), Ubuntu LTS. Harden first: `ufw` allow 22/80/443 only, key-only SSH, create a non-root sudo user.
2. **Install Coolify**: `curl -fsSL https://cdn.coollabs.io/coolify/install.sh | sudo bash`. Point a DNS A-record at the VPS and let Coolify/Traefik auto-provision TLS via Let's Encrypt. In settings, **disable auto-update** so beta upgrades are deliberate.
3. **Add a `Dockerfile`** at the repo root — multi-stage, pinned to .NET 10:
   ```dockerfile
   FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
   WORKDIR /src
   COPY . .
   RUN dotnet publish 10xnotes.csproj -c Release -o /app
   FROM mcr.microsoft.com/dotnet/aspnet:10.0
   WORKDIR /app
   COPY --from=build /app .
   ENV ASPNETCORE_URLS=http://+:8080
   EXPOSE 8080
   ENTRYPOINT ["dotnet", "10xnotes.dll"]
   ```
   Set the app's exposed port to **8080** in Coolify (Kestrel must bind `0.0.0.0` via `ASPNETCORE_URLS`, not localhost).
4. **Create the application** in Coolify: connect the GitHub repo (mati-ck/10xDevs3), build pack = Dockerfile, branch `main` for auto-deploy-on-merge (matches the `ci_default_flow` hint). Add env vars/secrets (DB connection string to external Postgres, AI API key) under the resource's Environment tab.
5. **Wire agent ops**: create an API token (Settings → API Tokens), store it in GitHub Actions secrets, and install `coolify-cli` (v1.6.2+) in CI for `deploy` / `app logs`. Verify a long-idle Blazor session keeps its circuit before announcing launch.

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration (a starter Dockerfile is sketched above only as a getting-started step)
- CI/CD pipeline setup (GitHub Actions workflow authoring)
- Production-scale architecture (multi-server Coolify, multi-region, HA/DR, horizontal scaling with sticky sessions)
