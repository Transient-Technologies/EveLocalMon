namespace EveLocalMon.Core.Alerts;

public enum SessionEntryKind
{
    Info,
    Threat,
    Unreadable,
    Clear,
    Error,
}

public sealed record SessionEntry(DateTimeOffset At, SessionEntryKind Kind, string Title, string Detail);

/// <summary>
/// What happened this run, newest first. In memory only: a restart starts clean, and persistent
/// history is M5's job. Capped so a long session behind a game cannot grow without bound.
/// </summary>
public sealed class SessionLog(int capacity = SessionLog.DefaultCapacity)
{
    public const int DefaultCapacity = 200;

    private readonly List<SessionEntry> _entries = [];

    public event Action<SessionEntry>? Added;

    /// <summary>Raised after every add or clear; a view that just shows the list can listen to this alone.</summary>
    public event Action? Changed;

    /// <summary>Newest first. A snapshot, so callers can enumerate it while the log keeps growing.</summary>
    public IReadOnlyList<SessionEntry> Entries => _entries.ToArray();

    public int Count => _entries.Count;

    public SessionEntry Add(DateTimeOffset at, SessionEntryKind kind, string title, string detail = "")
    {
        var entry = new SessionEntry(at, kind, title, detail);
        _entries.Insert(0, entry);

        if (_entries.Count > capacity)
        {
            _entries.RemoveRange(capacity, _entries.Count - capacity);
        }

        Added?.Invoke(entry);
        Changed?.Invoke();
        return entry;
    }

    public void Clear()
    {
        _entries.Clear();
        Changed?.Invoke();
    }
}
