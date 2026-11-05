using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Tests;

/// <summary>The debounced view the status header reads (doc/specs/M4-SPEC.md §4.1).</summary>
public class AlertStateStableTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Snapshot Snap(
        int enemy = 0,
        int neutral = 0,
        Readability readability = Readability.Full,
        int? header = 18,
        int rows = 18) =>
        new(
            Rows: [],
            OccupiedFullRows: rows,
            HasPartialRowWithContent: false,
            HeaderMemberCount: header,
            Readability: readability,
            Threats: new ThreatBreakdown(enemy, neutral, 0, 0));

    private static AlertState NewState(int stableFrames = 2) => new(new AlertStateOptions { StableFrames = stableFrames });

    [Fact]
    public void FreshStateIsNotSettled()
    {
        Assert.False(NewState().Stable.Settled);
    }

    [Fact]
    public void SettlesAfterTheDebounceWindowOfIdenticalReadings()
    {
        AlertState state = NewState();

        state.Update(Snap(), Now);
        Assert.False(state.Stable.Settled);

        state.Update(Snap(), Now);
        Assert.True(state.Stable.Settled);
        Assert.Equal(0, state.Stable.Threats);
        Assert.Equal(18, state.Stable.MemberCount);
        Assert.Equal(18, state.Stable.VisibleRows);
    }

    [Fact]
    public void FlickerFromTheFirstFrameNeverSettlesAsClear()
    {
        AlertState state = NewState();

        foreach (int enemy in new[] { 1, 0, 1, 0, 1, 0 })
        {
            state.Update(Snap(enemy: enemy), Now);
        }

        Assert.False(state.Stable.Settled);
    }

    [Fact]
    public void ASingleFrameThreatFlashChangesNeitherCountNorBreakdown()
    {
        AlertState state = NewState();
        state.Update(Snap(), Now);
        state.Update(Snap(), Now);

        state.Update(Snap(enemy: 1, neutral: 1), Now);

        Assert.Equal(0, state.Stable.Threats);
        Assert.Equal(0, state.Stable.Breakdown.Total);
    }

    [Fact]
    public void TwoAgreeingFramesChangeBothCountAndBreakdown()
    {
        AlertState state = NewState();
        state.Update(Snap(), Now);
        state.Update(Snap(), Now);

        state.Update(Snap(enemy: 1, neutral: 1), Now);
        state.Update(Snap(enemy: 1, neutral: 1), Now);

        Assert.Equal(2, state.Stable.Threats);
        Assert.Equal(new ThreatBreakdown(1, 1, 0, 0), state.Stable.Breakdown);
    }

    [Fact]
    public void BreakdownAndVisibleRowsComeFromTheLastAgreeingSnapshot()
    {
        AlertState state = NewState();
        state.Update(Snap(enemy: 2, rows: 20), Now);
        state.Update(Snap(enemy: 2, rows: 20), Now);

        // Same total, different mix and row count: it agrees with the stable total, so it is taken.
        state.Update(Snap(enemy: 1, neutral: 1, rows: 21), Now);
        Assert.Equal(new ThreatBreakdown(1, 1, 0, 0), state.Stable.Breakdown);
        Assert.Equal(21, state.Stable.VisibleRows);

        // A disagreeing frame is ignored entirely.
        state.Update(Snap(enemy: 5, rows: 3), Now);
        Assert.Equal(new ThreatBreakdown(1, 1, 0, 0), state.Stable.Breakdown);
        Assert.Equal(21, state.Stable.VisibleRows);
    }

    [Fact]
    public void ReadabilityAndMemberCountAreDebouncedToo()
    {
        AlertState state = NewState();
        state.Update(Snap(), Now);
        state.Update(Snap(), Now);

        state.Update(Snap(readability: Readability.Overflow, header: 48, rows: 39), Now);
        Assert.Equal(Readability.Full, state.Stable.Readability);

        state.Update(Snap(readability: Readability.Overflow, header: 48, rows: 39), Now);
        Assert.Equal(Readability.Overflow, state.Stable.Readability);
        Assert.Equal(48, state.Stable.MemberCount);
        Assert.Equal(39, state.Stable.VisibleRows);
    }

    [Fact]
    public void ReadingTheStableViewNeverChangesWhichEventsAreEmitted()
    {
        Snapshot[] sequence =
        [
            Snap(), Snap(), Snap(enemy: 1), Snap(enemy: 1), Snap(enemy: 2), Snap(enemy: 1), Snap(enemy: 1),
            Snap(readability: Readability.Overflow, header: 48, rows: 39),
            Snap(readability: Readability.Overflow, header: 48, rows: 39),
            Snap(), Snap(),
        ];

        List<AlertEvent> Run(bool readStable)
        {
            AlertState state = NewState();
            var events = new List<AlertEvent>();
            foreach (Snapshot snapshot in sequence)
            {
                events.AddRange(state.Update(snapshot, Now));
                if (readStable)
                {
                    _ = state.Stable;
                }
            }

            return events;
        }

        Assert.Equal(Run(readStable: false), Run(readStable: true));
    }
}
