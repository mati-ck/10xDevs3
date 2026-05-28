---
bootstrapped_at: 2026-05-28T19:56:44Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: 10xnotes
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "dotnet list package --vulnerable --include-transitive"
---

## Hand-off

Verbatim copy of `context/foundation/tech-stack.md`.

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: 10xnotes
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: self-host
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: true
  has_background_jobs: false
```

### Why this stack

Solo developer shipping a note-generation web app in 3 weeks of after-hours work, with account-based auth and an AI generation step. The user chose the .NET language family at the opening question and accepted the recommended default for the `(web, dotnet)` cell: ASP.NET Core. It clears all four agent-friendly gates (typed, convention-based, popular within .NET training data, well-documented) and its bootstrapper confidence is verified, so scaffolding will be smooth. The auth and AI feature flags are set from the PRD's functional requirements; payments, realtime, and background jobs are out of scope per the PRD non-goals. Deployment targets self-host (chosen over the card's Azure App Service default); CI runs on GitHub Actions with auto-deploy-on-merge, the standard shape for a solo project. Intended UI direction: Blazor Web App (`dotnet new blazor`) — first-party, typed, well-documented C# full-stack UI, the strongest single-language fit for a 3-week solo build (preferred over the now-removed first-party React/Angular SPA templates and over niche community NuGet SPA templates that fail the popularity/docs agent-friendly gates). The registry's `dotnet` card scaffolds the API-first `webapi` template, so the bootstrapper will need a manual swap to the Blazor template (or addition of a Blazor project) — this is the one known friction point in the hand-off.

## Pre-scaffold verification

| Signal      | Value                                                          | Severity | Notes                                                            |
| ----------- | -------------------------------------------------------------- | -------- | ---------------------------------------------------------------- |
| npm package | not run                                                        | —        | non-JS starter (`cmd_template` is `dotnet new`, no npm CLI)      |
| GitHub repo | not run                                                        | —        | `docs_url` is `https://learn.microsoft.com/aspnet/core` — not a GitHub URL; no recency signal available |

Local toolchain present: `dotnet` 10.0.107.

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 6 — `10xnotes.csproj`, `10xnotes.http`, `Program.cs`, `Properties/launchSettings.json`, `appsettings.Development.json`, `appsettings.json`
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (`dotnet new webapi` ships no `.gitignore`); cwd `.gitignore` preserved untouched
**.bootstrap-scaffold cleanup**: deleted

**Project-name normalization**: `dotnet new` derives the project/assembly name from `-n`, so the temp-dir name produced `.bootstrap-scaffold.csproj` (with `<RootNamespace>_bootstrap_scaffold</RootNamespace>`) and `.bootstrap-scaffold.http` (with `@_bootstrap_scaffold_HostAddress`). Both were renamed to the hand-off `project_name` `10xnotes` before move-up: `10xnotes.csproj` (`<RootNamespace>_10xnotes</RootNamespace>` — leading digit sanitized to a valid C# identifier) and `10xnotes.http` (`@_10xnotes_HostAddress`). `Program.cs` uses top-level statements with no namespace declaration and needed no change.

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: 0/0/0/0 direct of total 0/0/0/0 — `--include-transitive` was passed; the project has no vulnerable packages, direct or transitive.

Tool output: `The given project '10xnotes' has no vulnerable packages given the current sources.` (`dotnet restore` was run first to populate the project assets the audit reads; restore succeeded.)

#### CRITICAL findings
none

#### HIGH findings
none

#### MODERATE findings
none

#### LOW / INFO findings
none

Single direct dependency in the scaffold: `Microsoft.AspNetCore.OpenApi` 10.0.7.

## Hints recorded but not acted on

| Hint                    | Value             |
| ----------------------- | ----------------- |
| bootstrapper_confidence | verified          |
| quality_override        | false             |
| path_taken              | standard          |
| self_check_answers      | null              |
| team_size               | solo              |
| deployment_target       | self-host         |
| ci_provider             | github-actions    |
| ci_default_flow         | auto-deploy-on-merge |
| has_auth                | true              |
| has_payments            | false             |
| has_realtime            | false             |
| has_ai                  | true              |
| has_background_jobs     | false             |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep. (This run created none.)
- Address audit findings per your project's risk tolerance — the full breakdown is in this log. (This run found none.)

Known friction point from the hand-off, surfaced but not acted on by the bootstrap run itself:
- The `dotnet` card scaffolds the API-first `webapi` template, but the intended UI direction is **Blazor Web App** (`dotnet new blazor`).
- The scaffold ships no `.gitignore`; build artifacts (`bin/`, `obj/`) are not ignored.

## Post-bootstrap manual changes

Done after the bootstrap run, at the user's request:

- **Blazor swap (resolved).** Replaced the `webapi` scaffold with a **Blazor Web App** (`dotnet new blazor -n 10xnotes -o .blazor-scaffold`, default Server interactivity), then swapped the files into cwd: removed the webapi files (`Program.cs`, `appsettings*.json`, `10xnotes.csproj`, `10xnotes.http`, `Properties/`) and stale `bin/`+`obj/`, moved the Blazor sources up (`Components/`, `wwwroot/`, `Program.cs`, `Properties/`, `appsettings*.json`, `10xnotes.csproj`), removed the temp dir. Root namespace `_10xnotes` is consistent across the project. `dotnet build` succeeds (0 warnings, 0 errors). Re-audit: 0 vulnerable packages.
- **`.gitignore` (resolved).** Generated the standard .NET ignore file (`dotnet new gitignore`) and append-merged it into the existing `.gitignore`, preserving the pre-existing `.claude/settings.local.json` line at the top under a `# ---- .NET (from dotnet new gitignore) ----` separator. `bin/` and `obj/` are now ignored.
