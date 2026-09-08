using System.Security.Claims;
using _10xnotes.Auth;
using _10xnotes.Data;
using _10xnotes.Data.Entities;
using _10xnotes.Profiles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace _10xNotes.Tests;

/// <summary>
/// Proves the display name is the owner's alone, that clearing it stores null rather than an
/// empty string, and that the claim the nav renders from says what the profile says.
/// <para>
/// SQLite in-memory, like <see cref="NoteServiceTests"/>. The Postgres-only parts — the
/// <c>auth.users</c> foreign key and the <c>on_auth_user_created</c> trigger that guarantees a
/// row — are verified against the real database, so these tests seed the profile row the trigger
/// would have made.
/// </para>
/// </summary>
public sealed class ProfileServiceTests : IDisposable
{
    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;

    public ProfileServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var schema = CreateContext(UserA);
        schema.Database.EnsureCreated();
    }

    // -- Reading ---------------------------------------------------------------------------

    [Fact]
    public async Task A_profile_with_no_name_reads_as_null()
    {
        SeedProfile(UserA, displayName: null);

        Assert.Null(await CreateService(UserA).GetDisplayNameAsync());
    }

    [Fact]
    public async Task A_name_that_was_set_reads_back()
    {
        SeedProfile(UserA, "Ala Kowalska");

        Assert.Equal("Ala Kowalska", await CreateService(UserA).GetDisplayNameAsync());
    }

    // -- Writing ---------------------------------------------------------------------------

    [Fact]
    public async Task Setting_a_name_stores_it_trimmed()
    {
        SeedProfile(UserA, displayName: null);

        var result = await CreateService(UserA).SetDisplayNameAsync("  Ala Kowalska  ");

        Assert.True(result.Succeeded);
        Assert.Equal("Ala Kowalska", result.DisplayName);
        Assert.Equal("Ala Kowalska", StoredNameOf(UserA));
    }

    /// <summary>
    /// The whole reason <c>DisplayNameValidator.Normalize</c> exists: a blank box must reach the
    /// column as null, not as "", or the nav renders an invisible entry instead of the email.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Clearing_a_name_stores_null_rather_than_an_empty_string(string submitted)
    {
        SeedProfile(UserA, "Ala Kowalska");

        var result = await CreateService(UserA).SetDisplayNameAsync(submitted);

        Assert.True(result.Succeeded);
        Assert.Null(result.DisplayName);
        Assert.Null(StoredNameOf(UserA));
    }

    /// <summary>
    /// Validated in the service as well as on the form: the page is not the only possible caller
    /// and the limit protects the column, not the UI.
    /// </summary>
    [Fact]
    public async Task An_over_long_name_is_refused_without_touching_the_row()
    {
        SeedProfile(UserA, "Ala Kowalska");

        var result = await CreateService(UserA)
            .SetDisplayNameAsync(new string('a', DisplayNameValidator.MaxLength + 1));

        Assert.False(result.Succeeded);
        Assert.Equal(ProfileUpdateFailure.NameTooLong, result.FailureReason);
        Assert.Equal("Ala Kowalska", StoredNameOf(UserA));
    }

    /// <summary>
    /// The trigger guarantees a row, so its absence means the trigger is gone — reported rather
    /// than papered over by creating one, which would hide that.
    /// </summary>
    [Fact]
    public async Task A_missing_profile_is_reported_rather_than_created()
    {
        var result = await CreateService(UserA).SetDisplayNameAsync("Ala Kowalska");

        Assert.False(result.Succeeded);
        Assert.Equal(ProfileUpdateFailure.ProfileMissing, result.FailureReason);

        using var context = CreateContext(null);
        Assert.Empty(context.Profiles.IgnoreQueryFilters());
    }

    // -- Isolation ---------------------------------------------------------------------------

    [Fact]
    public async Task One_user_cannot_read_anothers_display_name()
    {
        SeedProfile(UserA, "Ala Kowalska");
        SeedProfile(UserB, displayName: null);

        Assert.Null(await CreateService(UserB).GetDisplayNameAsync());
    }

    /// <summary>
    /// B writing their own name must leave A's alone. The global query filter is what makes
    /// B's read find B's row rather than A's; nothing here writes an owner filter by hand.
    /// </summary>
    [Fact]
    public async Task One_user_cannot_overwrite_anothers_display_name()
    {
        SeedProfile(UserA, "Ala Kowalska");
        SeedProfile(UserB, "Basia Nowak");

        await CreateService(UserB).SetDisplayNameAsync("Basia Zmieniona");

        Assert.Equal("Ala Kowalska", StoredNameOf(UserA));
        Assert.Equal("Basia Zmieniona", StoredNameOf(UserB));
    }

    [Fact]
    public async Task An_unauthenticated_read_sees_nothing()
    {
        SeedProfile(UserA, "Ala Kowalska");

        Assert.Null(await CreateService(null).GetDisplayNameAsync());
    }

    [Fact]
    public async Task An_unauthenticated_write_is_refused_as_a_missing_profile()
    {
        SeedProfile(UserA, "Ala Kowalska");

        var result = await CreateService(null).SetDisplayNameAsync("Ktokolwiek");

        // The row is invisible to an unauthenticated context, so the write never reaches
        // StampOwners' throw — it stops at "no profile", and A's name is untouched either way.
        Assert.False(result.Succeeded);
        Assert.Equal(ProfileUpdateFailure.ProfileMissing, result.FailureReason);
        Assert.Equal("Ala Kowalska", StoredNameOf(UserA));
    }

    // -- The sign-in read ----------------------------------------------------------------------

    /// <summary>
    /// The read Login.razor uses, where nobody is signed in yet: the accessor reports nobody, so
    /// the ordinary read finds nothing and the id has to be supplied. Without this path a user
    /// who set a name would see their email in the nav after every fresh sign-in.
    /// </summary>
    [Fact]
    public async Task The_sign_in_read_finds_the_name_when_the_accessor_reports_nobody()
    {
        SeedProfile(UserA, "Ala Kowalska");

        var signedOut = CreateService(null);

        Assert.Null(await signedOut.GetDisplayNameAsync());
        Assert.Equal("Ala Kowalska", await signedOut.GetDisplayNameAtSignInAsync(UserA));
    }

    /// <summary>
    /// It is still owner-scoped — the id chooses whose filter applies, it does not lift the
    /// filter. Asking for A's id must never surface B's row.
    /// </summary>
    [Fact]
    public async Task The_sign_in_read_is_still_scoped_to_the_id_it_was_given()
    {
        SeedProfile(UserA, "Ala Kowalska");
        SeedProfile(UserB, "Basia Nowak");

        var service = CreateService(null);

        Assert.Equal("Ala Kowalska", await service.GetDisplayNameAtSignInAsync(UserA));
        Assert.Equal("Basia Nowak", await service.GetDisplayNameAtSignInAsync(UserB));
        Assert.Null(await service.GetDisplayNameAtSignInAsync(Guid.NewGuid()));
    }

    // -- What the nav renders ------------------------------------------------------------------

    /// <summary>
    /// The nav's fallback rule, which lives in AuthCookie rather than in the markup so it can be
    /// tested in a project with no bUnit.
    /// </summary>
    [Fact]
    public void The_principal_shows_the_display_name_when_there_is_one()
    {
        var principal = AuthCookie.BuildPrincipal(UserA, "ala@example.com", "Ala Kowalska");

        Assert.Equal("Ala Kowalska", AuthCookie.DisplayNameOrEmail(principal));
        // The email stays reachable: the profile page shows it and the change-password
        // re-authentication uses it.
        Assert.Equal("ala@example.com", principal.FindFirstValue(ClaimTypes.Email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_principal_falls_back_to_the_email_when_there_is_no_name(string? displayName)
    {
        var principal = AuthCookie.BuildPrincipal(UserA, "ala@example.com", displayName);

        Assert.Equal("ala@example.com", AuthCookie.DisplayNameOrEmail(principal));
        // Absent rather than present-and-empty: two spellings of "not set" would mean only one
        // of them falls back.
        Assert.Null(principal.FindFirstValue(AuthCookie.DisplayNameClaimType));
    }

    /// <summary>
    /// A cookie issued before this claim existed simply lacks it. It must keep working rather
    /// than rendering a blank nav entry — that is what makes this change rollback-safe.
    /// </summary>
    [Fact]
    public void A_principal_from_before_the_claim_existed_still_renders()
    {
        var legacy = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, UserA.ToString()),
                new Claim(ClaimTypes.Email, "ala@example.com"),
                new Claim(ClaimTypes.Name, "ala@example.com")
            ],
            "Cookies"));

        Assert.Equal("ala@example.com", AuthCookie.DisplayNameOrEmail(legacy));
    }

    // -- Fixtures ------------------------------------------------------------------------------

    /// <summary>
    /// A real <see cref="UserScopedDbContextFactory"/> over the in-memory connection — the service
    /// must go through the sanctioned seam, so the test wires the seam rather than bypassing it.
    /// </summary>
    private ProfileService CreateService(Guid? userId) =>
        new(new UserScopedDbContextFactory(
            new SqliteTestContext.Factory(_connection),
            new StubCurrentUserAccessor(userId)));

    /// <summary>Stands in for the row <c>on_auth_user_created</c> creates in Postgres.</summary>
    private void SeedProfile(Guid ownerId, string? displayName)
    {
        using var context = CreateContext(ownerId);

        context.Profiles.Add(new Profile
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            CreatedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)
        });

        context.SaveChanges();
    }

    private string? StoredNameOf(Guid ownerId)
    {
        using var context = CreateContext(ownerId);

        return context.Profiles.Single().DisplayName;
    }

    private AppDbContext CreateContext(Guid? userId) => SqliteTestContext.Create(_connection, userId);

    public void Dispose() => _connection.Dispose();
}
