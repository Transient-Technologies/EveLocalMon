using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Tests;

public class DispatchOutcomeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 19, 0, 0, TimeSpan.Zero);

    private static ThreatIncreased Threat(int previous = 0, int current = 1) =>
        new(previous, current, new ThreatBreakdown(current, 0, 0, 0), Start);

    private sealed class FakeSound : ISoundPlayer
    {
        public void Play(byte[] wav)
        {
        }
    }

    private sealed class FakePresenter : IAlertPresenter
    {
        public Exception? Throw { get; set; }

        public void Present(AlertMessage message)
        {
            if (Throw is not null) throw Throw;
        }
    }

    private sealed class FakeNtfy : INtfyClient
    {
        public bool Result { get; set; } = true;

        public Task<bool> SendAsync(AlertMessage message, NtfyOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (AlertDispatcher Dispatcher, FakePresenter Popup, FakeNtfy Ntfy, TestTime Time) Build(AlertSettings? settings = null)
    {
        var popup = new FakePresenter();
        var ntfy = new FakeNtfy();
        var time = new TestTime();
        var dispatcher = new AlertDispatcher(new FakeSound(), popup, ntfy, time)
        {
            Settings = settings ?? new AlertSettings { NtfyTopic = "topic" },
        };

        return (dispatcher, popup, ntfy, time);
    }

    private static AlertMessage Message(AlertEvent alertEvent) => AlertMessageFactory.Create(alertEvent);

    [Fact]
    public async Task EnabledChannelsReportSent()
    {
        var (dispatcher, _, _, _) = Build();

        DispatchOutcome outcome = await dispatcher.DispatchAsync(Message(Threat()));

        Assert.Equal(new DispatchOutcome(ChannelResult.Sent, ChannelResult.Sent, ChannelResult.Sent), outcome);
        Assert.Equal("sound, popup, ntfy sent", outcome.Describe());
    }

    [Fact]
    public async Task ChannelsNotEnabledForTheKindReportDisabled()
    {
        var (dispatcher, _, _, _) = Build();

        DispatchOutcome outcome = await dispatcher.DispatchAsync(Message(new LocalClear(Start)));

        Assert.Equal(ChannelResult.Disabled, outcome.Sound);
        Assert.Equal(ChannelResult.Sent, outcome.Popup);
        Assert.Equal(ChannelResult.Disabled, outcome.Ntfy);
    }

    [Fact]
    public async Task MutedKeepsThePopupAndReportsSoundAndNtfyMuted()
    {
        var (dispatcher, _, _, _) = Build();
        dispatcher.MuteFor(TimeSpan.FromMinutes(15));

        DispatchOutcome outcome = await dispatcher.DispatchAsync(Message(Threat()));

        Assert.Equal(ChannelResult.Sent, outcome.Popup);
        Assert.Equal(ChannelResult.Muted, outcome.Sound);
        Assert.Equal(ChannelResult.Muted, outcome.Ntfy);
        Assert.Equal("popup sent · sound, ntfy muted", outcome.Describe());
    }

    [Fact]
    public async Task ASecondAlertInsideTheWindowIsRateLimited()
    {
        var (dispatcher, _, _, time) = Build();

        await dispatcher.DispatchAsync(Message(Threat(0, 1)));
        time.Now = Start.AddMilliseconds(1200); // past the popup's 1 s window, inside sound's 3 s and ntfy's 15 s
        DispatchOutcome second = await dispatcher.DispatchAsync(Message(Threat(1, 2)));

        Assert.Equal(ChannelResult.Sent, second.Popup);
        Assert.Equal(ChannelResult.RateLimited, second.Sound);
        Assert.Equal(ChannelResult.RateLimited, second.Ntfy);
        Assert.Equal("popup sent · sound, ntfy rate-limited", second.Describe());
    }

    [Fact]
    public async Task AThrowingPresenterFailsAloneAndTheOthersStillSend()
    {
        var (dispatcher, popup, _, _) = Build();
        popup.Throw = new InvalidOperationException("no window");
        var failures = new List<string>();
        dispatcher.ChannelFailed += failures.Add;

        DispatchOutcome outcome = await dispatcher.DispatchAsync(Message(Threat()));

        Assert.Equal(ChannelResult.Failed, outcome.Popup);
        Assert.Equal(ChannelResult.Sent, outcome.Sound);
        Assert.Equal(ChannelResult.Sent, outcome.Ntfy);
        Assert.Contains(failures, f => f.Contains("no window"));
    }

    [Fact]
    public async Task ANtfyThatReturnsFalseIsFailed()
    {
        var (dispatcher, _, ntfy, _) = Build();
        ntfy.Result = false;

        DispatchOutcome outcome = await dispatcher.DispatchAsync(Message(Threat()));

        Assert.Equal(ChannelResult.Failed, outcome.Ntfy);
        Assert.Equal(ChannelResult.Sent, outcome.Popup);
    }

    [Fact]
    public async Task NtfyWithoutATopicIsFailed()
    {
        var (dispatcher, _, _, _) = Build(new AlertSettings { NtfyTopic = null });

        DispatchOutcome outcome = await dispatcher.DispatchAsync(Message(Threat()));

        Assert.Equal(ChannelResult.Failed, outcome.Ntfy);
    }

    [Fact]
    public async Task TheEventOverloadReturnsOneOutcomePerMessage()
    {
        var (dispatcher, _, _, _) = Build();

        IReadOnlyList<DispatchOutcome> outcomes = await dispatcher.DispatchAsync([Threat(), new LocalClear(Start)]);

        Assert.Equal(2, outcomes.Count);
    }

    [Fact]
    public void NothingEnabledSaysSo()
    {
        var outcome = new DispatchOutcome(ChannelResult.Disabled, ChannelResult.Disabled, ChannelResult.Disabled);

        Assert.Equal("no channels enabled", outcome.Describe());
    }

    [Theory]
    [InlineData(AlertKind.Unreadable)]
    [InlineData(AlertKind.Overflow)]
    [InlineData(AlertKind.MemberCountWhileUnreadable)]
    public void UnreadableKindsAllRouteToTheUnreadableChannelSet(AlertKind kind)
    {
        var settings = new AlertSettings
        {
            Unreadable = new AlertChannelSet(Sound: false, Popup: true, Ntfy: false),
            Threats = new AlertChannelSet(true, true, true),
        };

        Assert.Equal(settings.Unreadable, settings.ChannelsFor(kind));
    }
}
