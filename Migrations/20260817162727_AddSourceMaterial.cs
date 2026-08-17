using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10xnotes.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceMaterial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_materials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_source_materials_owner_id",
                table: "source_materials",
                column: "owner_id");

            // The same two things EF cannot express that InitialPersistenceBaseline had to
            // hand-write, for the same reasons:
            //
            // 1. A foreign key into the "auth" schema, which this context does not manage.
            //    ON DELETE CASCADE so deleting the account takes its materials with it.
            //
            // 2. Row Level Security with NO policies. The "public" schema is exposed through
            //    Supabase's Data API and the anon key is public by design, so without this
            //    anyone holding it could read every user's imported material — the exact thing
            //    the PRD privacy guardrail forbids. The anon/authenticated roles do not bypass
            //    RLS, while the app's postgres role does, so deny-all closes the Data API and
            //    costs the application nothing. Per-user access control stays where it already
            //    is: the global query filter in AppDbContext.
            migrationBuilder.Sql("""
                ALTER TABLE public.source_materials
                  ADD CONSTRAINT fk_source_materials_owner_id_auth_users
                  FOREIGN KEY (owner_id) REFERENCES auth.users (id) ON DELETE CASCADE;

                ALTER TABLE public.source_materials ENABLE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.source_materials
                  DROP CONSTRAINT IF EXISTS fk_source_materials_owner_id_auth_users;
                """);

            migrationBuilder.DropTable(
                name: "source_materials");
        }
    }
}
