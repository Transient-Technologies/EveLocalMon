using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EveLocalMon.App.Controls;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Alerts;

namespace EveLocalMon.App.Views;

public enum NtfyHealth
{
    Unknown,
    Delivered,
    Failed,
}

/// <summary>
/// Alert settings (doc/specs/M4-SPEC.md §8). Every change applies and saves immediately; text fields
/// wait for 500 ms of quiet first so typing a topic does not save on every keystroke.
/// </summary>
public sealed class AlertsTab : Grid
{
    private static readonly (string Title, string Description, Swatch Colour)[] Events =
    [
        ("Threat appears", "Hostile, neutral or unknown icon, or the threat count rises", Palette.Hostile),
        ("Local unreadable", "Window covered, minimised, scrolled past, or header lost", Palette.Warning),
        ("All clear", "Threats drop back to zero", Palette.Accent),
    ];

    private static readonly (string Label, int Minutes)[] MutePresets = [("15 min", 15), ("30 min", 30), ("1 hour", 60)];

    private readonly ChannelCheck[,] _checks = new ChannelCheck[3, 3];
    private readonly LevelSlider _volume = new("Volume");
    private readonly TextBlock _volumeText = Ui.Mono("70%", 13, Palette.TextBody.Brush);
    private readonly TextBox _server = new() { PlaceholderText = "https://ntfy.sh" };
    private readonly TextBox _topic = new() { PlaceholderText = "press Generate" };
    private readonly SelectableTextBlock _subscribe = new() { FontFamily = Fonts.Mono, FontSize = 12, Foreground = Palette.TextData.Brush };
    private readonly TextBlock _ntfyStatus = Ui.Label("not tested yet");
    private readonly StateDot _ntfyDot = new(6);
    private readonly List<(Button Button, int Minutes)> _muteButtons = [];
    private readonly TextBlock _muteStatus = Ui.Label("Not muted.", 12);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loading;
    private int? _selectedPreset;

    public AlertsTab()
    {
        ColumnDefinitions = new ColumnDefinitions("1.15*,*");
        Margin = new Thickness(10);

        _server.Classes.Add(EveTheme.Mono);
        _topic.Classes.Add(EveTheme.Mono);
        Avalonia.Automation.AutomationProperties.SetName(_server, "ntfy server");
        Avalonia.Automation.AutomationProperties.SetName(_topic, "ntfy topic");

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Emit();
        };
        _server.TextChanged += (_, _) => TextEdited();
        _topic.TextChanged += (_, _) => TextEdited();

        _volume.Changed += value =>
        {
            _volumeText.Text = $"{value}%";
            QueueChange();
        };
        _volume.Committed += value => VolumePreviewRequested?.Invoke(value);

        var left = Ui.Column(10, BuildMatrix(), BuildSound(), BuildMute());
        var right = Ui.Column(10, BuildNtfy(), BuildPopup());
        left.Margin = new Thickness(0, 0, 5, 0);
        right.Margin = new Thickness(5, 0, 0, 0);
        SetColumn(right, 1);
        Children.Add(left);
        Children.Add(right);

        Value = new AlertSettings();
        SetMute(null);
    }

    public event Action<AlertSettings>? Changed;

    /// <summary>"sound", "popup" or "ntfy".</summary>
    public event Action<string>? TestRequested;

    /// <summary>A mute preset, or null for Unmute.</summary>
    public event Action<TimeSpan?>? MuteRequested;

    /// <summary>The volume slider was released: play the chime once at this volume.</summary>
    public event Action<int>? VolumePreviewRequested;

    public AlertSettings Value
    {
        get => new()
        {
            Threats = Row(0),
            Unreadable = Row(1),
            AllClear = Row(2),
            VolumePercent = _volume.Value,
            NtfyServer = string.IsNullOrWhiteSpace(_server.Text) ? "https://ntfy.sh" : _server.Text!.Trim(),
            NtfyTopic = string.IsNullOrWhiteSpace(_topic.Text) ? null : _topic.Text!.Trim(),
            SoundFilePath = _soundFilePath,
            SoundMinIntervalMs = _template.SoundMinIntervalMs,
            PopupMinIntervalMs = _template.PopupMinIntervalMs,
            NtfyMinIntervalMs = _template.NtfyMinIntervalMs,
        };

        set
        {
            _loading = true;
            _template = value;
            _soundFilePath = value.SoundFilePath;
            Write(0, value.Threats);
            Write(1, value.Unreadable);
            Write(2, value.AllClear);
            _volume.Value = value.VolumePercent;
            _volumeText.Text = $"{value.VolumePercent}%";
            _server.Text = value.NtfyServer;
            _topic.Text = value.NtfyTopic ?? string.Empty;
            UpdateSubscribeLine();
            _loading = false;
            _lastEmitted = Value;
        }
    }

    private AlertSettings? _lastEmitted;
    private AlertSettings _template = new();
    private string? _soundFilePath;

    /// <summary>Null when not muted. Highlights the preset in use and says until when.</summary>
    public void SetMute(DateTimeOffset? until)
    {
        foreach ((Button button, int minutes) in _muteButtons)
        {
            EveTheme.SetKind(button, until is not null && minutes == _selectedPreset ? ButtonKind.Selected : ButtonKind.Normal);
        }

        if (until is { } time)
        {
            _muteStatus.Text = $"Muted until {time.ToLocalTime():HH:mm} — sound and ntfy are silent; popups still appear.";
            _muteStatus.Foreground = Palette.Warning.Brush;
        }
        else
        {
            _selectedPreset = null;
            _muteStatus.Text = "Not muted.";
            _muteStatus.Foreground = Palette.TextLabel.Brush;
        }
    }

    public void SetNtfyStatus(string text, NtfyHealth health)
    {
        _ntfyStatus.Text = text;
        if (health == NtfyHealth.Unknown)
        {
            _ntfyDot.SetState(Core.Status.MonitorState.Stopped);
            return;
        }

        _ntfyDot.SetState(Core.Status.MonitorState.Clear);
        _ntfyDot.Background = health == NtfyHealth.Delivered ? Palette.Ok.Brush : Palette.Hostile.Brush;
        _ntfyDot.BoxShadow = default;
    }

    private Control BuildMatrix()
    {
        const string Columns = "*,84,84,84";
        var body = Ui.Column(0);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Margin = new Thickness(12, 8, 12, 4) };
        header.Children.Add(Ui.Label("Event"));
        string[] channels = ["Sound", "Popup", "ntfy"];
        for (int c = 0; c < 3; c++)
        {
            TextBlock label = Ui.Label(channels[c]);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            SetColumn(label, c + 1);
            header.Children.Add(label);
        }

        body.Children.Add(header);

        for (int r = 0; r < Events.Length; r++)
        {
            (string title, string description, Swatch colour) = Events[r];
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Margin = new Thickness(12, 10) };
            var name = Ui.Column(1,
                Ui.Row(8, Ui.Chip(colour.Color), Ui.Text(title, 14, Palette.TextStrong.Brush)),
                Ui.Label(description));
            row.Children.Add(name);

            for (int c = 0; c < 3; c++)
            {
                var check = new ChannelCheck($"{title}: {channels[c]}") { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                check.Toggled += _ => QueueChange(immediate: true);
                _checks[r, c] = check;
                SetColumn(check, c + 1);
                row.Children.Add(check);
            }

            body.Children.Add(new Border { BorderBrush = Palette.Divider.Brush, BorderThickness = new Thickness(0, 1, 0, 0), Child = row });
        }

        var tests = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Margin = new Thickness(12, 8) };
        tests.Children.Add(Ui.Label("Send a test through one channel", 12));
        string[] keys = ["sound", "popup", "ntfy"];
        for (int c = 0; c < 3; c++)
        {
            string key = keys[c];
            Button test = Ui.Button("Test", dense: true, onClick: () => TestRequested?.Invoke(key));
            test.HorizontalAlignment = HorizontalAlignment.Center;
            Avalonia.Automation.AutomationProperties.SetName(test, $"Test {channels[c]}");
            SetColumn(test, c + 1);
            tests.Children.Add(test);
        }

        body.Children.Add(new Border
        {
            Background = Palette.TabStrip.Brush,
            BorderBrush = Palette.Divider.Brush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = tests,
        });

        return new SectionPanel("When to alert", body, headerRight: Ui.Label("rate-limited per channel"));
    }

    private Control BuildSound()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,48"), Margin = new Thickness(12, 12, 12, 8) };
        Label(row, Ui.Label("Volume", 12), 0);
        Label(row, _volume, 1);
        Label(row, _volumeText, 2);
        _volumeText.HorizontalAlignment = HorizontalAlignment.Right;
        _volume.Margin = new Thickness(0, 0, 12, 0);

        var note = Ui.Label("Built-in chime, synthesised at runtime · plays even when Windows is in Focus Assist");
        note.Margin = new Thickness(12, 0, 12, 12);
        note.TextWrapping = TextWrapping.Wrap;

        return new SectionPanel("Sound", Ui.Column(0, row, note));

        static void Label(Grid grid, Control control, int column)
        {
            SetColumn(control, column);
            grid.Children.Add(control);
        }
    }

    private Control BuildMute()
    {
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        foreach ((string label, int minutes) in MutePresets)
        {
            Button button = Ui.Button(label, onClick: () =>
            {
                _selectedPreset = minutes;
                MuteRequested?.Invoke(TimeSpan.FromMinutes(minutes));
            });
            button.BorderThickness = new Thickness(1, 1, 0, 1);
            _muteButtons.Add((button, minutes));
            segments.Children.Add(button);
        }

        segments.Children.Add(Ui.Button("Unmute", onClick: () => MuteRequested?.Invoke(null)));

        _muteStatus.TextWrapping = TextWrapping.Wrap;
        var body = Ui.Column(10, segments, _muteStatus);
        body.Margin = new Thickness(12);
        return new SectionPanel("Mute", body);
    }

    private Control BuildNtfy()
    {
        Button generate = Ui.Button("Generate", onClick: () =>
        {
            _topic.Text = AlertDispatcher.GenerateTopic();
            QueueChange(immediate: true);
        });

        var topicRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        SetColumn(generate, 1);
        generate.Margin = new Thickness(6, 0, 0, 0);
        topicRow.Children.Add(_topic);
        topicRow.Children.Add(generate);

        var notice = new Border
        {
            Background = Palette.Warning.WithOpacity(0.08),
            BorderBrush = Palette.Warning.WithOpacity(0.35),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            Child = Ui.Row(8,
                new Icon(IconKind.Warning, Palette.Warning.Brush, 14, 2.2) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) },
                new TextBlock
                {
                    Text = "Anyone who knows the topic name can read your alerts. Keep the generated random name and don't post it in chat.",
                    FontSize = 12,
                    Foreground = Palette.Warning.Brush,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 340,
                }),
        };

        var body = Ui.Column(10,
            Ui.Column(4, Ui.Label("Server"), _server),
            Ui.Column(4, Ui.Label("Topic"), topicRow),
            notice,
            Ui.Column(4, Ui.Label("Subscribe on your phone"), _subscribe));
        body.Margin = new Thickness(12);

        _subscribe.TextWrapping = TextWrapping.Wrap;
        var status = Ui.Row(6, _ntfyDot, _ntfyStatus);
        return new SectionPanel("ntfy push", body, headerRight: status);
    }

    private Control BuildPopup()
    {
        var description = Ui.Label("Appears on the monitor showing the watched region, stays on top of EVE and never takes focus. Click to dismiss.", 12);
        description.TextWrapping = TextWrapping.Wrap;

        var sample = new AlertPopupView(
            new AlertMessage(AlertKind.ThreatIncreased, "Hostile in Local", "1 red, 1 neutral — 2 threats visible", AlertPriority.Urgent, DateTimeOffset.Now),
            "20 in Local · all rows readable");
        sample.SetRemaining(0.64);
        sample.HorizontalAlignment = HorizontalAlignment.Left;
        sample.IsHitTestVisible = false;

        var body = Ui.Column(12, description, sample);
        body.Margin = new Thickness(12);
        return new SectionPanel("Popup", body);
    }

    private AlertChannelSet Row(int row) => new(_checks[row, 0].IsChecked, _checks[row, 1].IsChecked, _checks[row, 2].IsChecked);

    private void Write(int row, AlertChannelSet set)
    {
        _checks[row, 0].IsChecked = set.Sound;
        _checks[row, 1].IsChecked = set.Popup;
        _checks[row, 2].IsChecked = set.Ntfy;
    }

    private void TextEdited()
    {
        UpdateSubscribeLine();
        QueueChange();
    }

    private void QueueChange(bool immediate = false)
    {
        if (_loading)
        {
            return;
        }

        _debounce.Stop();
        if (immediate)
        {
            Emit();
        }
        else
        {
            _debounce.Start();
        }
    }

    /// <summary>TextBox raises TextChanged after it has already been set from code, so a programmatic
    /// load can arrive here late; only a value that differs from the last one is a real change.</summary>
    private void Emit()
    {
        AlertSettings current = Value;
        if (current == _lastEmitted)
        {
            return;
        }

        _lastEmitted = current;
        Changed?.Invoke(current);
    }

    private void UpdateSubscribeLine()
    {
        string server = string.IsNullOrWhiteSpace(_server.Text) ? "https://ntfy.sh" : _server.Text!.Trim();
        string host = Uri.TryCreate(server, UriKind.Absolute, out Uri? uri) ? uri.Authority : server;
        _subscribe.Text = string.IsNullOrWhiteSpace(_topic.Text) ? "— set a topic first —" : $"{host}/{_topic.Text!.Trim()}";
    }
}
