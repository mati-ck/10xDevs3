using _10xnotes.Auth;
using _10xnotes.Data;
using Microsoft.EntityFrameworkCore;

namespace _10xnotes.Profiles;

/// <summary>Why a profile update did not happen, in terms the UI can act on.</summary>
public enum ProfileUpdateFailure
{
    None = 0,

    /// <summary>The name exceeds <see cref="DisplayNameValidator.MaxLength"/> once trimmed.</summary>
    NameTooLong,

    /// <summary>
    /// No profile row for this user. The <c>on_auth_user_created</c> trigger guarantees one, so
    /// this means the trigger is gone or the account is — not that a row should now be created.
    /// </summary>
    ProfileMissing
}

/// <summary>Outcome of updating the signed-in user's profile.</summary>
public sealed record ProfileUpdateResult
{
    private ProfileUpdateResult() { }

    public bool Succeeded { get; private init; }

    /// <summary>The name as stored — trimmed, or <c>null</c> when cleared.</summary>
    public string? DisplayName { get; private init; }

    public ProfileUpdateFailure FailureReason { get; private init; }

    public static ProfileUpdateResult Success(string? displayName) =>
        new() { Succeeded = true, DisplayName = displayName };

    public static ProfileUpdateResult Failure(ProfileUpdateFailure reason) =>
        new() { Succeeded = false, FailureReason = reason };
}

/// <summary>
/// Everything that reads or writes the signed-in user's profile, so no page talks to the
/// database itself.
/// </summary>
/// <remarks>
/// Data comes through <see cref="UserScopedDbContextFactory"/>, never an injected context or
/// context factory — <c>DataAccessBoundaryTests</c> fails the build otherwise. No owner filter is
/// written here: the global query filter scopes every read, and <c>StampOwners</c> refuses any
/// write aimed at a row belonging to someone else.
/// <para>
/// Nothing here creates a profile. <c>Migrations/20260727200717_AddHandleNewUserTrigger</c> puts a
/// row in for every account, including ones made in the Supabase dashboard, so a missing row is a
/// broken invariant to report rather than a case to paper over — creating one here would hide the
/// trigger having been dropped.
/// </para>
/// </remarks>
public sealed class ProfileService(UserScopedDbContextFactory dbContextFactory)
{
    /// <summary>
    /// The signed-in user's display name, or <c>null</c> when they have not set one.
    /// </summary>
    public async Task<string?> GetDisplayNameAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        return await db.Profiles
            .Select(p => p.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The display name for a user who has just proved their credentials but is not signed in yet.
    /// </summary>
    /// <remarks>
    /// The sign-in POST is the one place the ordinary read cannot work: the cookie has not been
    /// written, so the current-user accessor reports nobody and the global filter matches nothing.
    /// See <see cref="UserScopedDbContextFactory.CreateForAsync"/> for why that cannot be worked
    /// around by reordering.
    /// <para>
    /// <paramref name="userId"/> must come from GoTrue's verified response and from nowhere else.
    /// It is not a parameter the user can influence at any caller that exists today, and it must
    /// not become one — everything else uses <see cref="GetDisplayNameAsync"/>.
    /// </para>
    /// </remarks>
    public async Task<string?> GetDisplayNameAtSignInAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateForAsync(userId, cancellationToken);

        return await db.Profiles
            .Select(p => p.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Sets or clears the signed-in user's display name.
    /// </summary>
    /// <remarks>
    /// Validated here rather than trusting the page. The form applies the same rule, but the page
    /// is not the only possible caller and the limit protects the column, not the UI.
    /// </remarks>
    public async Task<ProfileUpdateResult> SetDisplayNameAsync(
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var validation = DisplayNameValidator.Validate(displayName);

        if (!validation.Succeeded)
        {
            return ProfileUpdateResult.Failure(ProfileUpdateFailure.NameTooLong);
        }

        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        var profile = await db.Profiles.FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return ProfileUpdateResult.Failure(ProfileUpdateFailure.ProfileMissing);
        }

        profile.DisplayName = validation.DisplayName;

        await db.SaveChangesAsync(cancellationToken);

        return ProfileUpdateResult.Success(validation.DisplayName);
    }
}
