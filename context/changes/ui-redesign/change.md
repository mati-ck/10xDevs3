---
change_id: ui-redesign
title: UI redesign
status: implementing
created: 2026-09-11
updated: 2026-09-11
archived_at: null
---

## Notes

Visual redesign of the whole app from the Claude Design mockups (dark-only, modern SaaS, violet accent).

- Design source of truth: `design/README.md` (locked decisions, tokens, artboard → route map) and the `design/*.dc.html` artboards.
- Canvas: https://claude.ai/code/artifact/4ebbcf9c-d18c-44f6-b33b-fa92318f7615
- Process: run end-to-end with subagents; the user asked the orchestrator to make decisions without stopping after the plan.
- Git: one local commit per implementation phase on `feature/ui-redesign`, no push.
