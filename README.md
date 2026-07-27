# 10xDevs3

10xNotes — a .NET 10 Blazor Web App (Interactive Server) that turns long source material
into an AI-drafted note the user edits and accepts. See `context/foundation/prd.md`.

## Running locally

```bash
dotnet tool restore   # pins dotnet-ef (see .config/dotnet-tools.json)
dotnet run            # http://localhost:5125 / https://localhost:7111
```

## Configuration

The app requires one setting: **`ConnectionStrings__Postgres`** — the Supabase Postgres
connection string. It is never committed; `appsettings.json` carries an empty placeholder
only to document the key.

Use the Supabase **session pooler** (`aws-<region>.pooler.supabase.com`, port `5432`,
username `postgres.<project-ref>`). The direct connection (`db.<ref>.supabase.co:5432`) is
IPv6-only without the paid IPv4 add-on, and the transaction pooler (port `6543`) does not
support prepared statements, which Npgsql uses by default.

Local development:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=aws-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<db-password>;SSL Mode=Require;Trust Server Certificate=true"
```

Deployment: set `ConnectionStrings__Postgres` (double underscore) as an environment
variable on the Coolify resource.

## Health endpoints

| Endpoint | Reports | Probed by |
| --- | --- | --- |
| `/health` | process liveness only — never touches the database | `Dockerfile` HEALTHCHECK, `deploy.yml` |
| `/health/ready` | readiness, including database reachability | humans, manual verification |

Liveness deliberately excludes the database: a failing `/health` makes Coolify stop routing
to the container.
