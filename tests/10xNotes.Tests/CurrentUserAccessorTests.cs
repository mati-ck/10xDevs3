using System.Globalization;
using System.Security.Claims;
using _10xnotes.Auth;
using _10xnotes.Data;
using _10xnotes.Data.Entities;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace _10xNotes.Tests;

/// <summary>
/// Covers the seam between "who is signed in" and "which rows the data layer returns".
/// <para>
/// This is the path the F-01 implementation review flagged as the HIGH risk: identity sourced
/// from the wrong place silently yields zero rows for a signed-in user, and no compiler or
/// query-filter test catches it. So the claim mapping and the factory that consumes it are
/// asserted end to end, not just in isolation.
/// </para>
/// </summary>
public sealed class CurrentUserAccessorTests : IDisposable
{
    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;

    public CurrentUserAccessorTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var schema = new AppDbContext(SqliteOptions());
        schema.Database.EnsureCreated();
    }

    [Fact]
    public async Task NameIdentifier_becomes_the_current_user_id()
    {
        var accessor = AccessorFor(Authenticated(ClaimTypes.NameIdentifier, UserA.ToString()));

        Assert.Equal(UserA, await accessor.GetUserIdAsync());
    }

    [Fact]
    public async Task A_raw_sub_claim_is_accepted_too()
    {
        // Not every principal goes through ASP.NET's claim mapping; the raw GoTrue claim name
        // must still resolve, or an unmapped principal would silently see nothing.
        var accessor = AccessorFor(Authenticated("sub", UserA.ToString()));

        Assert.Equal(UserA, await accessor.GetUserIdAsync());
    }

    [Fact]
    public async Task An_unauthenticated_principal_has_no_user_id()
    {
        var accessor = AccessorFor(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(await accessor.GetUserIdAsync());
    }

    [Fact]
    public async Task A_principal_carrying_no_identifier_has_no_user_id()
    {
        var accessor = AccessorFor(Authenticated(ClaimTypes.Email, "ala@example.com"));

        Assert.Null(await accessor.GetUserIdAsync());
    }

    [Fact]
    public async Task An_unparseable_identifier_yields_null_rather_than_a_guess()
    {
        var accessor = AccessorFor(Authenticated(ClaimTypes.NameIdentifier, "not-a-guid"));

        Assert.Null(await accessor.GetUserIdAsync());
    }

    [Fact]
    public async Task A_context_from_the_factory_returns_the_signed_in_users_rows()
    {
        SeedProfile(UserA, "Ala");
        SeedProfile(UserB, "Bartek");

        var factory = new UserScopedDbContextFactory(
            new SqliteDbContextFactory(SqliteOptions()),
            new StubCurrentUserAccessor(UserA));

        await using var context = await factory.CreateAsync();
        var visible = await context.Profiles.ToListAsync();

        Assert.Equal(UserA, Assert.Single(visible).OwnerId);
    }

    [Fact]
    public async Task A_context_from_the_factory_sees_nothing_when_nobody_is_signed_in()
    {
        SeedProfile(UserA, "Ala");

        var factory = new UserScopedDbContextFactory(
            new SqliteDbContextFactory(SqliteOptions()),
            new StubCurrentUserAccessor(null));

        await using var context = await factory.CreateAsync();

        Assert.Empty(await context.Profiles.ToListAsync());
    }

    [Fact]
    public async Task Claims_reach_the_data_layer_end_to_end()
    {
        SeedProfile(UserA, "Ala");
        SeedProfile(UserB, "Bartek");

        // The real accessor over a real principal — nothing stubbed between the claim and the row.
        var factory = new UserScopedDbContextFactory(
            new SqliteDbContextFactory(SqliteOptions()),
            AccessorFor(Authenticated(ClaimTypes.NameIdentifier, UserB.ToString())));

        await using var context = await factory.CreateAsync();
        var visible = await context.Profiles.ToListAsync();

        Assert.Equal("Bartek", Assert.Single(visible).DisplayName);
    }

    private static ClaimsPrincipal Authenticated(string claimType, string value)
    {
        // The authentication type is what makes IsAuthenticated true; an identity without one
        // is anonymous no matter which claims it carries.
        //
        // The session-cap claim is part of what "signed in" means since Phase 3 — the accessor
        // treats a principal without one as past its cap and hands back no user id. These tests
        // are about claim-to-id mapping, so they carry a live cap and let SessionCapTests own
        // the cap rules themselves.
        var cap = DateTimeOffset.UtcNow.Add(AuthCookie.SessionCap).ToUnixTimeSeconds();

        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(claimType, value),
                new Claim(AuthCookie.SessionCapClaimType, cap.ToString(CultureInfo.InvariantCulture))
            ],
            authenticationType: "TestAuth"));
    }

    private static AuthenticationStateCurrentUserAccessor AccessorFor(ClaimsPrincipal principal)
        => new(new StubAuthenticationStateProvider(principal));

    private DbContextOptions<AppDbContext> SqliteOptions()
        => new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

    private void SeedProfile(Guid ownerId, string displayName)
    {
        using var context = new AppDbContext(SqliteOptions()) { CurrentUserId = ownerId };

        // Id and CreatedAt are supplied explicitly because their defaults are Postgres
        // functions (gen_random_uuid(), now()) that SQLite cannot evaluate.
        context.Profiles.Add(new Profile
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UnixEpoch
        });

        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class StubAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    /// <summary>
    /// Hands out contexts over the shared in-memory connection, standing in for the pooled
    /// factory that DI registers at runtime.
    /// </summary>
    private sealed class SqliteDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
