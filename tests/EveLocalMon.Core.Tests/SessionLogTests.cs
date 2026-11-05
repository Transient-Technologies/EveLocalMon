using EveLocalMon.Core.Alerts;

namespace EveLocalMon.Core.Tests;

public class SessionLogTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewestEntryComesFirst()
    {
        var log = new SessionLog();

        log.Add(Start, SessionEntryKind.Info, "first");
        log.Add(Start.AddSeconds(1), SessionEntryKind.Threat, "second", "detail");

        Assert.Equal(["second", "first"], log.Entries.Select(e => e.Title));
        Assert.Equal(SessionEntryKind.Threat, log.Entries[0].Kind);
        Assert.Equal("detail", log.Entries[0].Detail);
    }

    [Fact]
    public void CapsAtTwoHundredDroppingTheOldest()
    {
        var log = new SessionLog();

        for (int i = 0; i < 250; i++)
        {
            log.Add(Start.AddSeconds(i), SessionEntryKind.Info, $"entry {i}");
        }

        Assert.Equal(200, log.Count);
        Assert.Equal("entry 249", log.Entries[0].Title);
        Assert.Equal("entry 50", log.Entries[^1].Title);
    }

    [Fact]
    public void EntriesAreImmutableSnapshots()
    {
        var log = new SessionLog();
        log.Add(Start, SessionEntryKind.Info, "one");

        IReadOnlyList<SessionEntry> before = log.Entries;
        log.Add(Start, SessionEntryKind.Info, "two");

        Assert.Single(before);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void RaisesAddedForEachEntry()
    {
        var log = new SessionLog();
        var seen = new List<string>();
        log.Added += entry => seen.Add(entry.Title);

        log.Add(Start, SessionEntryKind.Info, "a");
        log.Add(Start, SessionEntryKind.Error, "b");

        Assert.Equal(["a", "b"], seen);
    }

    [Fact]
    public void ClearEmptiesTheLogAndRaisesChanged()
    {
        var log = new SessionLog();
        int changes = 0;
        log.Changed += () => changes++;
        log.Add(Start, SessionEntryKind.Info, "a");

        log.Clear();

        Assert.Equal(0, log.Count);
        Assert.Empty(log.Entries);
        Assert.Equal(2, changes);
    }
}
