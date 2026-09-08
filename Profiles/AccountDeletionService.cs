using _10xnotes.Data;
using Microsoft.EntityFrameworkCore;

namespace _10xnotes.Profiles;

/// <summary>
/// Deletes the signed-in user's identity row, letting the database cascades take everything else.
/// </summary>
/// <remarks>
/// One statement against <c>auth.users</c>. Its foreign keys cascade into <c>profiles</c>,
/// <c>source_materials</c>, <c>notes</c>, <c>note_events</c> and <c>generation_quotas</c>, plus
/// GoTrue's own <c>sessions</c>, <c>identities</c> and <c>refresh_tokens</c> — so this class needs
/// to know about none of them, and a table added later inherits the behaviour from its own foreign
/// key rather than from a list here that somebody has to remember to extend.
/// <para>
/// The app's <c>postgres</c> role has <c>DELETE</c> on <c>auth.users</c> — verified 2026-09-08 —
/// so no <c>service_role</c> key is involved. GoTrue offers no self-service delete endpoint; its
/// admin hard-delete path is <c>tx.Destroy(user)</c>, which is this same row delete plus cascade.
/// </para>
/// <para>
/// <strong>Raw SQL bypasses the global query filter by construction.</strong> That is exactly why
/// this method takes no id: the target is read from <see cref="AppDbContext.CurrentUserId"/>, which
/// <see cref="UserScopedDbContextFactory"/> set from the authenticated principal. There is no
/// parameter a caller could aim at another account, and adding one would remove the only thing
/// standing between this statement and someone else's data. If a future caller needs to delete a
/// different user, that is a different method with a different name and its own authorization —
/// not an overload of this one.
/// </para>
/// </remarks>
public sealed class AccountDeletionService(
    UserScopedDbContextFactory dbContextFactory,
    ILogger<AccountDeletionService> logger)
{
    /// <summary>
    /// Deletes the signed-in user and everything the cascades reach. Irreversible.
    /// </summary>
    /// <returns><c>true</c> when exactly one identity row was removed.</returns>
    public async Task<bool> DeleteCurrentAccountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        var userId = db.CurrentUserId;

        if (userId == Guid.Empty)
        {
            // Fail closed, matching StampOwners: an unauthenticated delete has no target, and a
            // statement with an empty guid would be a delete aimed at nothing at best.
            throw new InvalidOperationException(
                "Cannot delete an account without an authenticated user.");
        }

        // Interpolated-SQL execution, not string concatenation: EF turns the hole into a bound
        // parameter. The value is the context's own current user, so there is nothing here a
        // request could influence — but it stays parameterised regardless, because a guid that
        // becomes a literal today is the template somebody copies for a value that is not one.
        var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM auth.users WHERE id = {userId}",
            cancellationToken);

        if (rowsAffected != 1)
        {
            // Zero means the row was already gone. Worth a log — the cookie authenticated a user
            // the database does not have — but the caller still signs out, which is the right
            // outcome either way.
            logger.LogWarning(
                "Deleting the account removed {RowsAffected} rows from auth.users; expected 1.",
                rowsAffected);
        }

        return rowsAffected == 1;
    }
}
