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
