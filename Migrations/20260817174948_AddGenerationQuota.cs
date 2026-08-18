using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10xnotes.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generation_quotas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_date = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generation_quotas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_generation_quotas_owner_id_usage_date",
                table: "generation_quotas",
                columns: new[] { "owner_id", "usage_date" },
                unique: true);

            // The same two things EF cannot express that AddSourceMaterial had to hand-write,
            // for the same reasons:
            //
            // 1. A foreign key into the "auth" schema, which this context does not manage.
            //    ON DELETE CASCADE so deleting the account takes its ledger with it.
            //
            // 2. Row Level Security with NO policies. The "public" schema is exposed through
            //    Supabase's Data API and the anon key is public by design. This table holds no
            //    note content, but it does reveal when and how heavily a given user works — and,
            //    more sharply, anyone holding the anon key could DELETE the ledger and reset
            //    every user's allowance. The anon/authenticated roles do not bypass RLS while the
            //    app's postgres role does, so deny-all closes the Data API and costs the
            //    application nothing.
            //
            // Unlike source_materials, id and created_at carry no database defaults: this row is
            // only ever written by GenerationQuotaService, which stamps both from the same clock
            // that decides usage_date. See AppDbContext.OnModelCreating.
            migrationBuilder.Sql("""
                ALTER TABLE public.generation_quotas
                  ADD CONSTRAINT fk_generation_quotas_owner_id_auth_users
                  FOREIGN KEY (owner_id) REFERENCES auth.users (id) ON DELETE CASCADE;

                ALTER TABLE public.generation_quotas ENABLE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.generation_quotas
                  DROP CONSTRAINT IF EXISTS fk_generation_quotas_owner_id_auth_users;
                """);

            migrationBuilder.DropTable(
                name: "generation_quotas");
        }
    }
}
