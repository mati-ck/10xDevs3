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
    /// <summary>
    /// How many times a contended reservation re-tries before giving up.
    /// </summary>
    /// <remarks>
    /// Each pass either takes a slot, refuses definitively, or loses a race to another request —
    /// and every lost race means some other request won, so the loop cannot spin without the
    /// ledger advancing. A handful of passes covers far more concurrency than one person with a
    /// few tabs can produce.
    /// </remarks>
    private const int MaxAttempts = 5;

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
        var today = Today();

        // Hoisted so it parameterizes into the statement below rather than being re-read per row.
        var limit = _options.DailyGenerationLimit;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            await using var db = await dbContextFactory.CreateAsync(cancellationToken);

            // One statement both tests the limit and takes the slot. Reading the row, checking the
            // count in C# and then writing count+1 is a read-modify-write with a window in the
            // middle: two concurrent requests both read 4, both pass a limit of 5, and both write
            // 5 — and nothing raises, because the UPDATE matches a row for each of them. OwnerId is
            // the entity's only concurrency token and it never changes, so it cannot detect that
            // conflict. Putting the limit into the WHERE clause makes the database serialize the
            // decision on the row, so the overrun cannot happen however many requests arrive at
            // once.
            //
            // No owner filter written here: ExecuteUpdate honours the global query filter, so this
            // can only ever touch the signed-in user's row.
            var taken = await db.GenerationQuotas
                .Where(q => q.UsageDate == today && q.Count < limit)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(q => q.Count, q => q.Count + 1),
                    cancellationToken);

            if (taken > 0)
            {
                return true;
            }

            // Nothing was updated: either today has no row yet, or the allowance is spent. Read
            // the count rather than merely testing existence — a row that appeared between the
            // UPDATE and this read was just created by a concurrent request and still has room,
            // and treating "a row exists" as "the allowance is spent" would refuse a caller that
            // had a slot waiting for it.
            var spent = await db.GenerationQuotas
                .Where(q => q.UsageDate == today)
                .Select(q => (int?)q.Count)
                .FirstOrDefaultAsync(cancellationToken);

            if (spent is not null)
            {
                if (spent >= limit)
                {
                    return false;
                }

                // Raced with whoever created the row; the next pass takes a slot from it.
                continue;
            }

            if (limit < 1)
            {
                return false;
            }

            try
            {
                // Id and CreatedAt are stamped here rather than by a database default, so that
                // CreatedAt comes from the same clock that decided UsageDate. OwnerId is
                // deliberately NOT set: AppDbContext stamps it from the signed-in user and
                // overwrites anything supplied, so ownership can never come from the caller.
                db.GenerationQuotas.Add(new GenerationQuota
                {
                    Id = Guid.NewGuid(),
                    UsageDate = today,
                    Count = 1,
                    CreatedAt = timeProvider.GetUtcNow()
                });

                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException exception)
            {
                // Lost the race to create the day's first row — the unique index on
                // (owner_id, usage_date) is what turns that into an exception rather than a second
                // row silently doubling the allowance. The next pass increments the winner's row.
                logger.LogInformation(
                    exception,
                    "Concurrent insert of the daily quota row; retrying against the winning row.");
            }
        }

        // Every pass lost a race. Refusing is the safe direction: the caller sees the quota
        // message and can click again, whereas granting would spend money on an unverified slot.
        logger.LogWarning(
            "Gave up reserving a generation slot after {Attempts} contended attempts.", MaxAttempts);

        return false;
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
