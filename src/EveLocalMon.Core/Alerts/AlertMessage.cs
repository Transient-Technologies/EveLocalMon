namespace EveLocalMon.Core.Alerts;

public enum AlertKind
{
    ThreatIncreased,
    LocalClear,
    Unreadable,
    /// <summary>Local is too long to read in full. Routed exactly like <see cref="Unreadable"/>; split
    /// out only so the popup can draw a different icon.</summary>
    Overflow,
    Readable,
    MemberCountWhileUnreadable,
}

public enum AlertPriority
{
    Low,
    High,
    Urgent,
}

public sealed record AlertMessage(AlertKind Kind, string Title, string Body, AlertPriority Priority, DateTimeOffset At);
