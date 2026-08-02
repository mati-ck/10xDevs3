# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Enable RLS in the same migration that creates the table — tooling tables included

- **Context**: Any migration adding a table to `public` on Supabase — including tables created for you by tooling (EF Core's `__EFMigrationsHistory`, and any future framework-managed table).
- **Problem**: Supabase exposes the `public` schema through the Data API and the anon key is public by design, so a table without RLS is readable and writable by anyone. In F-01 the app's own table was hardened but EF's `__EFMigrationsHistory` was not — verified: the anon key could delete rows from the migration ledger and break the next deploy.
- **Rule**: Every table created in `public` must `ENABLE ROW LEVEL SECURITY` in the same migration that creates it — including tables your tooling creates for you. Deny-all (RLS on, zero policies) is the correct default when access is enforced in application code, because the app's `postgres` role bypasses RLS. After any DDL, re-run the Supabase security advisor to confirm nothing was missed.
- **Applies to**: all
