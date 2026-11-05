using EveLocalMon.Core.Audio;

namespace EveLocalMon.Core.Alerts;

/// <summary>
/// Fans <see cref="AlertEvent"/>s out to the enabled channels. Rate limits per channel (not per
/// alert) so a burst collapses to one chime while the popup still updates, isolates channel
/// failures from each other, and never blocks the capture loop.
/// </summary>
public sealed class AlertDispatcher(
    ISoundPlayer? soundPlayer,
    IAlertPresenter? presenter,
    INtfyClient? ntfyClient,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _lastSound = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPopup = DateTimeOffset.MinValue;
    private DateTimeOffset _lastNtfy = DateTimeOffset.MinValue;
    private byte[]? _cachedWav;
    private (string? Path, int Volume) _cachedWavKey = (null, -1);

    public AlertSettings Settings { get; set; } = new();

    /// <summary>Kept in memory only: a restart should never leave you silently muted.</summary>
    public DateTimeOffset? MutedUntil { get; private set; }

    public bool IsMuted => MutedUntil is { } until && _time.GetUtcNow() < until;

    /// <summary>Reports a channel that failed, for the status line. Never throws out of dispatch.</summary>
    public event Action<string>? ChannelFailed;

    public void MuteFor(TimeSpan duration) => MutedUntil = _time.GetUtcNow() + duration;

    public void Unmute() => MutedUntil = null;

    public async Task<IReadOnlyList<DispatchOutcome>> DispatchAsync(IEnumerable<AlertEvent> events, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<DispatchOutcome>();
        foreach (AlertEvent alertEvent in events)
        {
            AlertMessage message = AlertMessageFactory.Create(alertEvent);
            outcomes.Add(await DispatchAsync(message, cancellationToken).ConfigureAwait(false));
        }

        return outcomes;
    }

    public async Task<DispatchOutcome> DispatchAsync(AlertMessage message, CancellationToken cancellationToken = default)
    {
        AlertChannelSet channels = Settings.ChannelsFor(message.Kind);
        DateTimeOffset now = _time.GetUtcNow();

        // Muting silences sound and push but never the popup: the app must not quietly imply that
        // Local is clear while it is muted.
        bool muted = IsMuted;

        ChannelResult popup = ChannelResult.Disabled;
        if (channels.Popup && presenter is not null)
        {
            popup = !Allow(ref _lastPopup, now, Settings.PopupMinIntervalMs)
                ? ChannelResult.RateLimited
                : Run(() => presenter.Present(message), "popup");
        }

        ChannelResult sound = ChannelResult.Disabled;
        if (channels.Sound && soundPlayer is not null)
        {
            sound = muted ? ChannelResult.Muted
                : !Allow(ref _lastSound, now, Settings.SoundMinIntervalMs) ? ChannelResult.RateLimited
                : Run(() => soundPlayer.Play(GetWav()), "sound");
        }

        ChannelResult ntfy = ChannelResult.Disabled;
        if (channels.Ntfy && ntfyClient is not null)
        {
            ntfy = muted ? ChannelResult.Muted
                : !Allow(ref _lastNtfy, now, Settings.NtfyMinIntervalMs) ? ChannelResult.RateLimited
                : await SendNtfyAsync(ntfyClient, message, cancellationToken).ConfigureAwait(false);
        }

        return new DispatchOutcome(sound, popup, ntfy);
    }

    private async Task<ChannelResult> SendNtfyAsync(INtfyClient ntfyClient, AlertMessage message, CancellationToken cancellationToken)
    {
        string? topic = Settings.NtfyTopic;
        if (string.IsNullOrWhiteSpace(topic))
        {
            ChannelFailed?.Invoke("ntfy: no topic configured.");
            return ChannelResult.Failed;
        }

        try
        {
            bool sent = await ntfyClient.SendAsync(message, new NtfyOptions(Settings.NtfyServer, topic), cancellationToken)
                .ConfigureAwait(false);
            if (!sent)
            {
                ChannelFailed?.Invoke("ntfy: send failed.");
            }

            return sent ? ChannelResult.Sent : ChannelResult.Failed;
        }
        catch (Exception ex)
        {
            ChannelFailed?.Invoke($"ntfy: {ex.Message}");
            return ChannelResult.Failed;
        }
    }

    /// <summary>Generates a topic that can't be guessed — on ntfy.sh, knowing the topic is the
    /// only thing standing between a stranger and your intel.</summary>
    public static string GenerateTopic()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyz23456789";
        Span<char> chars = stackalloc char[20];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        }

        return "evelocalmon-" + new string(chars);
    }

    private bool Allow(ref DateTimeOffset last, DateTimeOffset now, int minIntervalMs)
    {
        if (now - last < TimeSpan.FromMilliseconds(minIntervalMs))
        {
            return false;
        }

        last = now;
        return true;
    }

    private ChannelResult Run(Action action, string channel)
    {
        try
        {
            action();
            return ChannelResult.Sent;
        }
        catch (Exception ex)
        {
            ChannelFailed?.Invoke($"{channel}: {ex.Message}");
            return ChannelResult.Failed;
        }
    }

    private byte[] GetWav()
    {
        var key = (Settings.SoundFilePath, Settings.VolumePercent);
        if (_cachedWav is not null && _cachedWavKey == key)
        {
            return _cachedWav;
        }

        byte[] wav = Settings.SoundFilePath is { } path && File.Exists(path)
            ? File.ReadAllBytes(path)
            : AlertTone.BuildWav(Settings.VolumePercent);

        _cachedWav = wav;
        _cachedWavKey = key;
        return wav;
    }
}
