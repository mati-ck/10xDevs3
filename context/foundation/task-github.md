# Roadmap → GitHub Issues — Migration Plan (executed)

> Migration of `context/foundation/roadmap.md` to GitHub Issues.
> **Executed 2026-07-02** — 10 issues (#6–#15) live on `mati-ck/10xDevs3`, milestone `MVP` (#1).
> Generated: 2026-07-02.

## Created issues (ID → number)

| Roadmap ID | Issue | Roadmap ID | Issue |
| --- | --- | --- | --- |
| F-01 | [#6](https://github.com/mati-ck/10xDevs3/issues/6) | S-04 | [#11](https://github.com/mati-ck/10xDevs3/issues/11) |
| F-02 | [#7](https://github.com/mati-ck/10xDevs3/issues/7) | S-05 | [#12](https://github.com/mati-ck/10xDevs3/issues/12) |
| S-01 | [#8](https://github.com/mati-ck/10xDevs3/issues/8) | S-06 | [#13](https://github.com/mati-ck/10xDevs3/issues/13) |
| S-02 | [#9](https://github.com/mati-ck/10xDevs3/issues/9) | OQ1 | [#14](https://github.com/mati-ck/10xDevs3/issues/14) |
| S-03 | [#10](https://github.com/mati-ck/10xDevs3/issues/10) | OQ2 | [#15](https://github.com/mati-ck/10xDevs3/issues/15) |

## Task management system

**GitHub Issues** on `mati-ck/10xDevs3` (private repo, Issues enabled).

- `gh` 2.95.0, authenticated as `mati-ck`, token scopes: `gist`, `read:org`, `repo`, `workflow`.
- **Constraint:** the token has **no `project` scope**, so a native **GitHub Projects** board (with dependency/status fields) cannot be created without `gh auth refresh -s project`. This plan uses **Issues + labels + milestone**; dependencies are expressed textually in issue bodies (`Depends on #N` / `Blocked by #N`).

## Decisions (approved)

| Decision | Choice |
| --- | --- |
| Scope | 8 roadmap issues (F-01, F-02, S-01…S-06) **+ 2 decision issues** (OQ1, OQ2) |
| Grouping | Single milestone `MVP` + full labels |
| Language | **English** titles and bodies |

## Labels to create

| Label | Color | Applies to |
| --- | --- | --- |
| `foundation` | `5319e7` | F-01, F-02 |
| `slice` | `1d76db` | S-01…S-06 |
| `status:ready` | `0e8a16` | F-01 |
| `status:proposed` | `fbca04` | F-02, S-01, S-02, S-03, S-05 |
| `status:blocked` | `d73a4a` | S-04, S-06 |
| `stream:A` | `c5def5` | F-01, F-02, S-01 |
| `stream:B` | `bfd4f2` | S-02 |
| `stream:C` | `d4c5f9` | S-03, S-04, S-05, S-06 |
| `north-star` | `e4b400` | S-01 |
| `decision-needed` | `d93f0b` | OQ1, OQ2 |

## Milestone

- **`MVP`** — First MVP roadmap; groups all 8 roadmap items (F/S) plus the 2 decision issues.

## Issue set (10)

Creation order is topological so `Depends on #N` resolves to already-created numbers.
Cross-references below use roadmap IDs; actual `#N` numbers are substituted at creation time (two-pass: create → capture numbers → edit bodies).

### 1. `[F-01] Per-user persistent data layer (persistence-baseline)` → #6
- **Labels:** `foundation`, `status:ready`, `stream:A` · **Milestone:** MVP
- **Outcome:** (foundation) Database connected (external managed Postgres per `infrastructure.md`), EF Core + migrations working, records can be scoped and queried per user. Minimal persistence contract — not the full data model.
- **PRD refs:** NFR (durability: data survives re-login), NFR (privacy: per-user isolation mechanism)
- **Prerequisites:** — (ready to start)
- **Unlocks:** F-02 (#7), S-01 (#8)
- **Parallel with:** —
- **Unknowns:** —
- **Risk:** Sequenced first — auth (F-02) and note saving (S-01) don't exist without a store; risk is over-scope (build only connection + migrations + isolation, not domain entities — those land in S-01).
- **Status:** `ready`
- **Footer:** ▶ Next: `/10x-plan persistence-baseline` · Source: `context/foundation/roadmap.md`

### 2. `[F-02] Email + password authentication (email-password-auth)` → #7
- **Labels:** `foundation`, `status:proposed`, `stream:A` · **Milestone:** MVP
- **Outcome:** (foundation) User can register, log in, and log out; app recognizes the signed-in user and protects routes so an unauthenticated visitor sees no data. Flat model, no roles.
- **PRD refs:** FR-001, FR-002, Access Control
- **Prerequisites:** Depends on F-01 (#6)
- **Unlocks:** S-01 (#8), S-03 (#10); enforces the privacy NFR
- **Parallel with:** —
- **Unknowns:** —
- **Risk:** Sequenced right after persistence — privacy (NFR) gates launch and the north star requires "save to account"; risk is growth toward a full role system (keep the PRD's flat model).
- **Status:** `proposed`
- **Footer:** ▶ Next: `/10x-plan email-password-auth`

### 3. `[S-01] Import Markdown → AI generation → review → save (markdown-import-generation)` ★ north star → #8
- **Labels:** `slice`, `status:proposed`, `stream:A`, `north-star` · **Milestone:** MVP
- **Outcome:** User imports a Markdown file as source material, generates an AI note beside it with one click, can edit it and save it (save = acceptance); the source material stays unchanged.
- **PRD refs:** US-01, FR-004, FR-005, FR-006, FR-008, NFR (visible progress feedback >2s)
- **Prerequisites (Depends on):** F-01 (#6), F-02 (#7)
- **Parallel with:** —
- **Unknowns:** —
- **Risk:** The product's riskiest assumption (AI generation quality clearing 75% acceptance) is settled here; deliver only the minimal loop (paste goes separately in S-02) so validation arrives fast under the after-hours budget.
- **Status:** `proposed`
- **Footer:** ▶ Next: `/10x-plan markdown-import-generation`

### 4. `[S-02] Paste text → AI generation (paste-text-generation)` → #9
- **Labels:** `slice`, `status:proposed`, `stream:B` · **Milestone:** MVP
- **Outcome:** User pastes raw text as source material and generates a note from it via the same loop as S-01.
- **PRD refs:** FR-003
- **Prerequisites (Depends on):** S-01 (#8)
- **Parallel with:** S-03 (#10), S-04 (#11), S-05 (#12), S-06 (#13)
- **Unknowns:** —
- **Risk:** Second entry into the existing generation loop — low risk; only pitfall is treating paste as a separate flow rather than an input variant of S-01.
- **Status:** `proposed`
- **Footer:** ▶ Next: `/10x-plan paste-text-generation`

### 5. `[S-03] Browse own notes and source materials (browse-notes-and-sources)` → #10
- **Labels:** `slice`, `status:proposed`, `stream:C` · **Milestone:** MVP
- **Outcome:** User sees a list of their saved notes and source materials and can open one; cannot see others' data.
- **PRD refs:** FR-007, NFR (privacy)
- **Prerequisites (Depends on):** S-01 (#8), F-02 (#7)
- **Parallel with:** S-02 (#9), S-04 (#11), S-05 (#12), S-06 (#13)
- **Unknowns:** Should source material be a separate top-level list, or available mainly beside its note? — Owner: user. Block: no. (PRD leaves this "to be decided in design" for FR-007.)
- **Risk:** Sequenced after S-01 — nothing to browse without saved notes; risk is view growth toward search/filters outside MVP.
- **Status:** `proposed`
- **Footer:** ▶ Next: `/10x-plan browse-notes-and-sources`

### 6. `[S-04] Edit source material (edit-source-material)` → #11
- **Labels:** `slice`, `status:blocked`, `stream:C` · **Milestone:** MVP
- **Outcome:** User edits a previously saved source material.
- **PRD refs:** FR-009
- **Prerequisites (Depends on):** S-01 (#8)
- **Blocked by:** OQ1 (#14)
- **Parallel with:** S-02 (#9), S-03 (#10), S-05 (#12), S-06 (#13)
- **Unknowns:** Is source material editable after a note is generated, and if so — what happens to the existing note (kept / marked stale / regenerated)? — Owner: user. Block: yes. (PRD Open Question 1.)
- **Risk:** Edit behavior depends directly on the unresolved question of note fidelity vs a changed source; planning before the decision risks rework.
- **Status:** `blocked`

### 7. `[S-05] Delete a note (delete-note)` → #12
- **Labels:** `slice`, `status:proposed`, `stream:C` · **Milestone:** MVP
- **Outcome:** User deletes one of their notes; the source material stays intact.
- **PRD refs:** FR-010
- **Prerequisites (Depends on):** S-01 (#8)
- **Parallel with:** S-02 (#9), S-03 (#10), S-04 (#11), S-06 (#13)
- **Unknowns:** —
- **Risk:** Simple single-note operation, no cascade (the note is deleted, not the source) — lowest risk in the content lifecycle.
- **Status:** `proposed`
- **Footer:** ▶ Next: `/10x-plan delete-note`

### 8. `[S-06] Delete source material (delete-source-material)` → #13
- **Labels:** `slice`, `status:blocked`, `stream:C` · **Milestone:** MVP
- **Outcome:** User deletes a source material.
- **PRD refs:** FR-011
- **Prerequisites (Depends on):** S-01 (#8)
- **Blocked by:** OQ2 (#15)
- **Parallel with:** S-02 (#9), S-03 (#10), S-04 (#11), S-05 (#12)
- **Unknowns:** What happens to notes linked to a source material when it is deleted (cascade delete vs orphaning the note)? — Owner: user. Block: yes. (PRD Open Question 2.)
- **Risk:** Cascade behavior is unresolved; a wrong default (cascade vs orphan) could irreversibly delete accepted notes, so the slice waits for the decision.
- **Status:** `blocked`

### 9. `[OQ1] Decision: is source material editable after note generation?` → #14
- **Labels:** `decision-needed` · **Milestone:** MVP
- **Question:** Is source material editable after a note is generated, and if so what happens to the existing note (kept / marked stale / regenerated)?
- **Owner:** user · **Origin:** Socratic round on FR-009 (PRD Open Question 1)
- **Blocks:** S-04 (#11) — resolving this promotes S-04 from `blocked` to actionable.

### 10. `[OQ2] Decision: notes' fate when their source material is deleted?` → #15
- **Labels:** `decision-needed` · **Milestone:** MVP
- **Question:** When a source material is deleted, do its linked notes cascade-delete or become orphaned?
- **Owner:** user · **Origin:** Socratic round on FR-011 (PRD Open Question 2)
- **Blocks:** S-06 (#13) — resolving this promotes S-06 from `blocked` to actionable.

## Dependency map (live issue numbers)

```
F-01 #6 ──unlocks──▶ F-02 #7 ──▶ S-01 #8 ★
   └──unlocks──────────────────▶ S-01 #8 ★
                                   │
        ┌──────────┬──────────────┼──────────────┬──────────────┐
        ▼          ▼              ▼              ▼              ▼
     S-02 #9    S-03 #10      S-04 #11       S-05 #12      S-06 #13
                            (blocked ← #14)              (blocked ← #15)
                             OQ1 #14 ▲                    OQ2 #15 ▲
```

## Execution approach (gh CLI)

1. Create the 10 labels (`gh label create --force`, idempotent).
2. Create the `MVP` milestone (`gh api repos/{owner}/{repo}/milestones`), skip if it already exists.
3. **Pass 1** — create all 10 issues in topological order with labels + milestone set at creation; bodies carry `@@ROADMAP-ID@@` placeholders for cross-references. Capture the resulting issue number for each roadmap ID.
4. **Pass 2** — substitute each `@@ROADMAP-ID@@` with the real `#N` and `gh issue edit <N> --body-file` so `Depends on` / `Blocked by` / `Unlocks` point at live issues.
5. Print a summary table (roadmap ID → issue #, URL).

**Nothing is destructive**: only label/milestone/issue creation and body edits on the freshly created issues. No existing issues, labels, or milestones are modified (default repo labels are left untouched).

## Verification after run — ✅ passed 2026-07-02

- `gh issue list --milestone MVP` → **10 issues** ✅
- `gh issue list --label status:ready` → **#6 (F-01) only** ✅
- `gh issue list --label north-star` → **#8 (S-01) only** ✅
- `gh issue list --label status:blocked` → **#11 (S-04), #13 (S-06)** ✅
- `gh issue list --label decision-needed` → **#14 (OQ1), #15 (OQ2)** ✅
- Cross-refs verified live: #6 Unlocks → #7, #8; #11 Blocked by → #14.

> Note: a first `sed`-based body substitution (BSD/macOS) briefly pushed empty bodies; caught and corrected with a `perl` pass — all 10 bodies now carry full content with resolved `#N` cross-references.

## Later additions (outside the 2026-07-02 migration)

Issues opened after the migration run. Same conventions: English body, `MVP` milestone, labels from the table above.

| Change ID | Issue | Origin |
| --- | --- | --- |
| `auth-session-hardening` | [#22](https://github.com/mati-ck/10xDevs3/issues/22) | Carry-forward from the F-01 and F-02 implementation reviews (2026-08-02). Closed and archived 2026-08-02 → `context/archive/2026-08-02-auth-session-hardening/` |

### Post-migration edits to existing issues

| Date | Issue | Edit |
| --- | --- | --- |
| 2026-08-13 | [#8](https://github.com/mati-ck/10xDevs3/issues/8) (S-01) | Change ID renamed `markdown-import-generation` → `import-generation-review-save` to match the change folder; title and body updated, `status:proposed` → `status:ready` (prerequisites #6 and #7 both closed). The §"Issue set" entry above records the original migration state and is left as-is. |

## Issue hygiene convention

- **Closing a change closes its issue** — verified on #6 (F-01) and #7 (F-02).
- **A closed issue carries no `status:*` label.** `status:*` describes work in flight; once the issue is closed the state lives in `state: CLOSED`, and a leftover `status:ready` reads as "still actionable" in `gh issue list --label status:ready`. #7 kept `status:ready` after closing and was corrected on 2026-08-02; #6 was already clean.
- Check with `gh issue list --state closed --json number,labels` after archiving a change.

## Not created (out of scope of current token)

- **GitHub Projects board** — token lacks `project` scope. To add a board with status/dependency fields: `gh auth refresh -s project`, then create a Project and add issues #6–#15.
