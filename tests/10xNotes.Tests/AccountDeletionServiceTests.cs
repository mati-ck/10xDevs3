using System.Reflection;
using _10xnotes.Data;
using _10xnotes.Profiles;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace _10xNotes.Tests;

/// <summary>
/// Guards the only irreversible operation in the product.
/// </summary>
/// <remarks>
/// What these tests can prove is that the statement is aimed at the signed-in user and at nobody
/// else: the SQL is read back through the provider's log hook, the way <c>ProjectionGuardTests</c>
/// reads back its projections, because "correct by construction" is not a guard once somebody
/// adds a parameter.
/// <para>
/// What they cannot prove is the cascade. It lives in foreign keys into <c>auth.users</c>, and the
/// SQLite harness has no such table — so <c>DELETE FROM auth.users</c> is never executed here, only
/// inspected. The cascade's proof is the manual checklist against a throwaway account on the real
/// project, and nothing in this file should be read as covering it.
/// </para>
/// </remarks>
public sealed class AccountDeletionServiceTests : IDisposable
{
    private static readonly Guid Owner = new("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection;
    private readonly List<string> _sql = [];

    public AccountDeletionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = SqliteTestContext.Create(_connection, Owner);
        context.Database.EnsureCreated();
    }

    /// <summary>The SQL the provider actually emitted, lower-cased for matching.</summary>
    private string CapturedSql => string.Join("\n", _sql).ToLowerInvariant();

    /// <summary>
    /// The statement targets the identity row and lets the foreign keys do the rest — it must not
    /// enumerate the owned tables itself, or a table added later would be silently left behind.
    /// </summary>
    [Fact]
    public async Task The_delete_targets_the_auth_users_row()
    {
        await RunAndIgnoreMissingTable();

        Assert.Contains("delete from auth.users", CapturedSql);
        Assert.Contains("where id =", CapturedSql);

        foreach (var table in new[] { "profiles", "source_materials", "notes", "note_events", "generation_quotas" })
        {
            Assert.DoesNotContain(table, CapturedSql);
        }
    }

    /// <summary>
    /// A parameter, never a literal. The value here is the context's own current user and so is
    /// not attacker-influenced today — but a guid inlined as a literal is the template somebody
    /// copies for a value that is.
    /// </summary>
    [Fact]
    public async Task The_user_id_travels_as_a_parameter_and_not_as_a_literal()
    {
        await RunAndIgnoreMissingTable();

        var statement = CapturedSql[CapturedSql.IndexOf("delete from auth.users", StringComparison.Ordinal)..];

        Assert.Contains("@p0", statement);
        Assert.DoesNotContain(Owner.ToString().ToLowerInvariant(), statement);
    }

    /// <summary>
    /// Whose id it is. Raw SQL bypasses the owner query filter, so nothing downstream re-checks
    /// this — the statement deletes exactly the account named in that parameter, and this is the
    /// only test that reads which one.
    /// </summary>
    [Fact]
    public async Task The_parameter_carries_the_signed_in_users_id()
    {
        await RunAndIgnoreMissingTable();

        Assert.Contains($"@p0='{Owner.ToString().ToLowerInvariant()}'", CapturedSql);
    }

    /// <summary>
    /// And a different signed-in user gets their own id, not a value cached from the first
    /// context — the mistake that would make this delete somebody else's account.
    /// </summary>
    [Fact]
    public async Task A_different_signed_in_user_is_deleted_by_their_own_id()
    {
        var other = new Guid("22222222-2222-2222-2222-222222222222");

        try
        {
            await CreateService(other).DeleteCurrentAccountAsync();
        }
        catch (SqliteException)
        {
            // Expected: no such table auth.users.
        }

        Assert.Contains($"@p0='{other.ToString().ToLowerInvariant()}'", CapturedSql);
        Assert.DoesNotContain(Owner.ToString().ToLowerInvariant(), CapturedSql);
    }

    /// <summary>
    /// Fail closed, matching StampOwners. An unauthenticated delete has no target, and it must
    /// not become a statement aimed at the empty guid.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_delete_is_refused_before_any_sql()
    {
        var service = CreateService(userId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteCurrentAccountAsync());

        Assert.DoesNotContain("delete", CapturedSql);
    }

    /// <summary>
    /// The load-bearing property of the whole class: raw SQL bypasses the global query filter, so
    /// the only thing keeping this statement off another account is that there is no way to name
    /// one. If this test fails because a parameter was added, that parameter is the bug.
    /// </summary>
    [Fact]
    public void The_signature_offers_no_way_to_name_another_user()
    {
        var method = typeof(AccountDeletionService)
            .GetMethod(nameof(AccountDeletionService.DeleteCurrentAccountAsync))!;

        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(CancellationToken), parameter.ParameterType);

        // And no other public entry point takes a user id either.
        var offenders = typeof(AccountDeletionService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Guid)))
            .Select(m => m.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "AccountDeletionService must expose no way to aim the delete at a user other than the "
            + "signed-in one — raw SQL bypasses the owner query filter, so the absent parameter is "
            + "the guarantee. Offending members: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The service must take the sanctioned seam like every other service, so the context it uses
    /// is one that had the signed-in user applied to it.
    /// </summary>
    [Fact]
    public void The_service_takes_the_sanctioned_seam()
    {
        var parameters = typeof(AccountDeletionService).GetConstructors().Single().GetParameters();

        Assert.Contains(parameters, p => p.ParameterType == typeof(UserScopedDbContextFactory));
    }

    /// <summary>
    /// SQLite has no <c>auth.users</c>, which is the whole reason the cascade is a manual check.
    /// The statement is still emitted and logged before it fails, so everything above can read it.
    /// </summary>
    private async Task RunAndIgnoreMissingTable()
    {
        try
        {
            await CreateService(Owner).DeleteCurrentAccountAsync();
        }
        catch (SqliteException)
        {
            // Expected: no such table auth.users.
        }
    }

    private AccountDeletionService CreateService(Guid? userId) =>
        new(new UserScopedDbContextFactory(
                new SqliteTestContext.Factory(_connection, line => _sql.Add(line), logParameterValues: true),
                new StubCurrentUserAccessor(userId)),
            NullLogger<AccountDeletionService>.Instance);

    public void Dispose() => _connection.Dispose();
}
