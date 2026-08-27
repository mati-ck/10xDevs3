using _10xnotes.Data;
using _10xnotes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace _10xnotes.Notes;

/// <summary>
/// Everything that reads or writes a saved note, so no page talks to the database itself.
/// </summary>
/// <remarks>
/// Data comes through <see cref="UserScopedDbContextFactory"/>, never an injected context or
/// context factory — <c>DataAccessBoundaryTests</c> fails the build otherwise, and a context
/// obtained the other way has no current user, so every read comes back empty and every write
/// throws.
/// <para>
/// <see cref="AcceptAsync"/> and <see cref="UpdateAsync"/> are separate methods rather than one
/// with a flag, because they mean different things to the acceptance measurement: accepting a
/// fresh draft is what the PRD counts, re-saving an existing note is not. See
/// <see cref="NoteEvent"/> for why conflating them lets the ratio exceed 100%.
/// </para>
/// <para>
/// <c>OwnerId</c> is never assigned here. <c>AppDbContext.StampOwners</c> takes it from the
/// signed-in user and overwrites anything supplied, so ownership can never come from a caller.
/// </para>
/// </remarks>
public sealed class NoteService(
    UserScopedDbContextFactory dbContextFactory,
    TimeProvider timeProvider,
    ILogger<NoteService> logger)
{
    /// <summary>
    /// The saved note for a material, or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    /// What the material page needs on entry: whether a save would replace work the user already
    /// accepted, which is the one case that has to ask before proceeding.
    /// </remarks>
    public async Task<Note?> GetByMaterialAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        // No owner filter written here or anywhere below: the global query filter scopes every
        // query by the signed-in user, so another user's id simply finds nothing.
        return await db.Notes.FirstOrDefaultAsync(n => n.SourceMaterialId == materialId, cancellationToken);
    }

    /// <summary>
    /// Every note the signed-in user has saved, newest save first, as list rows rather than
    /// entities.
    /// </summary>
    /// <remarks>
    /// Projects into <see cref="NoteListItem"/> before materialising, so the SQL never selects
    /// <c>content</c> or <c>draft_content</c> — 64 KB each, and the list shows neither.
    /// <para>
    /// No owner filter is written here, exactly as everywhere else in this class: the global
    /// query filter is what makes the list private, and it is what the isolation test proves.
    /// The query is deliberately unbounded — see the plan's Performance Considerations.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<NoteListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        return await db.Notes
            // Id breaks the tie: updated_at defaults to now() in Postgres, and two notes stamped
            // in the same millisecond would otherwise come back in an order that can change
            // between refreshes.
            .OrderByDescending(n => n.UpdatedAt)
            .ThenBy(n => n.Id)
            .Select(n => new NoteListItem(n.Id, n.Title, n.UpdatedAt, n.SourceMaterialId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The note behind <c>/notes/{id}</c>, or <c>null</c> when it is missing or not theirs.</summary>
    public async Task<Note?> GetAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        return await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);
    }

    /// <summary>
    /// Accepts a freshly generated draft: writes the note for this material, replacing any note
    /// already there, and appends the <see cref="NoteEventKind.Saved"/> event that counts as the
    /// acceptance.
    /// </summary>
    /// <param name="draft">The model's output before the user edited it, stored for measurement.</param>
    public async Task<NoteSaveResult> AcceptAsync(
        Guid materialId,
        string? title,
        string? content,
        string draft,
        string promptVersion,
        string model,
        CancellationToken cancellationToken = default)
    {
        // Validated here rather than trusting the page. The editor enforces the same rules, but
        // the page is not the only possible caller and the limits protect the database, not the UI.
        var validation = NoteValidator.Validate(title, content);

        if (!validation.Succeeded)
        {
            return NoteSaveResult.FromValidation(validation.FailureReason);
        }

        // Two passes at most. The unique index on source_material_id is what makes the 1:1
        // relationship real, so two tabs accepting at once must produce one row rather than an
        // unhandled exception — the loser retries as an update against the winner's row. A second
        // failure returns rather than throws, for the reason NoteSaveResult documents.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = await dbContextFactory.CreateAsync(cancellationToken);

            var materialExists = await db.SourceMaterials
                .AnyAsync(m => m.Id == materialId, cancellationToken);

            if (!materialExists)
            {
                return NoteSaveResult.Failure(NoteSaveFailure.NotFound);
            }

            var now = timeProvider.GetUtcNow();

            var note = await db.Notes
                .FirstOrDefaultAsync(n => n.SourceMaterialId == materialId, cancellationToken);

            if (note is null)
            {
                note = new Note
                {
                    // Id and the timestamps are stamped here rather than left to the column
                    // defaults, so both timestamps on a new row come from the same clock. The
                    // defaults stay declared as a backstop for any future insert that omits them.
                    Id = Guid.NewGuid(),
                    SourceMaterialId = materialId,
                    CreatedAt = now
                };

                db.Notes.Add(note);
            }

            note.Title = validation.Title;
            note.Content = validation.Content;
            note.DraftContent = draft;
            note.PromptVersion = promptVersion;
            note.Model = model;
            note.UpdatedAt = now;

            // Added in the same SaveChanges as the note, so the ledger and the note it counts
            // cannot disagree: if the write fails, neither happened.
            db.NoteEvents.Add(new NoteEvent
            {
                Id = Guid.NewGuid(),
                SourceMaterialId = materialId,
                Kind = NoteEventKind.Saved,
                PromptVersion = promptVersion,
                Model = model,
                DraftLength = draft.Length,
                SavedLength = validation.Content.Length,
                OccurredAt = now
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return NoteSaveResult.Success(note);
            }
            catch (DbUpdateException exception) when (attempt == 0)
            {
                // Lost the race to create this material's first note. The next pass finds the
                // winner's row and updates it, which is what "saving replaces" means anyway.
                logger.LogInformation(
                    exception,
                    "Concurrent accept for material {MaterialId}; retrying against the winning row.",
                    materialId);
            }
            catch (DbUpdateException exception)
            {
                logger.LogError(
                    exception,
                    "Saving the note for material {MaterialId} failed twice.",
                    materialId);

                return NoteSaveResult.Failure(NoteSaveFailure.Conflict);
            }
        }

        return NoteSaveResult.Failure(NoteSaveFailure.Conflict);
    }

    /// <summary>
    /// Re-saves an already accepted note from <c>/notes/{id}</c>.
    /// </summary>
    /// <remarks>
    /// Appends no ledger event, on purpose. This is the same acceptance being corrected, not a new
    /// one — counting it would raise the numerator against an unchanged denominator, and fixing a
    /// typo three times would report an acceptance rate above 100%.
    /// <para>
    /// <c>DraftContent</c>, <c>PromptVersion</c> and <c>Model</c> are left alone: they describe
    /// the generation this note came from, and editing the text does not change which draft it
    /// started as.
    /// </para>
    /// </remarks>
    public async Task<NoteSaveResult> UpdateAsync(
        Guid noteId,
        string? title,
        string? content,
        CancellationToken cancellationToken = default)
    {
        var validation = NoteValidator.Validate(title, content);

        if (!validation.Succeeded)
        {
            return NoteSaveResult.FromValidation(validation.FailureReason);
        }

        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

        if (note is null)
        {
            return NoteSaveResult.Failure(NoteSaveFailure.NotFound);
        }

        note.Title = validation.Title;
        note.Content = validation.Content;
        note.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return NoteSaveResult.Success(note);
    }

    /// <summary>
    /// Records that a generation finished and produced a draft — the denominator of the
    /// acceptance rate.
    /// </summary>
    /// <remarks>
    /// Called only after a stream completes successfully. A failed, timed-out or cancelled
    /// generation produces nothing anyone could accept, so counting it would depress the ratio
    /// for something that is not the model's output quality.
    /// <para>
    /// A write failure is logged and swallowed rather than surfaced. This runs immediately after
    /// a note the user is looking at finished streaming, and losing that note over a ledger row
    /// is a far worse trade than a gap in a measurement nothing reads in real time — the log is
    /// what makes such a gap findable.
    /// </para>
    /// </remarks>
    public async Task RecordGenerationAsync(
        Guid materialId,
        string draft,
        string promptVersion,
        string model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbContextFactory.CreateAsync(cancellationToken);

            db.NoteEvents.Add(new NoteEvent
            {
                Id = Guid.NewGuid(),
                SourceMaterialId = materialId,
                Kind = NoteEventKind.Generated,
                PromptVersion = promptVersion,
                Model = model,
                DraftLength = draft.Length,
                // Nothing has been saved yet — that is the whole point of the two events being
                // separate rows.
                SavedLength = 0,
                OccurredAt = timeProvider.GetUtcNow()
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Deliberately silent, and deliberately caught *here* rather than at the call site.
            // The user navigating away mid-write is expected rather than a fault, so it is not
            // worth a log line — but it must not escape, because the only caller runs inside a
            // handler whose catch clears the note panel. Keeping the guarantee in this method
            // means a second caller inherits it instead of having to know to re-implement it.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Recording the generation of a note for material {MaterialId} failed; the acceptance "
                + "denominator is now short by one.",
                materialId);
        }
    }
}
