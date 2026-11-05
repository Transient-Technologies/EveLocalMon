namespace EveLocalMon.Core.Alerts;

/// <summary>What one channel actually did with one message. The session log reports this instead of
/// the settings, because "ntfy enabled" and "ntfy delivered" are different facts.</summary>
public enum ChannelResult
{
    /// <summary>Not enabled for this kind of alert, or no implementation is wired up.</summary>
    Disabled,
    Sent,

    /// <summary>Enabled, but muted. Only sound and ntfy are ever muted; the popup never is.</summary>
    Muted,

    /// <summary>Enabled, but dropped because the previous one went out too recently.</summary>
    RateLimited,
    Failed,
}

public sealed record DispatchOutcome(ChannelResult Sound, ChannelResult Popup, ChannelResult Ntfy)
{
    /// <summary>"sound · popup · ntfy sent", "popup sent · sound, ntfy muted", "ntfy rate-limited".
    /// Disabled channels are left out; if nothing was enabled at all it says so.</summary>
    public string Describe()
    {
        var groups = new List<string>();
        AddGroup(groups, ChannelResult.Sent, "sent");
        AddGroup(groups, ChannelResult.Muted, "muted");
        AddGroup(groups, ChannelResult.RateLimited, "rate-limited");
        AddGroup(groups, ChannelResult.Failed, "failed");

        return groups.Count == 0 ? "no channels enabled" : string.Join(" · ", groups);

        void AddGroup(List<string> into, ChannelResult result, string verb)
        {
            var names = new List<string>(3);
            if (Sound == result) names.Add("sound");
            if (Popup == result) names.Add("popup");
            if (Ntfy == result) names.Add("ntfy");

            if (names.Count > 0)
            {
                into.Add($"{string.Join(", ", names)} {verb}");
            }
        }
    }
}
