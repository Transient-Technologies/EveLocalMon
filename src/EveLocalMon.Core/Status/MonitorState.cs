namespace EveLocalMon.Core.Status;

public enum MonitorState
{
    Stopped,
    SetupNeeded,

    /// <summary>Watching, but the debounce window has not yet confirmed what Local looks like.</summary>
    Checking,
    Clear,
    Hostile,

    /// <summary>Local cannot be read in full. Deliberately its own state, never folded into Clear.</summary>
    Unreadable,
}
