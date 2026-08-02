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
