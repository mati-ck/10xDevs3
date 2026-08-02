---
change_id: email-password-auth
title: F-02 — uwierzytelnianie e-mail + hasło i ochrona tras
status: implementing
created: 2026-07-27
updated: 2026-07-27
archived_at: null
---

## Notes

F-02 z @roadmap.md

### Risk accepted (Phase 1, 2026-07-27): DataProtection key ring is unencrypted at rest

`PersistKeysToDbContext` stores the key ring as plaintext XML in `public.data_protection_keys`.
Startup logs `XmlKeyManager[35] No XML encryptor configured` when a key is created.

Accepted for the MVP because: RLS blocks the Supabase Data API (verified — the anon key reads
`[]` while a key row exists), so reading the keys requires the Postgres connection string, which
already grants full read/write access to every user's data. Encrypting the key ring would not
shrink the blast radius of leaking that credential.

Consequence if it leaks anyway: an attacker could forge an authentication cookie for any user
without knowing a password. **Revisit before real users** — the fix is `ProtectKeysWithCertificate`,
which needs certificate generation, secure delivery into the container, and a rotation story.

### Unreproduced observation (Phase 4, 2026-08-02): logout ignores the first click

While verifying gate 4.7 against the deployed app, the logout button repeatedly did nothing on the
first click after landing on `/logout` — the POST arrived only on the second or third attempt. The
network panel showed `GET /logout` alone, with no `POST`, so the form was not submitting rather
than the request failing. Logout itself is correct: once the POST goes through it returns 200 and
redirects to `/login`.

**Not reproduced afterwards** — a later attempt to trigger it deliberately failed, so this is
recorded as an observation, not a confirmed defect. Deliberately kept out of
`context/foundation/lessons.md`, which is reserved for rules established well enough to act on.

Working hypothesis: a window before Blazor's enhanced form handling attaches to the statically
rendered `/logout` page, during which the click hits an element that is not wired up yet. If it
resurfaces, the thing to capture is whether `blazor.web.js` had finished loading at click time —
that would confirm or kill the hypothesis in one observation. User-visible symptom would be
"I clicked Wyloguj and nothing happened", which reads as a broken session rather than a timing bug.
