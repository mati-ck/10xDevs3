using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10xnotes.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistenceBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profiles_owner_id",
                table: "profiles",
                column: "owner_id",
                unique: true);

            // Two things EF cannot express, both hand-written:
            //
            // 1. A foreign key into the "auth" schema, which this context does not manage.
            //    Supabase Auth owns identity, so auth.users is the identity store.
            //
            // 2. Row Level Security with NO policies. The "public" schema is exposed through
            //    Supabase's Data API, so without this anyone holding the project's anon key
            //    could read this table. The anon/authenticated roles do not bypass RLS, while
            //    the app's postgres role does (rolbypassrls = true) — so this closes the Data
            //    API completely and is invisible to EF Core. Policies are intentionally absent:
            //    per-user access control is enforced by the global query filter in AppDbContext.
            migrationBuilder.Sql("""
                ALTER TABLE public.profiles
                  ADD CONSTRAINT fk_profiles_owner_id_auth_users
                  FOREIGN KEY (owner_id) REFERENCES auth.users (id) ON DELETE CASCADE;

                ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.profiles
                  DROP CONSTRAINT IF EXISTS fk_profiles_owner_id_auth_users;
                """);

            migrationBuilder.DropTable(
                name: "profiles");
        }
    }
}
