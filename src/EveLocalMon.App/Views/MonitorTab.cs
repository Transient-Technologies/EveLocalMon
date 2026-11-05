using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EveLocalMon.App.Controls;
using EveLocalMon.App.Rendering;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.App.Views;

/// <summary>
/// Capture | Rows | Session (doc/specs/M4-SPEC.md §7). The capture and the rows table are raw: they
/// show exactly what the reader saw on the latest frame. The header above is the steadier view.
/// </summary>
public sealed class MonitorTab : Grid
{
    private const string RowColumns = "40,56,60,1.4*,1.4*,64,64";

    private readonly Image _preview = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _captureScroll;
    private readonly Control _captureEmpty;
    private readonly Control _rowsEmpty;
    private readonly TextBlock _rowsEmptyTitle = Ui.Text("", 15, Palette.Warning.Brush);
    private readonly TextBlock _rowsEmptyBody = Ui.Label("", 12);
    private readonly Control _rowsGlyph;
    private readonly ItemsControl _rows;
    private readonly ItemsControl _log;
    private readonly TextBlock _logEmpty = Ui.Label("Nothing yet this session.", 12);
    private readonly SectionPanel _rowsPanel;
    private readonly StackPanel _channelChips = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private WriteableBitmap? _bitmap;
    private SessionLog? _sessionLog;

    public MonitorTab()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*,300");
        Margin = new Thickness(10);

        _captureEmpty = Ui.Label("Nothing captured yet.\nSelect a region, then start watching.", 11);
        ((TextBlock)_captureEmpty).TextWrapping = TextWrapping.Wrap;
        _captureEmpty.Margin = new Thickness(12);
        RenderOptions.SetBitmapInterpolationMode(_preview, BitmapInterpolationMode.None);

        _captureScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Panel
            {
                Margin = new Thickness(14, 10, 12, 10),
                Children =
                {
                    _preview,
                    new Rectangle
                    {
                        Stroke = Palette.ControlBorder.Brush,
                        StrokeThickness = 1,
                        StrokeDashArray = [3, 3],
                        Margin = new Thickness(-2),
                        IsHitTestVisible = false,
                    },
                },
            },
        };

        _rows = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<RowVm?>((row, _) => BuildRow(row), supportsRecycling: false),
        };

        _rowsGlyph = new Icon(IconKind.Warning, Palette.Warning.Brush, 28, 1.5);
        _rowsEmpty = new StackPanel
        {
            Margin = new Thickness(24, 40),
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _rowsGlyph, _rowsEmptyTitle, _rowsEmptyBody },
        };
        _rowsEmptyBody.TextWrapping = TextWrapping.Wrap;
        _rowsEmptyBody.MaxWidth = 360;
        _rowsEmptyBody.TextAlignment = TextAlignment.Center;
        _rowsEmptyTitle.TextAlignment = TextAlignment.Center;
        _rowsGlyph.HorizontalAlignment = HorizontalAlignment.Center;

        _log = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<SessionEntry?>((entry, _) => BuildLogEntry(entry), supportsRecycling: false),
        };

        // 196 px fits a typical ~150 px list; a wider region (this user's is 180) makes the column grow
        // rather than clip, up to a limit past which the capture scrolls.
        SectionPanel capture = new("Capture", BuildCaptureBody(), headerRight: Ui.Label("1:1 px")) { MinWidth = 196, MaxWidth = 340 };

        _rowsPanel = new SectionPanel("Rows", BuildRowsBody(), count: "0", headerRight: Ui.Label("sorted by slot · threats highlighted"));

        Button clear = Ui.Button("Clear", dense: true, onClick: () => _sessionLog?.Clear());
        SectionPanel session = new("Session", BuildSessionBody(), headerRight: clear);

        Grid.SetColumn(_rowsPanel, 1);
        Grid.SetColumn(session, 2);
        _rowsPanel.Margin = new Thickness(10, 0);
        Children.Add(capture);
        Children.Add(_rowsPanel);
        Children.Add(session);

        ShowRowsEmpty("Nothing to show yet", "Rows appear here once watching starts.", warning: false);
    }

    /// <summary>Draws the latest frame, its row outlines, and the rows table.</summary>
    public void ShowFrame(Frame frame, Snapshot? snapshot, Calibration? calibration, TemplateSet templates, double renderScaling)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap = PreviewRenderer.CreateBitmap(frame.Width, frame.Height);
            _preview.Source = _bitmap;
        }

        PreviewRenderer.RenderRows(_bitmap, frame, snapshot, calibration, templates);

        // Physical pixels 1:1 whatever the monitor scaling, so the preview matches what the reader
        // sees rather than a resampled approximation of it.
        _preview.Width = frame.Width / renderScaling;
        _preview.Height = frame.Height / renderScaling;
        _preview.InvalidateVisual();
        _captureEmpty.IsVisible = false;

        UpdateRows(snapshot, templates);
    }

    /// <summary>Binds the session list to <paramref name="log"/>.</summary>
    public void SetLog(SessionLog log)
    {
        _sessionLog = log;
        log.Changed += RefreshLog;
        RefreshLog();
    }

    /// <summary>The "Channels for threats" chips. When muted, sound and ntfy are struck through.</summary>
    public void SetChannels(AlertChannelSet threats, bool muted)
    {
        _channelChips.Children.Clear();
        _channelChips.Children.Add(Chip("Sound", threats.Sound, struck: muted && threats.Sound));
        _channelChips.Children.Add(Chip("Popup", threats.Popup, struck: false));
        _channelChips.Children.Add(Chip("ntfy", threats.Ntfy, struck: muted && threats.Ntfy));
    }

    private Control BuildCaptureBody()
    {
        var legend = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(10, 8),
        };
        AddLegend(legend, 0, 0, "Friendly", Palette.FromBgr(IconTemplate.DefaultColor(TemplateCategory.Friendly)));
        AddLegend(legend, 1, 0, "Hostile", Palette.Hostile.Color);
        AddLegend(legend, 0, 1, "Neutral", Palette.FromBgr(IconTemplate.DefaultColor(TemplateCategory.Neutral)));
        AddLegend(legend, 1, 1, "Unknown", Palette.Warning.Color);

        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var bottom = new Border { BorderBrush = Palette.Divider.Brush, BorderThickness = new Thickness(0, 1, 0, 0), Child = legend };
        Grid.SetRow(bottom, 1);
        body.Children.Add(new Panel { Children = { _captureScroll, _captureEmpty } });
        body.Children.Add(bottom);
        return body;
    }

    private static void AddLegend(Grid grid, int column, int row, string text, Color colour)
    {
        var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2) };
        entry.Children.Add(Ui.Chip(colour, outlineOnly: true));
        entry.Children.Add(Ui.Label(text));
        Grid.SetColumn(entry, column);
        Grid.SetRow(entry, row);
        grid.Children.Add(entry);
    }

    private Control BuildRowsBody()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(RowColumns), Height = 24, Margin = new Thickness(10, 0) };
        string[] titles = ["Row", "Slot y", "Kind", "Category", "Template", "Best", "2nd"];
        for (int i = 0; i < titles.Length; i++)
        {
            TextBlock title = Ui.Label(titles[i]);
            if (i >= 5)
            {
                title.HorizontalAlignment = HorizontalAlignment.Right;
            }

            Grid.SetColumn(title, i);
            header.Children.Add(title);
        }

        var list = new Panel
        {
            Children =
            {
                new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
                _rowsEmpty,
            },
        };

        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        Border line = Ui.HLine(Palette.Divider.Brush);
        Grid.SetRow(line, 1);
        Grid.SetRow(list, 2);
        body.Children.Add(header);
        body.Children.Add(line);
        body.Children.Add(list);
        return body;
    }

    private Control BuildSessionBody()
    {
        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        _logEmpty.Margin = new Thickness(0, 10);
        var scroll = new ScrollViewer { Content = new StackPanel { Children = { _logEmpty, _log } }, Margin = new Thickness(10, 4) };

        var footer = new Border
        {
            BorderBrush = Palette.Divider.Brush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 8),
            Child = Ui.Column(6, Ui.Label("Channels for threats"), _channelChips),
        };
        Grid.SetRow(footer, 1);
        body.Children.Add(scroll);
        body.Children.Add(footer);
        return body;
    }

    private void UpdateRows(Snapshot? snapshot, TemplateSet templates)
    {
        if (snapshot is null)
        {
            _rows.ItemsSource = Array.Empty<RowVm>();
            _rowsPanel.SetCount("0");
            ShowRowsEmpty("Calibration needed", "Detect or enter a calibration on the Calibration tab to read rows.", warning: true);
            return;
        }

        if (snapshot.Readability == Readability.NotVisible)
        {
            _rows.ItemsSource = Array.Empty<RowVm>();
            _rowsPanel.SetCount("0");
            ShowRowsEmpty(
                "No member list found in the region",
                "This is not “all clear”. Local looks covered, minimised or moved — alerts stay armed.",
                warning: true);
            return;
        }

        var colours = templates.Icons.ToDictionary(t => t.Name, t => Palette.FromBgr(t.DisplayColor));
        bool extraNoIcon = snapshot.Threats.ExtraNoIcon > 0;

        List<RowVm> rows = snapshot.Rows
            .Where(row => row.Category != RowCategory.Empty || row.IsPartial)
            .Select((row, index) => RowVm.From(index + 1, row, colours, extraNoIcon))
            .ToList();

        _rows.ItemsSource = rows;
        _rowsPanel.SetCount(rows.Count.ToString());
        _rowsEmpty.IsVisible = false;
    }

    private void ShowRowsEmpty(string title, string body, bool warning)
    {
        _rowsEmpty.IsVisible = true;
        _rowsEmptyTitle.Text = title;
        _rowsEmptyTitle.Foreground = warning ? Palette.Warning.Brush : Palette.TextLabel.Brush;
        _rowsEmptyBody.Text = body;
        _rowsGlyph.IsVisible = warning;
    }

    private void RefreshLog()
    {
        IReadOnlyList<SessionEntry> entries = _sessionLog?.Entries ?? [];
        _log.ItemsSource = entries;
        _logEmpty.IsVisible = entries.Count == 0;
    }

    private static Control Chip(string name, bool enabled, bool struck)
    {
        var text = Ui.Text(name, 11, enabled && !struck ? Palette.AccentLight.Brush : Palette.TextDisabled.Brush);
        if (struck)
        {
            text.TextDecorations = TextDecorations.Strikethrough;
        }

        return new Border
        {
            BorderBrush = enabled && !struck ? Palette.AccentBorder.Brush : Palette.Hairline.Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2),
            Child = text,
        };
    }

    // Avalonia 12 may ask an item template to build for null content while a list is being replaced, so
    // both item builders accept null and return an empty control.
    private static Control BuildRow(RowVm? row)
    {
        if (row is null)
        {
            return new Panel();
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(RowColumns), Height = 21 };

        void Put(Control control, int column)
        {
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        var padded = new Border
        {
            Padding = new Thickness(10, 0),
            Child = grid,
            Background = row.Tint,
            BorderBrush = Palette.Panel.Brush,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        Put(Ui.Mono(row.Index.ToString(), 12, Palette.TextLabel.Brush), 0);
        Put(Ui.Mono(row.SlotY.ToString(), 12, Palette.TextData.Brush), 1);
        Put(Ui.Text(row.Kind, 12, Palette.TextLabel.Brush), 2);
        Put(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { Ui.Chip(row.Chip), Ui.Text(row.Category, 12, row.Text) },
        }, 3);
        Put(Ui.Mono(row.Template, 12, Palette.TextData.Brush), 4);
        Put(Align(Ui.Mono(row.Best, 12, Palette.TextBody.Brush)), 5);
        Put(Align(Ui.Mono(row.Second, 12, Palette.TextLabel.Brush)), 6);
        return padded;

        static Control Align(TextBlock text)
        {
            text.HorizontalAlignment = HorizontalAlignment.Right;
            return text;
        }
    }

    private static Control BuildLogEntry(SessionEntry? entry)
    {
        if (entry is null)
        {
            return new Panel();
        }

        IBrush title = entry.Kind switch
        {
            SessionEntryKind.Threat or SessionEntryKind.Error => Palette.Hostile.Brush,
            SessionEntryKind.Unreadable => Palette.Warning.Brush,
            SessionEntryKind.Clear => Palette.Accent.Brush,
            _ => Palette.TextBody.Brush,
        };

        var text = new StackPanel { Spacing = 0, Children = { Ui.Text(entry.Title, 12, title) } };
        if (!string.IsNullOrEmpty(entry.Detail))
        {
            TextBlock detail = Ui.Label(entry.Detail);
            detail.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(detail);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*"), Margin = new Thickness(0, 5) };
        TextBlock time = Ui.Mono($"[{entry.At.ToLocalTime():HH:mm:ss}]", 11, Palette.TextLabel.Brush);
        time.VerticalAlignment = VerticalAlignment.Top;
        time.Margin = new Thickness(0, 1, 0, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(time);
        grid.Children.Add(text);

        return new Border { BorderBrush = Palette.Divider.Brush, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    /// <summary>What one row of the table shows. The chip is the icon's EVE colour; tint and text are
    /// what the app thinks of the row (doc/specs/M4-SPEC.md §2.1).</summary>
    private sealed record RowVm(
        int Index,
        int SlotY,
        string Kind,
        string Category,
        Color Chip,
        IBrush? Tint,
        IBrush Text,
        string Template,
        string Best,
        string Second)
    {
        public static RowVm From(int index, RowResult row, Dictionary<string, Color> colours, bool extraNoIcon)
        {
            (string category, Color chip, IBrush? tint, IBrush text) = row.Category switch
            {
                RowCategory.Enemy => ("Hostile", ChipFor(row, colours, TemplateCategory.Enemy), Palette.Hostile.WithOpacity(0.13), Palette.Hostile.Brush),
                RowCategory.Friendly => ("Friendly", ChipFor(row, colours, TemplateCategory.Friendly), null, Palette.TextBody.Brush),
                RowCategory.Neutral => ("Neutral", ChipFor(row, colours, TemplateCategory.Neutral), Palette.Warning.WithOpacity(0.10), Palette.Warning.Brush),
                RowCategory.Unknown => ("Unknown", Palette.Warning.Color, Palette.Warning.WithOpacity(0.10), Palette.Warning.Brush),
                RowCategory.NoIcon => (
                    "No icon",
                    Palette.NoIcon.Color,
                    extraNoIcon ? Palette.Warning.WithOpacity(0.10) : null,
                    extraNoIcon ? Palette.Warning.Brush : Palette.TextBody.Brush),
                _ => ("Empty", Palette.TextDisabled.Color, null, Palette.TextLabel.Brush),
            };

            bool hasScore = row.Category is RowCategory.Friendly or RowCategory.Enemy or RowCategory.Neutral or RowCategory.Unknown;
            return new RowVm(
                index,
                row.SlotTop,
                row.IsPartial ? "partial" : "full",
                category,
                chip,
                tint,
                text,
                row.TemplateName ?? "—",
                hasScore ? row.BestScore.ToString("F1") : "—",
                hasScore ? row.SecondBestScore.ToString("F1") : "—");
        }

        private static Color ChipFor(RowResult row, Dictionary<string, Color> colours, TemplateCategory fallback) =>
            row.TemplateName is { } name && colours.TryGetValue(name, out Color colour)
                ? colour
                : Palette.FromBgr(IconTemplate.DefaultColor(fallback));
    }
}
