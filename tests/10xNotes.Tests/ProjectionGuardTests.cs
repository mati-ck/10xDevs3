using _10xnotes.Data;
using _10xnotes.Data.Entities;
using _10xnotes.Notes;
using _10xnotes.SourceMaterials;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace _10xNotes.Tests;

/// <summary>
/// Keeps both list queries from ever selecting the text columns they exist to avoid.
/// </summary>
/// <remarks>
/// The plan calls projection "a requirement, not an optimisation": <c>source_materials.content</c>
/// is unbounded <c>text</c> with a real ceiling of 128 K characters, and a note carries two 64 K
/// columns. A list of twenty materials read as entities is megabytes of text dragged into memory to
/// render a title. The current queries are correct by construction — <c>Select</c> precedes
/// <c>ToListAsync</c> — but "by construction" is not a guard: moving the <c>Select</c> after the
/// materialisation, or returning entities and mapping in the page, would pass every other test in
/// this suite while quietly restoring the cost. So these read the SQL that was actually emitted.
/// </remarks>
public sealed class ProjectionGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly List<string> _sql = [];
    private readonly Guid _owner = Guid.NewGuid();

    public ProjectionGuardTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = SqliteTestContext.Create(_connection, _owner);
        context.Database.EnsureCreated();

        var material = new SourceMaterial
        {
            Id = Guid.NewGuid(),
            Title = "Wykład o pamięci",
            Kind = SourceMaterialKind.MarkdownFile,
            OriginalFileName = "wyklad.md",
            Content = new string('x', 4096),
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.SourceMaterials.Add(material);

        context.Notes.Add(new Note
        {
            Id = Guid.NewGuid(),
            SourceMaterialId = material.Id,
            Title = "Wykład o pamięci",
            Content = new string('y', 4096),
            DraftContent = new string('z', 4096),
            PromptVersion = "v2",
            Model = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        context.SaveChanges();
    }

    private UserScopedDbContextFactory RecordingFactory() =>
        new(new SqliteTestContext.Factory(_connection, line => _sql.Add(line)),
            new StubCurrentUserAccessor(_owner));

    /// <summary>The SELECT statements captured so far, lower-cased for matching.</summary>
    private string CapturedSql => string.Join("\n", _sql).ToLowerInvariant();

    [Fact]
    public async Task The_note_list_query_does_not_select_any_note_body()
    {
        var service = new NoteService(
            RecordingFactory(),
            TimeProvider.System,
            NullLogger<NoteService>.Instance);

        var notes = await service.ListAsync();

        Assert.Single(notes);
        // "content" bare, not "\"content\"" or ".content": the provider quotes identifiers and
        // the naming convention differs between SQLite and Postgres, so anchoring on punctuation
        // makes the assertion unfalsifiable. No column, table or parameter in either query
        // legitimately contains the substring, so its mere presence means a body column got in.
        Assert.DoesNotContain("content", CapturedSql);
    }

    [Fact]
    public async Task The_material_list_query_does_not_select_the_material_body()
    {
        var service = new SourceMaterialService(RecordingFactory());

        var materials = await service.ListAsync();

        Assert.Single(materials);
        Assert.DoesNotContain("content", CapturedSql);
    }

    public void Dispose() => _connection.Dispose();
}
