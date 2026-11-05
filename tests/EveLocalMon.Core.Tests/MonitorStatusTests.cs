using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Status;

namespace EveLocalMon.Core.Tests;

public class MonitorStatusTests
{
    private static StableView View(
        bool settled = true,
        int threats = 0,
        ThreatBreakdown? breakdown = null,
        Readability readability = Readability.Full,
        int? members = 18,
        int visible = 18) =>
        new(settled, threats, breakdown ?? new ThreatBreakdown(threats, 0, 0, 0), readability, members, visible);

    private static MonitorStatus Compute(
        StableView? stable,
        bool running = true,
        bool hasRegion = true,
        bool hasCalibration = true,
        string? error = null) =>
        MonitorStatus.Compute(running, hasRegion, hasCalibration, stable, error);

    [Fact]
    public void NotRunningIsStoppedWhateverElseIsTrue()
    {
        MonitorStatus status = Compute(View(threats: 2), running: false, error: "boom");

        Assert.Equal(MonitorState.Stopped, status.State);
        Assert.Equal("Stopped", status.Word);
    }

    [Theory]
    [InlineData(false, true, "Select a region")]
    [InlineData(false, false, "Select a region")]
    [InlineData(true, false, "Calibrate the region")]
    public void MissingSetupIsSetupNeededEvenWithAStableView(bool hasRegion, bool hasCalibration, string reason)
    {
        MonitorStatus status = Compute(View(), hasRegion: hasRegion, hasCalibration: hasCalibration);

        Assert.Equal(MonitorState.SetupNeeded, status.State);
        Assert.Equal(reason, status.Reason);
    }

    [Fact]
    public void CaptureErrorIsUnreadableEvenBeforeAnythingHasSettled()
    {
        MonitorStatus status = Compute(View(settled: false), error: "access denied");

        Assert.Equal(MonitorState.Unreadable, status.State);
        Assert.Contains("access denied", status.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrUnsettledViewIsCheckingNeverClear(bool nullView)
    {
        MonitorStatus status = Compute(nullView ? null : View(settled: false));

        Assert.Equal(MonitorState.Checking, status.State);
        Assert.Equal("Checking", status.Word);
        Assert.NotEqual(MonitorState.Clear, status.State);
    }

    [Fact]
    public void AllReadableAndNoThreatsIsClear()
    {
        MonitorStatus status = Compute(View());

        Assert.Equal(MonitorState.Clear, status.State);
        Assert.Equal("All rows readable", status.Reason);
        Assert.Equal(18, status.InLocal);
        Assert.Equal(0, status.Threats);
    }

    [Fact]
    public void ThreatsAreHostileAndSayWhichKind()
    {
        MonitorStatus status = Compute(View(threats: 2, breakdown: new ThreatBreakdown(1, 1, 0, 0)));

        Assert.Equal(MonitorState.Hostile, status.State);
        Assert.Equal("1 red, 1 neutral", status.Reason);
        Assert.Equal(2, status.Threats);
    }

    [Fact]
    public void OverflowWithAThreatIsStillHostileAndStatesTheOverflow()
    {
        MonitorStatus status = Compute(View(threats: 1, readability: Readability.Overflow, members: 48, visible: 39));

        Assert.Equal(MonitorState.Hostile, status.State);
        Assert.Contains("list overflowing", status.Reason);
    }

    [Fact]
    public void OverflowWithoutThreatsIsUnreadableNotClear()
    {
        MonitorStatus status = Compute(View(readability: Readability.Overflow, members: 48, visible: 39));

        Assert.Equal(MonitorState.Unreadable, status.State);
        Assert.Equal("48 in Local, 39 readable", status.Reason);
    }

    [Fact]
    public void NotVisibleIsNeverClearAndHasNoThreatCount()
    {
        MonitorStatus status = Compute(View(readability: Readability.NotVisible, members: null, visible: 0));

        Assert.Equal(MonitorState.Unreadable, status.State);
        Assert.NotEqual(MonitorState.Clear, status.State);
        Assert.Null(status.Threats);
        Assert.Null(status.InLocal);
    }

    [Theory]
    [InlineData(Readability.Full, 18, 18, "18 / 18")]
    [InlineData(Readability.Overflow, 48, 39, "39 / 48")]
    [InlineData(Readability.Overflow, null, 39, "39 / ?")]
    [InlineData(Readability.NotVisible, null, 0, "0 / ?")]
    public void ReadableTextShowsWhatIsVisibleAgainstWhatExists(Readability readability, int? members, int visible, string expected)
    {
        MonitorStatus status = Compute(View(readability: readability, members: members, visible: visible));

        Assert.Equal(expected, status.Readable);
    }
}
