using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Alerts;

/// <summary>
/// Turns a stream of <see cref="Snapshot"/>s into alert events. Debounces threats, readability
/// and header member count independently, so a single blinking criminal/suspect/war-target flag
/// (which can flip a row between Enemy, NoIcon and Unknown frame to frame) doesn't itself cause
/// repeated alerts. Rate limiting per channel belongs to the M2 dispatcher, not here.
/// </summary>
public sealed class AlertState
{
    private readonly AlertStateOptions _options;
    private readonly StableTracker<int> _threats;
    private readonly StableTracker<Readability> _readability;
    private readonly StableTracker<int?> _memberCount;
    private ThreatBreakdown _agreedBreakdown = new(0, 0, 0, 0);
    private int _agreedVisibleRows;

    public AlertState(AlertStateOptions options)
    {
        _options = options;
        _threats = new StableTracker<int>(0, options.StableFrames);
        _readability = new StableTracker<Readability>(Readability.Full, options.StableFrames);
        _memberCount = new StableTracker<int?>(null, options.StableFrames);
    }

    /// <summary>The debounced view the status header shows, so the header and the alerts it sits above
    /// agree by construction. Reading it never affects which events are emitted.</summary>
    public StableView Stable => new(
        Settled: _threats.Confirmed && _readability.Confirmed,
        _threats.Stable,
        _agreedBreakdown,
        _readability.Stable,
        _memberCount.Stable,
        _agreedVisibleRows);

    public IReadOnlyList<AlertEvent> Update(Snapshot snapshot, DateTimeOffset now)
    {
        var events = new List<AlertEvent>();

        if (_threats.Update(snapshot.Threats.Total, out int previousThreats))
        {
            if (snapshot.Threats.Total > previousThreats)
            {
                events.Add(new ThreatIncreased(previousThreats, snapshot.Threats.Total, snapshot.Threats, now));
            }
            else if (snapshot.Threats.Total == 0 && previousThreats > 0 && _options.EmitLocalClear)
            {
                events.Add(new LocalClear(now));
            }
        }

        if (_readability.Update(snapshot.Readability, out Readability previousReadability))
        {
            events.Add(new ReadabilityChanged(previousReadability, snapshot.Readability, snapshot.HeaderMemberCount, snapshot.OccupiedFullRows, now));
        }

        // A null reading (header digits unreadable this frame) never touches the tracker —
        // it neither resets progress toward the current pending value nor counts toward it.
        if (snapshot.HeaderMemberCount is { } rawCount
            && _memberCount.Update(rawCount, out int? previousCount)
            && previousCount is { } previous
            && rawCount > previous
            && _readability.Stable != Readability.Full
            && _options.AlertOnMemberIncreaseWhileUnreadable)
        {
            events.Add(new MemberCountIncreasedWhileUnreadable(previous, rawCount, now));
        }

        // Only a frame that agrees with the stable values may supply the breakdown text, so a
        // one-frame flash of a different category never reaches the header.
        if (snapshot.Threats.Total == _threats.Stable && snapshot.Readability == _readability.Stable)
        {
            _agreedBreakdown = snapshot.Threats;
            _agreedVisibleRows = snapshot.OccupiedFullRows;
        }

        return events;
    }

    /// <summary>Tracks a raw value stream and reports the debounced "stable" value: a new
    /// reading only takes effect once it has repeated <c>stableFrames</c> times in a row.</summary>
    private sealed class StableTracker<T>
    {
        private readonly int _stableFrames;
        private T _pendingValue;
        private int _pendingCount;
        private T? _lastRaw;
        private bool _hasLastRaw;
        private int _runCount;

        public StableTracker(T initialStable, int stableFrames)
        {
            Stable = initialStable;
            _stableFrames = stableFrames;
            _pendingValue = initialStable;
            _pendingCount = stableFrames; // the initial value counts as already stable
        }

        public T Stable { get; private set; }

        /// <summary>True once a run of <c>stableFrames</c> identical readings has been seen. Unlike
        /// <see cref="Stable"/> the initial value does not count, so "nothing seen yet" and
        /// "seen, and it really is zero" stay distinguishable.</summary>
        public bool Confirmed { get; private set; }

        /// <summary>Feeds one new raw reading. Returns true (and sets <paramref name="previous"/>)
        /// only on the update where Stable actually changes.</summary>
        public bool Update(T raw, out T previous)
        {
            previous = Stable;

            if (_hasLastRaw && EqualityComparer<T>.Default.Equals(raw, _lastRaw))
            {
                _runCount++;
            }
            else
            {
                _lastRaw = raw;
                _hasLastRaw = true;
                _runCount = 1;
            }

            if (_runCount >= _stableFrames)
            {
                Confirmed = true;
            }

            if (EqualityComparer<T>.Default.Equals(raw, _pendingValue))
            {
                _pendingCount++;
            }
            else
            {
                _pendingValue = raw;
                _pendingCount = 1;
            }

            if (_pendingCount >= _stableFrames && !EqualityComparer<T>.Default.Equals(_pendingValue, Stable))
            {
                Stable = _pendingValue;
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// The debounced state of Local. <paramref name="Breakdown"/> and <paramref name="VisibleRows"/> come
/// from the latest frame that agreed with the stable threat count and readability.
/// </summary>
/// <param name="Settled">False until the threat count and readability have each been read the same way
/// for the debounce window. Before that the header must say it is still checking, not "clear".</param>
public sealed record StableView(
    bool Settled,
    int Threats,
    ThreatBreakdown Breakdown,
    Readability Readability,
    int? MemberCount,
    int VisibleRows);
