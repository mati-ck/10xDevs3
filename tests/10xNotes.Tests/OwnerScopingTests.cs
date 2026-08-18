using _10xnotes.Data;
using _10xnotes.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace _10xNotes.Tests;

/// <summary>
/// Executable proof of the PRD privacy guardrail: one user's rows are never visible to another.
/// <para>
/// Runs on SQLite in-memory, so no network and no Supabase credentials are involved — query
/// filters and change-tracking behave identically across providers. Postgres-specific concerns
/// (the auth.users FK, RLS) are verified against the real database instead.
/// </para>
/// </summary>
public sealed class OwnerScopingTests : IDisposable
{
    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;

    public OwnerScopingTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var schema = CreateContext(UserA);
        schema.Database.EnsureCreated();
    }

    [Fact]
    public void Query_returns_only_rows_owned_by_the_current_user()
    {
        SeedProfile(UserA, "Ala");
        SeedProfile(UserB, "Bartek");

        using var context = CreateContext(UserA);
        var visible = context.Profiles.ToList();

        Assert.Single(visible);
        Assert.Equal(UserA, visible[0].OwnerId);
        Assert.Equal("Ala", visible[0].DisplayName);
    }

    [Fact]
    public void A_different_user_sees_a_different_row_set()
    {
        SeedProfile(UserA, "Ala");
        SeedProfile(UserB, "Bartek");

        using var asUserB = CreateContext(UserB);
        var visible = asUserB.Profiles.ToList();

        Assert.Single(visible);
        Assert.Equal(UserB, visible[0].OwnerId);
    }

    [Fact]
    public void SaveChanges_stamps_the_owner_on_insert()
    {
        using var context = CreateContext(UserA);
        context.Profiles.Add(NewProfile(displayName: "Bez wlasciciela"));
        context.SaveChanges();

        using var verify = CreateContext(UserA);
        Assert.Equal(UserA, verify.Profiles.Single().OwnerId);
    }

    [Fact]
    public void Saving_without_an_authenticated_user_is_refused()
    {
        using var context = CreateContext(userId: null);
        context.Profiles.Add(NewProfile(displayName: "Anonim"));

        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("authenticated user", error.Message);
    }

    [Fact]
    public void An_unauthenticated_context_sees_nothing()
    {
        SeedProfile(UserA, "Ala");

        using var context = CreateContext(userId: null);

        Assert.Empty(context.Profiles.ToList());
    }

    [Fact]
    public void IgnoreQueryFilters_is_the_one_deliberate_escape_hatch()
    {
        SeedProfile(UserA, "Ala");
        SeedProfile(UserB, "Bartek");

        using var context = CreateContext(UserA);

        Assert.Equal(2, context.Profiles.IgnoreQueryFilters().Count());
    }

    [Fact]
    public void A_caller_supplied_OwnerId_is_overwritten_on_insert()
    {
        // The classic vector is a model-bound form or a DTO round-trip carrying OwnerId. Trusting
        // it would let anyone insert a row owned by somebody else.
        using var context = CreateContext(UserA);
        var smuggled = NewProfile("Podszywacz");
        smuggled.OwnerId = UserB;
        context.Profiles.Add(smuggled);
        context.SaveChanges();

        using var verify = CreateContext(UserA);
        Assert.Equal(UserA, verify.Profiles.Single().OwnerId);
    }

    [Fact]
    public void Modifying_a_row_owned_by_another_user_is_refused()
    {
        SeedProfile(UserB, "Bartek");

        using var context = CreateContext(UserA);
        // Attached rather than queried: the query filter would never hand UserA this row, which
        // is precisely why the write path needs its own guard.
        var victim = new Profile { Id = IdOf(UserB), OwnerId = UserB, DisplayName = "Przejete" };
        context.Attach(victim).State = EntityState.Modified;

        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("owned by another user", error.Message);
    }

    [Fact]
    public void Deleting_a_row_owned_by_another_user_is_refused()
    {
        SeedProfile(UserB, "Bartek");

        using var context = CreateContext(UserA);
        var victim = new Profile { Id = IdOf(UserB), OwnerId = UserB };
        context.Attach(victim).State = EntityState.Deleted;

        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("owned by another user", error.Message);

        using var verify = CreateContext(UserB);
        Assert.Single(verify.Profiles);
    }

    [Fact]
    public void Attaching_another_users_row_with_a_forged_OwnerId_is_refused()
    {
        // The residual gap the F-01 review left open: OwnerId is set to the *attacker's* id, so
        // the change-tracker guard sees nothing wrong. Only owner_id in the WHERE clause stops
        // this, which is what IsConcurrencyToken() buys.
        SeedProfile(UserB, "Bartek");

        using var context = CreateContext(UserA);
        var forged = new Profile { Id = IdOf(UserB), OwnerId = UserA, DisplayName = "Przejete" };
        context.Attach(forged).State = EntityState.Modified;

        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("owned by another user", error.Message);

        using var verify = CreateContext(UserB);
        Assert.Equal("Bartek", verify.Profiles.Single().DisplayName);
    }

    [Fact]
    public void Updating_your_own_row_still_works()
    {
        // Guards that block legitimate writes are worse than no guards, so pin the happy path.
        SeedProfile(UserA, "Ala");

        using var context = CreateContext(UserA);
        var mine = context.Profiles.Single();
        mine.DisplayName = "Ala Nowa";
        context.SaveChanges();

        using var verify = CreateContext(UserA);
        Assert.Equal("Ala Nowa", verify.Profiles.Single().DisplayName);
    }

    // ---------------------------------------------------------------------------------------
    // SourceMaterial — the first domain entity. It gets isolation by implementing IOwnedByUser
    // and nothing else, so these cases exist to prove that the convention actually carried over
    // rather than to re-test AppDbContext. They mirror the Profile cases above deliberately: a
    // future entity that quietly fails to inherit the guarantee should fail here, loudly.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Materials_are_visible_only_to_their_owner()
    {
        SeedMaterial(UserA, "Wyklad 1");
        SeedMaterial(UserB, "Notatki Bartka");

        using var context = CreateContext(UserA);
        var visible = context.SourceMaterials.ToList();

        Assert.Single(visible);
        Assert.Equal("Wyklad 1", visible[0].Title);
    }

    [Fact]
    public void A_user_owns_many_materials()
    {
        // The one place SourceMaterial deliberately departs from Profile: owner_id carries no
        // unique index, because re-importing is legitimate and a user accumulates materials.
        SeedMaterial(UserA, "Wyklad 1");
        SeedMaterial(UserA, "Wyklad 2");

        using var context = CreateContext(UserA);

        Assert.Equal(2, context.SourceMaterials.Count());
    }

    [Fact]
    public void A_caller_supplied_OwnerId_is_overwritten_when_importing()
    {
        // Title and content are model-bound from a form; OwnerId must never be, and is
        // overwritten rather than trusted even if something later binds it by accident.
        using var context = CreateContext(UserA);
        var smuggled = NewMaterial("Podszywacz");
        smuggled.OwnerId = UserB;
        context.SourceMaterials.Add(smuggled);
        context.SaveChanges();

        using var verify = CreateContext(UserA);
        Assert.Equal(UserA, verify.SourceMaterials.Single().OwnerId);
    }

    [Fact]
    public void Importing_without_an_authenticated_user_is_refused()
    {
        using var context = CreateContext(userId: null);
        context.SourceMaterials.Add(NewMaterial("Anonim"));

        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("authenticated user", error.Message);
    }

    [Fact]
    public void An_unauthenticated_context_sees_no_materials()
    {
        SeedMaterial(UserA, "Wyklad 1");

        using var context = CreateContext(userId: null);

        Assert.Empty(context.SourceMaterials.ToList());
    }

    [Fact]
    public void Modifying_a_material_owned_by_another_user_is_refused()
    {
        SeedMaterial(UserB, "Notatki Bartka");

        using var context = CreateContext(UserA);
        var victim = new SourceMaterial
        {
            Id = MaterialIdOf(UserB),
            OwnerId = UserB,
            Title = "Przejete",
            Content = "x",
            OriginalFileName = "x.md"
        };
        context.Attach(victim).State = EntityState.Modified;

        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("owned by another user", error.Message);
    }

    [Fact]
    public void Deleting_a_material_owned_by_another_user_is_refused()
    {
        SeedMaterial(UserB, "Notatki Bartka");

        using var context = CreateContext(UserA);
        var victim = new SourceMaterial { Id = MaterialIdOf(UserB), OwnerId = UserB };
        context.Attach(victim).State = EntityState.Deleted;

        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("owned by another user", error.Message);

        using var verify = CreateContext(UserB);
        Assert.Single(verify.SourceMaterials);
    }

    [Fact]
    public void Attaching_another_users_material_with_a_forged_OwnerId_is_refused()
    {
        // OwnerId set to the *attacker's* id, so the change-tracker guard sees nothing wrong.
        // Only owner_id in the WHERE clause stops this — the IsConcurrencyToken() convention.
        SeedMaterial(UserB, "Notatki Bartka");

        using var context = CreateContext(UserA);
        var forged = new SourceMaterial
        {
            Id = MaterialIdOf(UserB),
            OwnerId = UserA,
            Title = "Przejete",
            Content = "x",
            OriginalFileName = "x.md"
        };
        context.Attach(forged).State = EntityState.Modified;

        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("owned by another user", error.Message);

        using var verify = CreateContext(UserB);
        Assert.Equal("Notatki Bartka", verify.SourceMaterials.Single().Title);
    }

    [Fact]
    public void Imported_content_round_trips_unchanged()
    {
        // The PRD guardrail is that the source material stays available and unchanged. Polish
        // diacritics and Markdown punctuation are the two things a bad encoding or an
        // over-eager sanitizer would quietly mangle.
        const string content = "# Wykład\n\nZażółć gęślą jaźń — *kursywa* i `kod`.\n";

        using var context = CreateContext(UserA);
        var material = NewMaterial("Wyklad");
        material.Content = content;
        context.SourceMaterials.Add(material);
        context.SaveChanges();

        using var verify = CreateContext(UserA);
        Assert.Equal(content, verify.SourceMaterials.Single().Content);
    }

    /// <summary>The primary key of the single material owned by <paramref name="ownerId"/>.</summary>
    private Guid MaterialIdOf(Guid ownerId)
    {
        using var context = CreateContext(ownerId);
        return context.SourceMaterials.Single().Id;
    }

    private void SeedMaterial(Guid ownerId, string title)
    {
        using var context = CreateContext(ownerId);
        context.SourceMaterials.Add(NewMaterial(title));
        context.SaveChanges();
    }

    // Id and CreatedAt are supplied explicitly for the same reason NewProfile does it: their
    // defaults are Postgres functions SQLite cannot evaluate.
    private static SourceMaterial NewMaterial(string title) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Content = $"# {title}",
        OriginalFileName = $"{title}.md",
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    /// <summary>The primary key of the single profile owned by <paramref name="ownerId"/>.</summary>
    private Guid IdOf(Guid ownerId)
    {
        using var context = CreateContext(ownerId);
        return context.Profiles.Single().Id;
    }

    private void SeedProfile(Guid ownerId, string displayName)
    {
        using var context = CreateContext(ownerId);
        context.Profiles.Add(NewProfile(displayName));
        context.SaveChanges();
    }

    // Id and CreatedAt are supplied explicitly because their defaults are Postgres functions
    // (gen_random_uuid(), now()) that SQLite cannot evaluate.
    private static Profile NewProfile(string displayName) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = displayName,
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    private AppDbContext CreateContext(Guid? userId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Mirrors what UserScopedDbContextFactory does at runtime: create, then apply the
        // current user. Guid.Empty stands for "nobody is signed in".
        return new AppDbContext(options) { CurrentUserId = userId ?? Guid.Empty };
    }

    public void Dispose() => _connection.Dispose();
}
