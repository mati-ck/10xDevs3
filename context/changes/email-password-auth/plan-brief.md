# F-02 Email + Password Authentication — Plan Brief

> Full plan: `context/changes/email-password-auth/plan.md`

## What & Why

10xNotes has a per-user data layer but no users. F-02 adds registration, login, and logout backed by Supabase Auth, plus route protection, so the PRD's privacy guardrail ("one user's material is never visible to another") stops being theoretical. It also repairs the two identity defects F-01 knowingly deferred. Without it, S-01 — the north star, where saving a note *is* the acceptance signal — has no account to save against.

## Starting Point

F-01 shipped `public.profiles` keyed to `auth.users`, an owner-scoping query filter, and 6 passing isolation tests — but nothing authenticates. `Program.cs` has no authentication or authorization, `Components/Routes.razor:1-7` has a bare router, and the current-user accessor reads from `IHttpContextAccessor`, which is invalid across a Blazor Server circuit. Supabase's GoTrue is provisioned with **0 users** and currently requires email confirmation. DataProtection keys are not persisted in the container — irrelevant until now, fatal for cookie auth.

## Desired End State

A visitor registers with email and password, logs in, and logs out. Logged out, only `/login` and `/register` are reachable and no app data is exposed. Logged in, the app knows who they are on both the server-rendered and interactive paths, and every query is scoped to them without any call site asking. Sessions survive a deploy.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Email confirmation | Turn auto-confirm ON in Supabase | The PRD never asks for verification, and free-tier SMTP reliably delivers only to team members — otherwise registration dead-ends. |
| GoTrue integration | Typed `HttpClient` over the REST API | We need two endpoints and discard the JWT anyway, so the SDK's session model buys nothing. |
| Session carrier | ASP.NET cookie; Supabase tokens discarded | One session concept, native to `AuthenticationStateProvider`, `[Authorize]`, and antiforgery. |
| Auth page rendering | Static SSR with form POST | `SignInAsync` needs an unstarted response, which an interactive circuit cannot provide. |
| Profile creation | Postgres trigger on `auth.users` | Canonical Supabase pattern — an account cannot exist without a profile, whatever creates it. |
| Identity source | `AuthenticationStateProvider` + `AddDbContextFactory` | Fixes both F-01 defects at once: identity stays valid across the circuit *and* is read per operation. |
| DataProtection keys | Persist to Postgres via EF | Uses infrastructure we already back up; no Coolify volume to forget on a rebuild. |
| Route protection | Deny by default, allow-list public routes | A page added by a future slice is protected automatically — fail-closed, like F-01's query filter. |
| Error messages | Generic everywhere | No enumeration surface; registration copy points at recovery without confirming an address exists. |
| Session lifetime | 14 days, sliding, no "remember me" | Serves the PRD's returnability goal without a checkbox nobody thinks about. |
| Password rules | Minimum 8 characters, no composition rules | Above Supabase's 6-character default and aligned with current guidance favouring length. |
| Testing | Unit-test the seams; verify flows manually | Covers what breaks silently — especially identity → query filter — without a browser harness. |

## Scope

**In scope:** DataProtection keys in Postgres (+RLS) · cookie auth scheme · `ICurrentUserAccessor` reworked onto `AuthenticationStateProvider` · `AddDbContextFactory` · typed GoTrue client · register/login/logout (static SSR, Polish copy) · `handle_new_user` trigger · deny-by-default authorization with allow-list · `AuthorizeRouteView` · auth-aware nav · unit tests · live deploy verification.

**Out of scope:** roles/permissions · password reset, email change, account deletion · email confirmation flow · OAuth · Supabase session persistence · domain entities (S-01) · RLS policies · end-to-end browser tests · removing the demo pages.

## Architecture / Approach

```
/register, /login  (static SSR forms — HttpContext still writable)
        │
        ├─► SupabaseAuthClient ──► GoTrue /auth/v1/signup | /token
        │         │                        │
        │         │                        └─► auth.users  ──trigger──► public.profiles
        │         ▼
        │   classified result (never raw GoTrue text)
        ▼
   HttpContext.SignInAsync → cookie { sub = auth.users.id, email }   [Supabase JWT discarded]
        │
        ▼
 AuthenticationStateProvider ──► ICurrentUserAccessor ──► UserScopedDbContextFactory
                                                                │
                                                                ▼
                                          AppDbContext.CurrentUserId → global query filter
```

Supabase Auth is a credential store only. The cookie is the session, and `OwnerId` is already `auth.users.id` from F-01, so no identity translation is needed anywhere.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Auth foundation | Keys in Postgres, cookie scheme, identity rework, context factory | The `ICurrentUserAccessor` interface change ripples into F-01's tests |
| 2. Supabase integration | GoTrue client, register/login/logout, profile trigger | Two non-transactional systems; `SECURITY DEFINER` function needs a pinned `search_path` |
| 3. Route protection & UI | Deny-by-default policy, `AuthorizeRouteView`, auth-aware nav | A fallback policy that catches `/health` de-routes the container |
| 4. Tests & deploy | Seam tests, live verification | Cookie/redirect behavior is only manually verified |

**Prerequisites:** auto-confirm enabled in Supabase Auth settings (before Phase 2 verification); `ConnectionStrings__Postgres` already set in Coolify; F-01 merged.
**Estimated effort:** ~3–4 after-hours sessions; Phase 2 is the largest.

## Open Risks & Assumptions

- **The allow-list is the single most dangerous line in this change.** If the fallback policy reaches `/health`, the Docker HEALTHCHECK stops seeing `Healthy` and Coolify de-routes a perfectly working app — the outage already recorded in the deploy runbook. Phase 3's automated criteria check this explicitly.
- **Auto-confirm means nobody's email is verified.** Accounts can be created with addresses the registrant doesn't own; revisit before real users and before any password-reset feature.
- **Generic error copy costs usability.** Someone re-registering an existing address gets a message that cannot explain why; the copy is written to point at login without confirming existence.
- **Profile creation is invisible from C#.** The trigger is the canonical Supabase pattern, but a developer debugging missing profiles will find nothing in the codebase — hence the explicit pointer in the plan and the migration comment.
- **`ICurrentUserAccessor` becomes async**, a breaking change to a contract F-01 shipped and tested; the test stub changes with it.
- **`AddDbContextCheck` assumes a resolvable `AppDbContext`**, which the move to a factory disturbs — `/health/ready` must keep reporting database reachability either way.

## Success Criteria (Summary)

- A new visitor can register, log in, and log out; the account exists in `auth.users` with a matching `profiles` row.
- Logged out, every app route redirects to login while `/health` still returns the literal body `Healthy`.
- A logged-in user's queries return their own rows and no one else's — verified at the data layer, not just the UI — and the session survives a redeploy.
