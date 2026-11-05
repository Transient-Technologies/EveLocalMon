using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EveLocalMon.App.Controls;
using EveLocalMon.App.Rendering;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using PixelRect = EveLocalMon.Core.Imaging.PixelRect;

namespace EveLocalMon.App.Views;

/// <summary>
/// Auto-detect result plus manual override (doc/specs/M4-SPEC.md §10). The fields edit a draft; the
/// guides re-render from the latest frame within 150 ms of any change, and nothing reaches the reader
/// until <b>Apply</b>. "Auto-detect with manual override" is only a real override if a wrong guess is a
/// five-second fix, so every number the detector chose is editable here.
/// </summary>
public sealed class CalibrationTab : Grid
{
    private enum Field
    {
        IconX, IconSize, NameX, NameRight, RowPitch, ListTop, ListHeight, HeaderX, HeaderY, HeaderWidth, HeaderHeight,
    }

    private static readonly int[] Zooms = [1, 2, 4];

    private readonly Dictionary<Field, Stepper> _steppers = [];
    private readonly Image _guides = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _slotsHint = Ui.Label("");
    private readonly TextBlock _dirtyText = Ui.Label("No unapplied changes", 12);
    private readonly Button _apply;
    private readonly Button _revert;
    private readonly List<(int Zoom, Button Button)> _zoomButtons = [];
    private readonly DispatcherTimer _renderDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private readonly Border _bannerIcon = new() { Width = 34, Height = 34, BorderThickness = new Thickness(1) };
    private readonly TextBlock _bannerTitle = Ui.Text("Auto-detect: not run yet", 15, Palette.TextStrong.Brush);
    private readonly TextBlock _bannerSub = Ui.Label("Select a region to detect the list layout.");
    private readonly StackPanel _bannerNotes = new() { Spacing = 3 };
    private readonly Control _bannerCheck = new Icon(IconKind.Check, Palette.AccentLight.Brush, 16, 1.5);
    private readonly Control _bannerWarn = new Icon(IconKind.Warning, Palette.Warning.Brush, 16, 2);

    private WriteableBitmap? _bitmap;
    private Frame? _frame;
    private Calibration? _baseline;
    private int _zoom = 2;
    private bool _loading;
    private double _renderScaling = 1;

    public CalibrationTab()
    {
        ColumnDefinitions = new ColumnDefinitions("380,*");
        Margin = new Thickness(10);
        RenderOptions.SetBitmapInterpolationMode(_guides, BitmapInterpolationMode.None);

        _apply = Ui.Button("Apply", ButtonKind.Primary, onClick: () =>
        {
            if (ReadDraft() is { } draft)
            {
                Applied?.Invoke(draft);
            }
        });
        _revert = Ui.Button("Revert to detected", onClick: RevertToDetected);

        _renderDelay.Tick += (_, _) =>
        {
            _renderDelay.Stop();
            RenderGuides();
        };

        SectionPanel guides = BuildGuides();
        Grid right = BuildRight();
        right.Margin = new Thickness(10, 0, 0, 0);
        SetColumn(right, 1);
        Children.Add(guides);
        Children.Add(right);

        SetBanner(null, null, string.Empty);
        UpdateDirty();
    }

    /// <summary>The draft was applied; the owner saves it and restarts the loop if it is running.</summary>
    public event Action<Calibration>? Applied;

    public event Action? RedetectRequested;

    /// <summary>Zoom changed by the user (1, 2 or 4).</summary>
    public event Action<int>? ZoomChanged;

    /// <summary>True while the draft differs from the applied calibration (drives the "Calibration •" tab label).</summary>
    public event Action<bool>? DirtyChanged;

    public bool IsDirty { get; private set; }

    public int Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Zooms.Contains(value) ? value : 2;
            foreach ((int zoom, Button button) in _zoomButtons)
            {
                EveTheme.SetKind(button, zoom == _zoom ? ButtonKind.Selected : ButtonKind.Normal);
            }

            ApplyZoomSize();
        }
    }

    /// <summary>Loads <paramref name="calibration"/> as both the applied values and the draft.</summary>
    public void SetCalibration(Calibration? calibration)
    {
        _baseline = calibration;
        _loading = true;
        if (calibration is not null)
        {
            WriteFields(calibration);
        }

        _loading = false;
        UpdateDirty();
        RenderGuides();
    }

    /// <summary>The latest captured frame the guides are drawn over.</summary>
    public void SetFrame(Frame? frame, double renderScaling)
    {
        _frame = frame;
        _renderScaling = renderScaling;
        RenderGuides();
    }

    public void SetDetection(DetectionResult? result, DateTimeOffset? at, string summary) => SetBanner(result, at, summary);

    private SectionPanel BuildGuides()
    {
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (int zoom in Zooms)
        {
            Button button = Ui.Button($"{zoom}×", dense: true, onClick: () =>
            {
                Zoom = zoom;
                ZoomChanged?.Invoke(zoom);
            });
            button.MinWidth = 32;
            button.BorderThickness = zoom == Zooms[^1] ? new Thickness(1) : new Thickness(1, 1, 0, 1);
            Avalonia.Automation.AutomationProperties.SetName(button, $"Zoom {zoom} times");
            _zoomButtons.Add((zoom, button));
            zoomRow.Children.Add(button);
        }

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Border { Margin = new Thickness(30, 14, 14, 14), Child = _guides },
        };

        var legend = new WrapPanel { Margin = new Thickness(10, 8) };
        void Entry(string text, Color colour, bool dashed = false)
        {
            legend.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 2, 14, 2),
                Children = { LineSample(colour, dashed), Ui.Label(text) },
            });
        }

        Entry("Icon column", Palette.Accent.Color);
        Entry("Name x", Palette.TextStrong.Color);
        Entry("List", Palette.TextData.Color, dashed: true);
        Entry("Header count", Palette.Warning.Color);

        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var bottom = new Border { BorderBrush = Palette.Divider.Brush, BorderThickness = new Thickness(0, 1, 0, 0), Child = legend };
        SetRow(bottom, 1);
        body.Children.Add(scroll);
        body.Children.Add(bottom);

        Zoom = 2;
        return new SectionPanel("Guides", body, headerRight: zoomRow);
    }

    private Grid BuildRight()
    {
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };

        var banner = new Border
        {
            Background = Palette.Panel.Brush,
            BorderBrush = Palette.Hairline.Brush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10),
            Child = Ui.Column(8,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    Margin = new Thickness(14, 12, 14, 0),
                    Children =
                    {
                        _bannerIcon,
                        Placed(Ui.Column(1, _bannerTitle, _bannerSub), 1, 12),
                        Placed(Ui.Button(IconKind.Refresh, "Re-detect", onClick: () => RedetectRequested?.Invoke()), 2, 0),
                    },
                },
                WithMargin(_bannerNotes, new Thickness(60, 0, 14, 12))),
        };

        var groups = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        AddGroup(groups, 0, 0, "Icon column", Palette.Accent, false,
            (Field.IconX, "Icon x", "left edge of icons", 0, 10000),
            (Field.IconSize, "Icon size", "square, px", 1, 100));
        AddGroup(groups, 1, 0, "Name", Palette.TextStrong, false,
            (Field.NameX, "Name x", "start of pilot name", 0, 10000),
            (Field.NameRight, "Name right", "right edge of list", 0, 10000));
        AddGroup(groups, 0, 1, "Rows", Palette.TextData, true,
            (Field.RowPitch, "Row pitch", "px between rows", 1, 200),
            (Field.ListTop, "List top", "first row slot", 0, 10000),
            (Field.ListHeight, "List height", "", 0, 10000));
        AddGroup(groups, 1, 1, "Header count", Palette.Warning, false,
            (Field.HeaderX, "Header x", "", 0, 10000),
            (Field.HeaderY, "Header y", "", 0, 10000),
            (Field.HeaderWidth, "Header w", "", 0, 10000),
            (Field.HeaderHeight, "Header h", "", 0, 10000));

        foreach (Stepper stepper in _steppers.Values)
        {
            stepper.ValueChanged += _ => OnFieldChanged();
        }

        var footer = new Border
        {
            Background = Palette.TabStrip.Brush,
            BorderBrush = Palette.Divider.Brush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 10),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    _dirtyText,
                    Placed(Ui.Row(8, _revert, _apply), 1, 0),
                },
            },
        };

        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        SetRow(footer, 1);
        body.Children.Add(new ScrollViewer { Content = groups });
        body.Children.Add(footer);

        SectionPanel override_ = new("Manual override", body, headerRight: Ui.Label("region pixels · preview updates as you type"));
        SetRow(banner, 0);
        SetRow(override_, 1);
        right.Children.Add(banner);
        right.Children.Add(override_);
        return right;
    }

    private void AddGroup(
        Grid grid, int column, int row, string title, Swatch colour, bool dashed,
        params (Field Field, string Label, string Hint, int Min, int Max)[] fields)
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(14, 12) };
        panel.Children.Add(Ui.Row(8, LineSample(colour.Color, dashed), Ui.Text(title, 13, Palette.TextStrong.Brush)));

        foreach ((Field field, string label, string hint, int min, int max) in fields)
        {
            var stepper = new Stepper(label, min, max);
            _steppers[field] = stepper;

            TextBlock hintText = field == Field.ListHeight ? _slotsHint : Ui.Label(hint);
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("78,Auto,*") };
            TextBlock name = Ui.Label(label, 12);
            name.Margin = new Thickness(0, 0, 8, 0);
            SetColumn(stepper, 1);
            SetColumn(hintText, 2);
            hintText.Margin = new Thickness(10, 0, 0, 0);
            line.Children.Add(name);
            line.Children.Add(stepper);
            line.Children.Add(hintText);
            panel.Children.Add(line);
        }

        var cell = new Border
        {
            BorderBrush = Palette.Divider.Brush,
            BorderThickness = new Thickness(0, 0, column == 0 ? 1 : 0, 1),
            Child = panel,
        };
        SetColumn(cell, column);
        SetRow(cell, row);
        grid.Children.Add(cell);
    }

    private static Control LineSample(Color colour, bool dashed) => new Avalonia.Controls.Shapes.Line
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(12, 0),
        Stroke = new SolidColorBrush(colour),
        StrokeThickness = 2,
        StrokeDashArray = dashed ? [2, 2] : null,
        Width = 12,
        Height = 2,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control Placed(Control control, int column, double leftMargin)
    {
        SetColumn(control, column);
        control.Margin = new Thickness(leftMargin, 0, 0, 0);
        return control;
    }

    private static Control WithMargin(Control control, Thickness margin)
    {
        control.Margin = margin;
        return control;
    }

    private void SetBanner(DetectionResult? result, DateTimeOffset? at, string summary)
    {
        _bannerNotes.Children.Clear();

        if (result is null)
        {
            _bannerTitle.Inlines?.Clear();
            _bannerTitle.Text = "Auto-detect: not run this session";
            _bannerTitle.Foreground = Palette.TextStrong.Brush;
            _bannerSub.Text = string.IsNullOrEmpty(summary) ? "Select a region to detect the list layout." : summary;
            _bannerIcon.Child = null;
            _bannerIcon.Background = null;
            _bannerIcon.BorderBrush = Palette.ControlBorder.Brush;
            return;
        }

        (string word, Swatch colour, bool good) = result.Confidence switch
        {
            DetectionConfidence.Good => ("good", Palette.Accent, true),
            DetectionConfidence.Partial => ("partial", Palette.Warning, false),
            _ => ("failed", Palette.Hostile, false),
        };

        _bannerTitle.Inlines = new Avalonia.Controls.Documents.InlineCollection
        {
            new Avalonia.Controls.Documents.Run("Auto-detect: ") { Foreground = Palette.TextStrong.Brush },
            new Avalonia.Controls.Documents.Run(word) { Foreground = colour.Brush },
        };
        _bannerSub.Text = at is { } time ? $"Detected {time.ToLocalTime():HH:mm:ss} {summary}".TrimEnd() : summary;

        _bannerIcon.Background = good ? Palette.AccentFill.Brush : colour.WithOpacity(0.12);
        _bannerIcon.BorderBrush = good ? Palette.AccentBorder.Brush : colour.Brush;
        _bannerIcon.Child = good ? new Icon(IconKind.Check, Palette.AccentLight.Brush, 16, 1.5) : new Icon(IconKind.Warning, colour.Brush, 16, 2);

        foreach (string note in result.Notes)
        {
            TextBlock line = Ui.Label($"· {note}", 12);
            line.TextWrapping = TextWrapping.Wrap;
            _bannerNotes.Children.Add(line);
        }
    }

    private void OnFieldChanged()
    {
        if (_loading)
        {
            return;
        }

        _slotsHint.Text = SlotsText();
        UpdateDirty();

        // Redraw shortly after the last keystroke instead of on every one.
        _renderDelay.Stop();
        _renderDelay.Start();
    }

    private void RevertToDetected()
    {
        if (_baseline is null)
        {
            return;
        }

        _loading = true;
        WriteFields(_baseline);
        _loading = false;
        UpdateDirty();
        RenderGuides();
    }

    private void WriteFields(Calibration c)
    {
        Set(Field.IconX, c.IconX);
        Set(Field.IconSize, c.IconSize);
        Set(Field.NameX, c.NameX);
        Set(Field.NameRight, c.NameRight);
        Set(Field.RowPitch, c.RowPitch);
        Set(Field.ListTop, c.ListRect.Y);
        Set(Field.ListHeight, c.ListRect.Height);
        Set(Field.HeaderX, c.HeaderCountRect?.X ?? 0);
        Set(Field.HeaderY, c.HeaderCountRect?.Y ?? 0);
        Set(Field.HeaderWidth, c.HeaderCountRect?.Width ?? 0);
        Set(Field.HeaderHeight, c.HeaderCountRect?.Height ?? 0);
        _slotsHint.Text = SlotsText();
    }

    private void Set(Field field, int value) => _steppers[field].Value = value;

    private int Get(Field field) => _steppers[field].Value;

    private string SlotsText() => Get(Field.RowPitch) > 0 ? $"{Get(Field.ListHeight) / Get(Field.RowPitch)} slots" : string.Empty;

    /// <summary>Null when the fields do not describe a usable calibration (nothing detected yet).</summary>
    private Calibration? ReadDraft()
    {
        int nameRight = Get(Field.NameRight);
        int pitch = Get(Field.RowPitch);
        int iconSize = Get(Field.IconSize);
        if (nameRight <= 0 || pitch <= 0 || iconSize <= 0)
        {
            return null;
        }

        PixelRect? header = Get(Field.HeaderWidth) > 0 && Get(Field.HeaderHeight) > 0
            ? new PixelRect(Get(Field.HeaderX), Get(Field.HeaderY), Get(Field.HeaderWidth), Get(Field.HeaderHeight))
            : null;

        // The list's left edge is not editable (it is always the region's left), so keep whatever the
        // baseline had rather than assuming 0.
        int listX = _baseline?.ListRect.X ?? 0;
        return new Calibration(
            new PixelRect(listX, Get(Field.ListTop), nameRight - listX, Get(Field.ListHeight)),
            Get(Field.IconX),
            Get(Field.NameX),
            nameRight,
            header,
            pitch,
            iconSize);
    }

    private void UpdateDirty()
    {
        int changes = CountChanges();
        bool dirty = changes > 0;
        _dirtyText.Text = dirty ? $"{changes} unapplied change{(changes == 1 ? string.Empty : "s")}" : "No unapplied changes";
        _dirtyText.Foreground = dirty ? Palette.Warning.Brush : Palette.TextLabel.Brush;
        _apply.IsEnabled = dirty;
        _revert.IsEnabled = dirty && _baseline is not null;

        if (dirty != IsDirty)
        {
            IsDirty = dirty;
            DirtyChanged?.Invoke(dirty);
        }
    }

    private int CountChanges()
    {
        if (_baseline is null)
        {
            return _steppers.Values.Any(s => s.Value != 0) ? 1 : 0;
        }

        Calibration c = _baseline;
        (Field Field, int Baseline)[] pairs =
        [
            (Field.IconX, c.IconX), (Field.IconSize, c.IconSize), (Field.NameX, c.NameX), (Field.NameRight, c.NameRight),
            (Field.RowPitch, c.RowPitch), (Field.ListTop, c.ListRect.Y), (Field.ListHeight, c.ListRect.Height),
            (Field.HeaderX, c.HeaderCountRect?.X ?? 0), (Field.HeaderY, c.HeaderCountRect?.Y ?? 0),
            (Field.HeaderWidth, c.HeaderCountRect?.Width ?? 0), (Field.HeaderHeight, c.HeaderCountRect?.Height ?? 0),
        ];

        return pairs.Count(p => Get(p.Field) != p.Baseline);
    }

    private void RenderGuides()
    {
        if (_frame is not { } frame)
        {
            _guides.Source = null;
            return;
        }

        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap = PreviewRenderer.CreateBitmap(frame.Width, frame.Height);
            _guides.Source = _bitmap;
        }

        PreviewRenderer.RenderGuides(_bitmap, frame, ReadDraft());
        ApplyZoomSize();
        _guides.InvalidateVisual();
    }

    private void ApplyZoomSize()
    {
        if (_frame is not { } frame)
        {
            return;
        }

        // 1 physical pixel is 1/scaling DIP; zoom multiplies that, so a guide column is exactly
        // `zoom` physical pixels wide whatever the monitor scaling.
        _guides.Width = frame.Width / _renderScaling * _zoom;
        _guides.Height = frame.Height / _renderScaling * _zoom;
    }
}
