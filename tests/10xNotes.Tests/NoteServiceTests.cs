using _10xnotes.Data;
using _10xnotes.Data.Entities;
using _10xnotes.Notes;
using _10xnotes.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace _10xNotes.Tests;

/// <summary>
/// Proves the three things saving a note has to get right: it is strictly one note per material,
/// it is invisible to anyone but its owner, and the acceptance ledger counts exactly the events
/// the PRD's 75% criterion is defined over.
/// <para>
/// SQLite in-memory, like <see cref="GenerationQuotaTests"/> — query filters, the unique index
/// and change tracking behave identically across providers. The Postgres-only parts (the
/// auth.users foreign key, the NO ACTION key into source_materials, RLS) are verified against
/// the real database.
/// </para>
/// </summary>
public sealed class NoteServiceTests : IDisposable
{
    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Afternoon = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private const string PromptVersion = "v2";
    private const string Model = "google/gemini-3.7-flash";
    private const string Draft = "## Streszczenie\n\n- punkt od modelu";
    private const string Edited = "## Streszczenie\n\n- punkt poprawiony przez człowieka";

    private readonly SqliteConnection _connection;

    public NoteServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var schema = CreateContext(UserA);
        schema.Database.EnsureCreated();
    }

    // -- Saving ---------------------------------------------------------------------------

    [Fact]
    public async Task Accepting_a_draft_creates_a_note_bound_to_its_material()
    {
        var material = SeedMaterial(UserA);
        var service = CreateService(UserA);

        var result = await service.AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);

        Assert.True(result.Succeeded);

        using var context = CreateContext(UserA);
        var note = Assert.Single(context.Notes);
        Assert.Equal(material, note.SourceMaterialId);
        Assert.Equal(UserA, note.OwnerId);
        Assert.Equal("Notatka", note.Title);
        Assert.Equal(Edited, note.Content);
        Assert.Equal(Draft, note.DraftContent);
        Assert.Equal(PromptVersion, note.PromptVersion);
        Assert.Equal(Model, note.Model);
    }

    [Fact]
    public async Task Accepting_twice_for_one_material_replaces_rather_than_adds()
    {
        // The 1:1 decision, from the side that matters: the roadmap's open question was 1:N vs
        // overwrite, and this is what "overwrite" has to mean in the table.
        var material = SeedMaterial(UserA);
        var service = CreateService(UserA);

        await service.AcceptAsync(material, "Pierwsza", "pierwsza treść", Draft, PromptVersion, Model);
        await service.AcceptAsync(material, "Druga", "druga treść", "drugi szkic", PromptVersion, Model);

        using var context = CreateContext(UserA);
        var note = Assert.Single(context.Notes);
        Assert.Equal("Druga", note.Title);
        Assert.Equal("druga treść", note.Content);
        Assert.Equal("drugi szkic", note.DraftContent);
    }

    [Fact]
    public async Task Two_materials_can_each_have_their_own_note()
    {
        // The unique index is on source_material_id, not on owner_id — the cap is one note per
        // material, never one note per user.
        var first = SeedMaterial(UserA);
        var second = SeedMaterial(UserA);
        var service = CreateService(UserA);

        await service.AcceptAsync(first, "Pierwsza", "treść", Draft, PromptVersion, Model);
        await service.AcceptAsync(second, "Druga", "treść", Draft, PromptVersion, Model);

        using var context = CreateContext(UserA);
        Assert.Equal(2, context.Notes.Count());
    }

    [Fact]
    public async Task Accepting_for_a_material_that_is_not_yours_is_refused()
    {
        var material = SeedMaterial(UserB);
        var service = CreateService(UserA);

        var result = await service.AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);

        Assert.False(result.Succeeded);
        Assert.Equal(NoteSaveFailure.NotFound, result.FailureReason);

        using var context = CreateContext(UserA);
        Assert.Empty(context.Notes);
    }

    [Fact]
    public async Task Accepting_without_an_authenticated_user_writes_nothing()
    {
        // Fail-closed without throwing: the material lookup is owner-scoped, so with nobody
        // signed in it finds nothing and the method returns before any write. A typed refusal
        // rather than an exception, for the reason NoteSaveResult documents — an exception
        // escaping a result-returning method lands in the page's catch-all and loses the reason.
        var material = SeedMaterial(UserA);
        var service = CreateService(userId: null);

        var result = await service.AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);

        Assert.False(result.Succeeded);
        Assert.Equal(NoteSaveFailure.NotFound, result.FailureReason);

        using var context = CreateContext(UserA);
        Assert.Empty(context.Notes);
        Assert.Empty(context.NoteEvents);
    }

    // -- Owner isolation ------------------------------------------------------------------

    [Fact]
    public async Task A_users_note_is_invisible_to_another_user()
    {
        var material = SeedMaterial(UserA);
        await CreateService(UserA).AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);

        using var asUserB = CreateContext(UserB);

        Assert.Empty(asUserB.Notes.ToList());
    }

    [Fact]
    public async Task Another_users_note_is_not_reachable_by_id()
    {
        var material = SeedMaterial(UserA);
        var saved = await CreateService(UserA).AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);

        // Identical to a note that does not exist — telling the two apart would let anyone probe
        // which ids are real.
        Assert.Null(await CreateService(UserB).GetAsync(saved.Note!.Id));
        Assert.Null(await CreateService(UserB).GetByMaterialAsync(material));
    }

    [Fact]
    public async Task A_material_with_no_note_yet_reads_as_none()
    {
        var material = SeedMaterial(UserA);

        Assert.Null(await CreateService(UserA).GetByMaterialAsync(material));
    }

    // -- Re-saving ------------------------------------------------------------------------

    [Fact]
    public async Task Re_saving_updates_the_text_and_bumps_only_UpdatedAt()
    {
        var material = SeedMaterial(UserA);
        var clock = new FixedClock(Afternoon);
        var service = CreateService(UserA, clock);

        var accepted = await service.AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);
        var createdAt = accepted.Note!.CreatedAt;

        clock.Now = Afternoon.AddHours(3);

        var updated = await service.UpdateAsync(accepted.Note.Id, "Poprawiona", "poprawiona treść");

        Assert.True(updated.Succeeded);

        using var context = CreateContext(UserA);
        var note = Assert.Single(context.Notes);
        Assert.Equal("Poprawiona", note.Title);
        Assert.Equal("poprawiona treść", note.Content);
        Assert.Equal(createdAt, note.CreatedAt);
        Assert.Equal(Afternoon.AddHours(3), note.UpdatedAt);
    }

    [Fact]
    public async Task Re_saving_leaves_the_original_draft_intact()
    {
        // DraftContent describes the generation this note came from, so editing the text must
        // not touch it — otherwise "how much did the human change" is unanswerable the moment
        // anybody fixes a typo.
        var material = SeedMaterial(UserA);
        var service = CreateService(UserA);

        var accepted = await service.AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);
        await service.UpdateAsync(accepted.Note!.Id, "Notatka", "całkiem inna treść");

        using var context = CreateContext(UserA);
        var note = Assert.Single(context.Notes);
        Assert.Equal(Draft, note.DraftContent);
        Assert.Equal(PromptVersion, note.PromptVersion);
        Assert.Equal(Model, note.Model);
    }

    [Fact]
    public async Task Re_saving_someone_elses_note_is_refused()
    {
        var material = SeedMaterial(UserA);
        var accepted = await CreateService(UserA).AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);

        var result = await CreateService(UserB).UpdateAsync(accepted.Note!.Id, "Przejęta", "treść");

        Assert.False(result.Succeeded);
        Assert.Equal(NoteSaveFailure.NotFound, result.FailureReason);

        using var context = CreateContext(UserA);
        Assert.Equal("Notatka", context.Notes.Single().Title);
    }

    [Fact]
    public async Task Re_saving_a_note_that_does_not_exist_is_refused()
    {
        var result = await CreateService(UserA).UpdateAsync(Guid.NewGuid(), "Notatka", "treść");

        Assert.False(result.Succeeded);
        Assert.Equal(NoteSaveFailure.NotFound, result.FailureReason);
    }

    // -- The acceptance ledger ------------------------------------------------------------

    [Fact]
    public async Task Accepting_appends_a_Saved_event()
    {
        var material = SeedMaterial(UserA);

        await CreateService(UserA).AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);

        using var context = CreateContext(UserA);
        var recorded = Assert.Single(context.NoteEvents);
        Assert.Equal(NoteEventKind.Saved, recorded.Kind);
        Assert.Equal(material, recorded.SourceMaterialId);
        Assert.Equal(UserA, recorded.OwnerId);
        Assert.Equal(Draft.Length, recorded.DraftLength);
        Assert.Equal(Edited.Length, recorded.SavedLength);
        Assert.Equal(PromptVersion, recorded.PromptVersion);
        Assert.Equal(Model, recorded.Model);
    }

    [Fact]
    public async Task Re_saving_appends_no_event_at_all()
    {
        // The single most load-bearing assertion in this file. If re-saving counted, fixing a
        // typo would raise the numerator against an unchanged denominator and the acceptance
        // rate could exceed 100% — which is exactly why AcceptAsync and UpdateAsync are two
        // methods rather than one with a flag.
        var material = SeedMaterial(UserA);
        var service = CreateService(UserA);

        var accepted = await service.AcceptAsync(material, "Notatka", Edited, Draft, PromptVersion, Model);
        await service.UpdateAsync(accepted.Note!.Id, "Poprawiona", "raz");
        await service.UpdateAsync(accepted.Note.Id, "Poprawiona", "dwa");

        using var context = CreateContext(UserA);
        Assert.Single(context.NoteEvents);
        Assert.Equal(NoteEventKind.Saved, context.NoteEvents.Single().Kind);
    }

    [Fact]
    public async Task Recording_a_generation_appends_a_Generated_event()
    {
        var material = SeedMaterial(UserA);

        await CreateService(UserA).RecordGenerationAsync(material, Draft, PromptVersion, Model);

        using var context = CreateContext(UserA);
        var recorded = Assert.Single(context.NoteEvents);
        Assert.Equal(NoteEventKind.Generated, recorded.Kind);
        Assert.Equal(Draft.Length, recorded.DraftLength);
        // Nothing has been saved at generation time; that is what keeps the two events distinct.
        Assert.Equal(0, recorded.SavedLength);
    }

    [Fact]
    public async Task Overwriting_a_note_keeps_both_acceptances_in_the_ledger()
    {
        // The reason the ledger exists at all: with one note per material, the second acceptance
        // destroys the first note's row, so the notes table alone can never count what happened.
        var material = SeedMaterial(UserA);
        var service = CreateService(UserA);

        await service.AcceptAsync(material, "Pierwsza", "raz", Draft, PromptVersion, Model);
        await service.AcceptAsync(material, "Druga", "dwa", Draft, PromptVersion, Model);

        using var context = CreateContext(UserA);
        Assert.Single(context.Notes);
        Assert.Equal(2, context.NoteEvents.Count(e => e.Kind == NoteEventKind.Saved));
    }

    [Fact]
    public async Task The_ledger_is_owner_scoped_like_everything_else()
    {
        var material = SeedMaterial(UserA);
        await CreateService(UserA).RecordGenerationAsync(material, Draft, PromptVersion, Model);

        using var asUserB = CreateContext(UserB);

        Assert.Empty(asUserB.NoteEvents.ToList());
    }

    // -- Validation is the service's job too ----------------------------------------------

    [Theory]
    [InlineData("", "treść", NoteSaveFailure.TitleEmpty)]
    [InlineData("Notatka", "", NoteSaveFailure.ContentEmpty)]
    [InlineData("Notatka", "   ", NoteSaveFailure.ContentEmpty)]
    public async Task Accepting_an_invalid_note_is_refused_by_the_service(
        string title, string content, NoteSaveFailure expected)
    {
        var material = SeedMaterial(UserA);

        var result = await CreateService(UserA).AcceptAsync(
            material, title, content, Draft, PromptVersion, Model);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.FailureReason);

        using var context = CreateContext(UserA);
        Assert.Empty(context.Notes);
        Assert.Empty(context.NoteEvents);
    }

    [Fact]
    public async Task Content_over_the_limit_is_refused_by_the_service_not_only_by_the_editor()
    {
        // The page is not the only possible caller, and the bound protects the row rather than
        // the layout.
        var material = SeedMaterial(UserA);
        var tooLong = new string('a', NoteValidator.MaxContentLength + 1);

        var result = await CreateService(UserA).AcceptAsync(
            material, "Notatka", tooLong, Draft, PromptVersion, Model);

        Assert.False(result.Succeeded);
        Assert.Equal(NoteSaveFailure.ContentTooLong, result.FailureReason);
    }

    [Fact]
    public async Task A_title_over_the_limit_is_refused_before_it_reaches_the_column()
    {
        var material = SeedMaterial(UserA);
        var tooLong = new string('a', NoteValidator.MaxTitleLength + 1);

        var result = await CreateService(UserA).AcceptAsync(
            material, tooLong, Edited, Draft, PromptVersion, Model);

        Assert.False(result.Succeeded);
        Assert.Equal(NoteSaveFailure.TitleTooLong, result.FailureReason);
    }

    [Fact]
    public async Task The_saved_title_is_the_trimmed_one()
    {
        var material = SeedMaterial(UserA);

        await CreateService(UserA).AcceptAsync(material, "  Notatka  ", Edited, Draft, PromptVersion, Model);

        using var context = CreateContext(UserA);
        Assert.Equal("Notatka", context.Notes.Single().Title);
    }

    // -- Fixtures -------------------------------------------------------------------------

    private NoteService CreateService(Guid? userId, TimeProvider? clock = null) =>
        new(CreateFactory(userId),
            clock ?? new FixedClock(Afternoon),
            NullLogger<NoteService>.Instance);

    /// <summary>
    /// A real <see cref="UserScopedDbContextFactory"/> over the in-memory connection — the
    /// service under test must go through the sanctioned seam, so the test wires the seam rather
    /// than bypassing it.
    /// </summary>
    private UserScopedDbContextFactory CreateFactory(Guid? userId) =>
        new(new SqliteContextFactory(_connection), new StubCurrentUserAccessor(userId));

    /// <summary>
    /// Inserts a material for <paramref name="ownerId"/> and returns its id.
    /// </summary>
    /// <remarks>
    /// Id and CreatedAt are supplied by hand: the columns carry Postgres defaults
    /// (<c>gen_random_uuid()</c>, <c>now()</c>) that SQLite cannot evaluate.
    /// </remarks>
    private Guid SeedMaterial(Guid ownerId)
    {
        var id = Guid.NewGuid();

        using var context = CreateContext(ownerId);

        context.SourceMaterials.Add(new SourceMaterial
        {
            Id = id,
            Title = "Wykład",
            Content = "Treść materiału źródłowego.",
            OriginalFileName = "wyklad.md",
            CreatedAt = Afternoon
        });

        context.SaveChanges();

        return id;
    }

    private AppDbContext CreateContext(Guid? userId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options) { CurrentUserId = userId ?? Guid.Empty };
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Hands out contexts on the shared in-memory connection, with no user applied.</summary>
    private sealed class SqliteContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }

    /// <summary>A clock the test moves by hand, so "three hours later" does not require waiting.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.FindSystemTimeZoneById(AppTimeProvider.DefaultTimeZoneId);
    }
}
