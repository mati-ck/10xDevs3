---
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
---

## Why this stack

Solo developer shipping a note-generation web app in 3 weeks of after-hours work, with account-based auth and an AI generation step. The user chose the .NET language family at the opening question and accepted the recommended default for the `(web, dotnet)` cell: ASP.NET Core. It clears all four agent-friendly gates (typed, convention-based, popular within .NET training data, well-documented) and its bootstrapper confidence is verified, so scaffolding will be smooth. The auth and AI feature flags are set from the PRD's functional requirements; payments, realtime, and background jobs are out of scope per the PRD non-goals. Deployment targets self-host (chosen over the card's Azure App Service default); CI runs on GitHub Actions with auto-deploy-on-merge, the standard shape for a solo project. Intended UI direction: Blazor Web App (`dotnet new blazor`) — first-party, typed, well-documented C# full-stack UI, the strongest single-language fit for a 3-week solo build (preferred over the now-removed first-party React/Angular SPA templates and over niche community NuGet SPA templates that fail the popularity/docs agent-friendly gates). The registry's `dotnet` card scaffolds the API-first `webapi` template, so the bootstrapper will need a manual swap to the Blazor template (or addition of a Blazor project) — this is the one known friction point in the hand-off.
