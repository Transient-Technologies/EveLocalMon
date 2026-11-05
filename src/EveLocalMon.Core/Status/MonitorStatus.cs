using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Status;

/// <summary>
/// What the status header shows. One pure function decides it, so "never call an unreadable Local
/// clear" is tested in one place rather than re-derived in views. It reads <see cref="StableView"/>
/// (the debounced state the alerts use), not the latest frame: the header must not flicker with a
/// blinking flag and must never disagree with the alerts above which it sits.
/// </summary>
/// <param name="InLocal">The stable header count; null when it cannot be read.</param>
/// <param name="Threats">Null when Local is not visible: "?" rather than a confident 0.</param>
public sealed record MonitorStatus(
    MonitorState State,
    string Word,
    string Reason,
    int? InLocal,
    string Readable,
    int? Threats,
    ThreatBreakdown? Breakdown)
{
    public static MonitorStatus Compute(
        bool running,
        bool hasRegion,
        bool hasCalibration,
        StableView? stable,
        string? captureError)
    {
        if (!running)
        {
            return Simple(MonitorState.Stopped, "Stopped", "Not watching — alerts are off");
        }

        if (!hasRegion || !hasCalibration)
        {
            return Simple(MonitorState.SetupNeeded, "Setup needed", hasRegion ? "Calibrate the region" : "Select a region");
        }

        // Not debounced: a capture failure means there is no frame to debounce, and it must show at once.
        if (captureError is not null)
        {
            return Simple(MonitorState.Unreadable, "Unreadable", $"Capture failed: {captureError}");
        }

        if (stable is not { Settled: true })
        {
            return Simple(MonitorState.Checking, "Checking", "Reading Local…");
        }

        string readable = DescribeReadable(stable);
        int? threats = stable.Readability == Readability.NotVisible ? null : stable.Threats;

        // Above Overflow on purpose: a visible hostile in a list that is also overflowing is the more
        // urgent fact, and the overflow is still stated.
        if (stable.Threats > 0)
        {
            string reason = AlertMessageFactory.DescribeBreakdown(stable.Breakdown);
            if (stable.Readability == Readability.Overflow)
            {
                reason += " · list overflowing";
            }

            return new MonitorStatus(MonitorState.Hostile, "Hostile", reason, stable.MemberCount, readable, stable.Threats, stable.Breakdown);
        }

        return stable.Readability switch
        {
            Readability.NotVisible => new MonitorStatus(
                MonitorState.Unreadable, "Unreadable", "Local not visible — covered, minimised or moved",
                stable.MemberCount, readable, threats, null),
            Readability.Overflow => new MonitorStatus(
                MonitorState.Unreadable, "Unreadable",
                stable.MemberCount is { } members
                    ? $"{members} in Local, {stable.VisibleRows} readable"
                    : $"{stable.VisibleRows} readable, member count unreadable",
                stable.MemberCount, readable, threats, stable.Breakdown),
            _ => new MonitorStatus(
                MonitorState.Clear, "Clear", "All rows readable",
                stable.MemberCount, readable, threats, stable.Breakdown),
        };
    }

    /// <summary>"18 / 18" when everything is visible, "39 / 48" in overflow, "39 / ?" when the header
    /// count is unreadable, "0 / ?" when nothing is visible.</summary>
    private static string DescribeReadable(StableView stable)
    {
        if (stable.Readability == Readability.NotVisible)
        {
            return "0 / ?";
        }

        if (stable.Readability == Readability.Full)
        {
            return $"{stable.VisibleRows} / {stable.VisibleRows}";
        }

        return stable.MemberCount is { } total ? $"{stable.VisibleRows} / {total}" : $"{stable.VisibleRows} / ?";
    }

    private static MonitorStatus Simple(MonitorState state, string word, string reason) =>
        new(state, word, reason, null, "—", null, null);
}
