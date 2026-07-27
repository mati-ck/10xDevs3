namespace _10xnotes.Data;

/// <summary>
/// Shared, singleton record of how the startup migration went, so readiness can report a
/// failed migration without the app having to crash to signal it.
/// </summary>
public sealed class DatabaseMigrationState
{
    private volatile string? _failureMessage;
    private volatile bool _applied;

    public bool Applied => _applied;

    public string? FailureMessage => _failureMessage;

    public void MarkApplied()
    {
        _failureMessage = null;
        _applied = true;
    }

    public void MarkFailed(string message)
    {
        _failureMessage = message;
        _applied = false;
    }
}
