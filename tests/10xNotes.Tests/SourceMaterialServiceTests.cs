using _10xnotes.Data;
using _10xnotes.Data.Entities;
using _10xnotes.SourceMaterials;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace _10xNotes.Tests;

/// <summary>
/// Proves the three things the materials list has to get right: it shows only its owner's rows,
/// it is ordered and stays ordered, and it reports whether a material already has a note without
/// ever reading across accounts to answer.
/// <para>
/// SQLite in-memory, like <see cref="NoteServiceTests"/> — the query filters and the LEFT JOIN
/// translate identically. The Postgres-only parts (the auth.users foreign key, NO ACTION, RLS)
/// are untouched by this slice.
/// </para>
/// </summary>
public sealed class SourceMaterialServiceTests : IDisposable
{
    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Afternoon = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public SourceMaterialServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var schema = CreateContext(UserA);
        schema.Database.EnsureCreated();
    }

    // -- Owner isolation --------------------------------------------------------------------

    [Fact]
    public async Task The_list_shows_only_the_signed_in_users_materials()
    {
        // The executable form of the PRD privacy guardrail for this list. It passes because of
        // the global query filter — ListAsync writes no owner filter of its own, deliberately.
        SeedMaterial(UserA, "Mój wykład");
        SeedMaterial(UserB, "Cudzy wykład");

        var listed = await CreateService(UserA).ListAsync();

        var row = Assert.Single(listed);
        Assert.Equal("Mój wykład", row.Title);
    }

    [Fact]
    public async Task Another_users_note_never_leaks_as_this_materials_note()
    {
        // Impossible in production — saving stamps the owner from the signed-in user — but that
        // is the point: this asserts the join is filtered on BOTH sides rather than only on the
        // material, which is the failure mode a one-sided filter would hide.
        var material = SeedMaterial(UserA, "Wykład");
        SeedNote(UserB, material, "Notatka intruza");

        var row = Assert.Single(await CreateService(UserA).ListAsync());

        Assert.Null(row.NoteId);
    }

    [Fact]
    public async Task The_list_of_a_user_with_no_materials_is_empty_rather_than_null()
    {
        var listed = await CreateService(UserA).ListAsync();

        Assert.NotNull(listed);
        Assert.Empty(listed);
    }

    // -- Whether a material already has a note ----------------------------------------------

    [Fact]
    public async Task A_material_reports_its_note_and_a_material_without_one_reports_none()
    {
        // Both variants in a single call on purpose: on a uniform data set a broken join looks
        // correct, because every row happens to agree.
        var withNote = SeedMaterial(UserA, "Z notatką", Afternoon);
        var withoutNote = SeedMaterial(UserA, "Bez notatki", Afternoon.AddHours(-1));
        var note = SeedNote(UserA, withNote, "Notatka");

        var listed = await CreateService(UserA).ListAsync();

        Assert.Equal(2, listed.Count);
        Assert.Equal(note, listed.Single(m => m.Id == withNote).NoteId);
        Assert.Null(listed.Single(m => m.Id == withoutNote).NoteId);
    }

    [Fact]
    public async Task A_material_with_a_note_still_produces_exactly_one_row()
    {
        // The unique index on notes.source_material_id is what makes DefaultIfEmpty() unable to
        // duplicate a material — worth asserting, because a duplicated row is the classic way a
        // left join goes wrong.
        var material = SeedMaterial(UserA, "Wykład");
        SeedNote(UserA, material, "Notatka");

        Assert.Single(await CreateService(UserA).ListAsync());
    }

    // -- Ordering ----------------------------------------------------------------------------

    [Fact]
    public async Task The_list_puts_the_most_recently_imported_material_first()
    {
        SeedMaterial(UserA, "Starszy", Afternoon);
        SeedMaterial(UserA, "Nowszy", Afternoon.AddHours(3));

        var listed = await CreateService(UserA).ListAsync();

        Assert.Equal(new[] { "Nowszy", "Starszy" }, listed.Select(m => m.Title));
    }

    [Fact]
    public async Task Materials_imported_in_the_same_instant_come_back_in_a_stable_order()
    {
        // Same CreatedAt, so Id is the only thing left to decide. Without the tie-break the
        // order of these rows is undefined and can change between refreshes.
        SeedMaterial(UserA, "Pierwszy", Afternoon);
        SeedMaterial(UserA, "Drugi", Afternoon);

        var listed = await CreateService(UserA).ListAsync();

        Assert.Equal(2, listed.Count);
        Assert.Equal(
            listed.Select(m => m.Id.ToString()).Order(StringComparer.OrdinalIgnoreCase),
            listed.Select(m => m.Id.ToString()));
    }

    // -- Fixtures ----------------------------------------------------------------------------

    /// <summary>
    /// A real <see cref="UserScopedDbContextFactory"/> over the in-memory connection — the
    /// service under test must go through the sanctioned seam, so the test wires the seam rather
    /// than bypassing it.
    /// </summary>
    private SourceMaterialService CreateService(Guid? userId) =>
        new(new UserScopedDbContextFactory(
            new SqliteTestContext.Factory(_connection),
            new StubCurrentUserAccessor(userId)));

    /// <summary>
    /// Inserts a material for <paramref name="ownerId"/> and returns its id.
    /// </summary>
    /// <remarks>
    /// Id and CreatedAt are supplied by hand: the columns carry Postgres defaults
    /// (<c>gen_random_uuid()</c>, <c>now()</c>) that SQLite cannot evaluate.
    /// </remarks>
    private Guid SeedMaterial(Guid ownerId, string title, DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();

        using var context = CreateContext(ownerId);

        context.SourceMaterials.Add(new SourceMaterial
        {
            Id = id,
            Title = title,
            Content = "Treść materiału źródłowego.",
            OriginalFileName = $"{title}.md",
            CreatedAt = createdAt ?? Afternoon
        });

        context.SaveChanges();

        return id;
    }

    /// <summary>
    /// Inserts a saved note for <paramref name="materialId"/> owned by <paramref name="ownerId"/>
    /// and returns its id. Written straight through the context rather than through
    /// <c>NoteService</c>, because one case here needs an owner combination the service
    /// (correctly) refuses to produce.
    /// </summary>
    private Guid SeedNote(Guid ownerId, Guid materialId, string title)
    {
        var id = Guid.NewGuid();

        using var context = CreateContext(ownerId);

        context.Notes.Add(new Note
        {
            Id = id,
            SourceMaterialId = materialId,
            Title = title,
            Content = "Treść notatki.",
            DraftContent = "Szkic notatki.",
            PromptVersion = "v2",
            Model = "google/gemini-3.7-flash",
            CreatedAt = Afternoon,
            UpdatedAt = Afternoon
        });

        context.SaveChanges();

        return id;
    }

    // Built through SqliteTestContext, not by hand: it applies the tick converter that lets
    // SQLite order by a timestamp at all, and the schema and every reader must agree on it.
    private AppDbContext CreateContext(Guid? userId) => SqliteTestContext.Create(_connection, userId);

    public void Dispose() => _connection.Dispose();
}
