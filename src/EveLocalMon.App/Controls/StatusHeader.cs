using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Status;

namespace EveLocalMon.App.Controls;

/// <summary>
/// The large state readout (doc/specs/M4-SPEC.md §5.2): full on the Monitor tab, compact elsewhere.
/// It only displays a <see cref="MonitorStatus"/>; deciding that status is Core's job, so the rule
/// "never call an unreadable Local clear" lives in one tested place.
/// </summary>
public sealed class StatusHeader : Border
{
    private readonly FullLayout _full = new();
    private readonly CompactLayout _compact = new();

    public StatusHeader()
    {
        Background = Palette.Panel.Brush;
        BorderBrush = Palette.Divider.Brush;
        BorderThickness = new Thickness(0, 0, 0, 1);

        _full.Root.IsVisible = true;
        _compact.Root.IsVisible = false;
        Child = new Panel { Children = { _full.Root, _compact.Root } };

        _full.StartStop.Click += (_, _) => StartStopClicked?.Invoke();
        _compact.StartStop.Click += (_, _) => StartStopClicked?.Invoke();
        _full.Mute.Click += (_, _) => MuteClicked?.Invoke();
        _full.Interval.Changed += ms => IntervalChanged?.Invoke(ms);

        SetRunning(false);
        SetMute(null);
    }

    public event Action? StartStopClicked;
    public event Action? MuteClicked;
    public event Action<int>? IntervalChanged;

    /// <summary>True on the Alerts and Calibration tabs: the 44 px strip instead of the 90 px header.</summary>
    public bool Compact
    {
        get => _compact.Root.IsVisible;
        set
        {
            _compact.Root.IsVisible = value;
            _full.Root.IsVisible = !value;
            Height = value ? 44 : 90;
        }
    }

    public int IntervalMs
    {
        get => _full.Interval.Value;
        set => _full.Interval.Value = value;
    }

    public void SetRunning(bool running)
    {
        SetStartStop(_full.StartStop, running);
        SetStartStop(_compact.StartStop, running);
    }

    public void Apply(MonitorStatus status)
    {
        _full.Apply(status);
        _compact.Apply(status);
    }

    /// <summary>Null when not muted; otherwise how long is left.</summary>
    public void SetMute(TimeSpan? remaining)
    {
        bool muted = remaining is not null;
        IBrush brush = muted ? Palette.Warning.Brush : Palette.TextBody.Brush;
        string text = remaining is { } left
            ? $"Muted · {(int)left.TotalMinutes}:{left.Seconds:00} left"
            : "Mute 15 min";

        _full.MuteIcon.Brush = brush;
        _full.MuteText.Text = text;
        _full.MuteText.Foreground = brush;
        ToolTip.SetTip(_full.Mute, muted ? "Click to unmute" : "Silence sound and ntfy for 15 minutes (popups still appear)");
    }

    /// <summary>How long since the loop last captured; <paramref name="stalled"/> turns it yellow.</summary>
    public void SetLastScan(TimeSpan? since, bool stalled)
    {
        _full.LastScan.Text = since is { } span ? $"{span.TotalSeconds:0.0} s" : "—";
        _full.LastScan.Foreground = stalled ? Palette.Warning.Brush : Palette.TextStrong.Brush;
    }

    private static void SetStartStop(Button button, bool running)
    {
        button.Content = running
            ? ButtonContent(IconKind.Stop, "Stop watching")
            : ButtonContent(IconKind.Play, "Start watching");

        static Control ButtonContent(IconKind icon, string text) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { new Icon(icon, Palette.AccentLight.Brush, 10), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
        };
    }

    private static string DescribeThreats(MonitorStatus status)
    {
        if (status.State is MonitorState.Stopped or MonitorState.SetupNeeded or MonitorState.Checking)
        {
            return string.Empty;
        }

        if (status.Breakdown is not { } breakdown)
        {
            return "can't see Local — not “clear”";
        }

        return $"{breakdown.Enemy} hostile · {breakdown.Neutral + breakdown.ExtraNoIcon} neutral · {breakdown.Unknown} unknown";
    }

    private static string DescribeReadableSub(MonitorStatus status) => status.State switch
    {
        MonitorState.Clear => "list fully visible",
        MonitorState.Hostile => "see Rows",
        MonitorState.Unreadable => "cannot confirm clear",
        MonitorState.Checking => "reading…",
        _ => string.Empty,
    };

    private static IBrush ThreatBrush(MonitorStatus status) =>
        status.Threats is > 0 ? Palette.Hostile.Brush : status.Threats is null && status.State == MonitorState.Unreadable ? Palette.Warning.Brush : Palette.TextStrong.Brush;

    private sealed class FullLayout
    {
        public readonly StateDot Dot = new(10);
        public readonly TextBlock Word = Ui.Text("Stopped", 34, Palette.TextLabel.Brush, FontWeight.Medium);
        public readonly TextBlock InLocal = Ui.Mono("—", 26);
        public readonly TextBlock InLocalSub = Ui.Label("");
        public readonly TextBlock Readable = Ui.Mono("—", 26);
        public readonly TextBlock ReadableSub = Ui.Label("");
        public readonly TextBlock Threats = Ui.Mono("—", 26);
        public readonly TextBlock ThreatsSub = Ui.Label("");
        public readonly TextBlock LastScan = Ui.Mono("—", 26);
        public readonly IntervalPicker Interval = new();
        public readonly Button StartStop = new() { MinWidth = 160 };
        public readonly Button Mute;
        public readonly Icon MuteIcon = new(IconKind.Mute, Palette.TextBody.Brush, 13);
        public readonly TextBlock MuteText = new() { VerticalAlignment = VerticalAlignment.Center };

        public FullLayout()
        {
            EveTheme.SetKind(StartStop, ButtonKind.Primary);
            Mute = new Button
            {
                MinWidth = 160,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { MuteIcon, MuteText } },
            };

            var left = new StackPanel
            {
                Margin = new Thickness(18, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Ui.Label("Local status"),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Dot, Word } },
                },
            };

            var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            AddCell(cells, 0, "In Local (header)", InLocal, InLocalSub);
            AddCell(cells, 1, "Readable rows", Readable, ReadableSub);
            AddCell(cells, 2, "Threats", Threats, ThreatsSub);
            AddCell(cells, 3, "Last scan", LastScan, Interval);

            var right = new StackPanel
            {
                Margin = new Thickness(18, 0),
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { StartStop, Mute },
            };

            Root = new Grid { ColumnDefinitions = new ColumnDefinitions("250,*,200") };
            Grid.SetColumn(cells, 1);
            Grid.SetColumn(right, 2);
            Border leftDivider = Ui.VLine(Palette.Divider.Brush);
            Border rightDivider = Ui.VLine(Palette.Divider.Brush);
            rightDivider.HorizontalAlignment = HorizontalAlignment.Left;
            leftDivider.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(rightDivider, 2);
            Root.Children.Add(left);
            Root.Children.Add(leftDivider);
            Root.Children.Add(cells);
            Root.Children.Add(right);
            Root.Children.Add(rightDivider);
        }

        public Grid Root { get; }

        public void Apply(MonitorStatus status)
        {
            Dot.SetState(status.State);
            Word.Text = status.Word;
            Word.Foreground = Palette.TextFor(status.State).Brush;
            InLocal.Text = status.InLocal?.ToString() ?? "—";
            InLocalSub.Text = status.InLocal is null ? (status.State is MonitorState.Stopped or MonitorState.SetupNeeded or MonitorState.Checking ? string.Empty : "header not readable") : "header digits read";
            Readable.Text = status.Readable;
            ReadableSub.Text = DescribeReadableSub(status);
            Threats.Text = status.Threats?.ToString() ?? (status.State is MonitorState.Stopped or MonitorState.SetupNeeded or MonitorState.Checking ? "—" : "?");
            Threats.Foreground = ThreatBrush(status);
            ThreatsSub.Text = DescribeThreats(status);
        }

        private static void AddCell(Grid grid, int column, string label, TextBlock value, Control sub)
        {
            var cell = new StackPanel
            {
                Margin = new Thickness(18, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { Ui.Label(label), value, sub },
            };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);

            if (column > 0)
            {
                Border line = Ui.VLine(Palette.Divider.Brush);
                line.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(line, column);
                grid.Children.Add(line);
            }
        }
    }

    private sealed class CompactLayout
    {
        public readonly StateDot Dot = new(8);
        public readonly TextBlock Word = Ui.Text("Stopped", 18, Palette.TextLabel.Brush, FontWeight.Medium);
        public readonly TextBlock InLocal = Ui.Mono("—", 13);
        public readonly TextBlock Readable = Ui.Mono("—", 13);
        public readonly TextBlock Threats = Ui.Mono("—", 13);
        public readonly Button StartStop = new() { MinWidth = 160 };

        public CompactLayout()
        {
            EveTheme.SetKind(StartStop, ButtonKind.Primary);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 22,
                Margin = new Thickness(18, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Dot, Word } },
                    Pair("In Local", InLocal),
                    Pair("Readable", Readable),
                    Pair("Threats", Threats),
                },
            };

            StartStop.HorizontalAlignment = HorizontalAlignment.Right;
            StartStop.Margin = new Thickness(0, 0, 18, 0);
            Root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(StartStop, 1);
            Root.Children.Add(row);
            Root.Children.Add(StartStop);
        }

        public Grid Root { get; }

        public void Apply(MonitorStatus status)
        {
            Dot.SetState(status.State);
            Word.Text = status.Word;
            Word.Foreground = Palette.TextFor(status.State).Brush;
            InLocal.Text = status.InLocal?.ToString() ?? "—";
            Readable.Text = status.Readable;
            Threats.Text = status.Threats?.ToString() ?? (status.State is MonitorState.Stopped or MonitorState.SetupNeeded or MonitorState.Checking ? "—" : "?");
            Threats.Foreground = ThreatBrush(status);
        }

        private static StackPanel Pair(string label, TextBlock value) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { Ui.Label(label), value },
        };
    }
}
