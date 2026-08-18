# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Enable RLS in the same migration that creates the table — tooling tables included

- **Context**: Any migration adding a table to `public` on Supabase — including tables created for you by tooling (EF Core's `__EFMigrationsHistory`, and any future framework-managed table).
- **Problem**: Supabase exposes the `public` schema through the Data API and the anon key is public by design, so a table without RLS is readable and writable by anyone. In F-01 the app's own table was hardened but EF's `__EFMigrationsHistory` was not — verified: the anon key could delete rows from the migration ledger and break the next deploy.
- **Rule**: Every table created in `public` must `ENABLE ROW LEVEL SECURITY` in the same migration that creates it — including tables your tooling creates for you. Deny-all (RLS on, zero policies) is the correct default when access is enforced in application code, because the app's `postgres` role bypasses RLS. After any DDL, re-run the Supabase security advisor to confirm nothing was missed.
- **Applies to**: all

## An advertised limit must be one every layer beneath it can carry

- **Context**: Any user-facing bound the application states or enforces — a length limit, a file size, a row count — where the value then crosses a transport, a serializer, or a column with a bound of its own. In S-01c the note editor advertised 64 KB (`NoteValidator.MaxContentLength`, shown in the character counter and in both pages' Polish copy) while Blazor Server's untouched SignalR default accepted 32 KB.
- **Problem**: The layers disagree silently, and the failure lands past the point where the application can handle it. Exceeding `MaximumReceiveMessageSize` does not return an error a component can catch — SignalR aborts the connection, so the user gets a reconnect modal and loses work that was never saved. Verified: 32768 against 65536, exactly half. The failure shape is the worst available — a long note renders correctly and only dies when the user touches it.
- **Rule**: When you set a user-facing limit, name every layer the value must cross and check each one's own bound. Derive the lower bounds from the advertised constant rather than setting them independently, and pin the relationship in a test — a comment cannot fail. Watch for unit mismatches while doing it: a character count is not a byte count.
- **Applies to**: all
