using _10xnotes.Data;
using _10xnotes.Data.Entities;
using _10xnotes.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace _10xnotes.Generation;

/// <summary>
/// The daily per-user allowance for note generation.
/// </summary>
/// <remarks>
/// Data comes through <see cref="UserScopedDbContextFactory"/>, never an injected context or
/// context factory — <c>DataAccessBoundaryTests</c> fails the build otherwise, and a context
/// obtained the other way has no current user, so the ledger would read empty and write nothing.
/// </remarks>
public sealed class GenerationQuotaService(
    UserScopedDbContextFactory dbContextFactory,
    TimeProvider timeProvider,
    IOptions<AiOptions> options,
    ILogger<GenerationQuotaService> logger)
{
    private readonly AiOptions _options = options.Value;

    /// <summary>
    /// Takes one generation slot for today, or reports that the allowance is spent.
    /// </summary>
    /// <remarks>
    /// Reserves *before* the provider is called rather than recording after it succeeds. The
    /// other order lets a run of expensive failures — a timeout still bills for the tokens
    /// already produced — cost money without ever moving the counter.
    /// </remarks>
    /// <returns><c>true</c> when a slot was taken and the caller may generate.</returns>
    public async Task<bool> TryReserveAsync(CancellationToken cancellationToken = default)
    {
        // One retry, because the only realistic conflict is two requests inserting the first row
        // of the same day at once. The retry re-reads, so the loser of that race increments the
        // winner's row instead of failing the user for a race they cannot see.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await ReserveOnceAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (attempt == 0)
            {
                logger.LogInformation(
                    exception,
                    "Concurrent generation quota insert; re-reading the row and retrying once.");
            }
        }

        return false;
    }

    private async Task<bool> ReserveOnceAsync(CancellationToken cancellationToken)
    {
        var today = Today();

        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        // No owner filter written here: the global query filter scopes this to the signed-in
        // user, so this can only ever find that user's row.
        var quota = await db.GenerationQuotas
            .FirstOrDefaultAsync(q => q.UsageDate == today, cancellationToken);

        // Checked before the branch, not inside it: doing it per-branch once let the
        // create-the-first-row path skip the check entirely, so every user got one generation a
        // day for free no matter how the limit was set — including a limit of 0, which an
        // operator would reasonably expect to switch generation off.
        if ((quota?.Count ?? 0) >= _options.DailyGenerationLimit)
        {
            return false;
        }

        if (quota is null)
        {
            // Id and CreatedAt are stamped here rather than by a database default, so that
            // CreatedAt comes from the same clock that decided UsageDate above. OwnerId is
            // deliberately NOT set: AppDbContext stamps it from the signed-in user and overwrites
            // anything supplied, so ownership can never come from the caller.
            db.GenerationQuotas.Add(new GenerationQuota
            {
                Id = Guid.NewGuid(),
                UsageDate = today,
                Count = 1,
                CreatedAt = timeProvider.GetUtcNow()
            });
        }
        else
        {
            quota.Count++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Today in the display timezone, not UTC.
    /// </summary>
    /// <remarks>
    /// A UTC day boundary lands at 01:00 or 02:00 Polish time depending on daylight saving, so a
    /// UTC-keyed ledger would hand the user a fresh allowance in the middle of their evening and
    /// cut them off an hour into the next one.
    /// </remarks>
    private DateOnly Today() =>
        DateOnly.FromDateTime(timeProvider.ToDisplayTime(timeProvider.GetUtcNow()).DateTime);
}
