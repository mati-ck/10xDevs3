using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10xnotes.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteAndNoteEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "note_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    draft_length = table.Column<int>(type: "integer", nullable: false),
                    saved_length = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    draft_content = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notes", x => x.id);
                    // NO ACTION spelled out rather than left to the implicit default, so the
                    // choice is visible to whoever reads this file next — it is a product
                    // decision, not a leftover. See AppDbContext.OnModelCreating for why it is
                    // neither Cascade (which would silently answer the still-open question of
                    // what deleting a material does to its note) nor Restrict (which would break
                    // account deletion, because that cascade reaches both tables in one
                    // statement and Restrict is checked immediately).
                    table.ForeignKey(
                        name: "fk_notes_source_materials_source_material_id",
                        column: x => x.source_material_id,
                        principalTable: "source_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "ix_note_events_owner_id_occurred_at",
                table: "note_events",
                columns: new[] { "owner_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notes_source_material_id",
                table: "notes",
                column: "source_material_id",
                unique: true);

            // The same two things EF cannot express that every table before these had to
            // hand-write, for the same reasons — now for both tables at once:
            //
            // 1. A foreign key into the "auth" schema, which this context does not manage.
            //    ON DELETE CASCADE so deleting the account takes the notes and the ledger with
            //    it. This is also the cascade that decides the note→material foreign key above:
            //    it reaches source_materials and notes within one statement, so that key must be
            //    NO ACTION (deferred to the end of the statement) rather than RESTRICT.
            //
            // 2. Row Level Security with NO policies. The "public" schema is exposed through
            //    Supabase's Data API and the anon key is public by design. "notes" holds the most
            //    sensitive content in the product — the user's own study notes, which the PRD
            //    guarantees are private — and "note_events" would let anyone holding the anon key
            //    read or rewrite the measurement the product is validated against. The
            //    anon/authenticated roles do not bypass RLS while the app's postgres role does,
            //    so deny-all closes the Data API and costs the application nothing.
            migrationBuilder.Sql("""
                ALTER TABLE public.notes
                  ADD CONSTRAINT fk_notes_owner_id_auth_users
                  FOREIGN KEY (owner_id) REFERENCES auth.users (id) ON DELETE CASCADE;

                ALTER TABLE public.note_events
                  ADD CONSTRAINT fk_note_events_owner_id_auth_users
                  FOREIGN KEY (owner_id) REFERENCES auth.users (id) ON DELETE CASCADE;

                ALTER TABLE public.notes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.note_events ENABLE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The auth.users keys come off first: EF does not know about them, so DropTable alone
            // would fail against a constraint it never created.
            migrationBuilder.Sql("""
                ALTER TABLE public.notes
                  DROP CONSTRAINT IF EXISTS fk_notes_owner_id_auth_users;

                ALTER TABLE public.note_events
                  DROP CONSTRAINT IF EXISTS fk_note_events_owner_id_auth_users;
                """);

            migrationBuilder.DropTable(
                name: "note_events");

            migrationBuilder.DropTable(
                name: "notes");
        }
    }
}
