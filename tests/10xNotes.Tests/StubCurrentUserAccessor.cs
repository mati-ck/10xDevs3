using _10xnotes.Data;

namespace _10xNotes.Tests;

/// <summary>Stands in for the real accessor so tests can pick who is asking.</summary>
internal sealed class StubCurrentUserAccessor(Guid? userId) : ICurrentUserAccessor
{
    public Guid? UserId { get; } = userId;
}
