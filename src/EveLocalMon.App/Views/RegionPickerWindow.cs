using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Platform.Windows;
using CorePixelRect = EveLocalMon.Core.Imaging.PixelRect;

namespace EveLocalMon.App.Views;

/// <summary>
/// A dimmed overlay on every monitor; drag to pick the Local pane (doc/specs/M4-SPEC.md §11).
///
/// Everything outside the selection is dimmed and the inside is not, so what you see inside is what
/// will be captured. The fiddly part, from M2, is that Avalonia positions windows in physical pixels
/// but sizes them in DIPs, so on a scaled monitor the two must be converted separately or the overlay
/// covers only part of the screen (doc/specs/M2-SPEC.md §3).
/// </summary>
public sealed class RegionPickerWindow : Window
{
    private const int LoupeCells = 14;
    private const double LoupeSize = 112;
    private const double CardGap = 10;

    private readonly Screen _screen;
    private readonly int _monitorNumber;
    private readonly Frame? _snapshot;
    private readonly TaskCompletionSource<CorePixelRect?> _result;
    private readonly Canvas _canvas = new();
    private readonly Rectangle[] _scrim = [new(), new(), new(), new()];
    private readonly Border _selection;
    private readonly Border[] _ticks = [new(), new(), new(), new()];
    private readonly Border _readout;
    private readonly TextBlock _size = Ui.Mono("", 14);
    private readonly TextBlock _where = Ui.Label("");
    private readonly TextBlock _widthCheck = Ui.Text("", 11);
    private readonly Control _widthTick = new Icon(IconKind.Check, Palette.Accent.Brush, 10, 1.4);
    private readonly Control _widthWarn = new Icon(IconKind.Warning, Palette.Warning.Brush, 11, 2);
    private readonly Border _loupe;
    private readonly WriteableBitmap _loupeBitmap = new(new PixelSize(LoupeCells, LoupeCells), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
    private (int X, int Y)? _dragStartScreen;
    private bool _activated;

    private RegionPickerWindow(Screen screen, int monitorNumber, Frame? snapshot, TaskCompletionSource<CorePixelRect?> result)
    {
        _screen = screen;
        _monitorNumber = monitorNumber;
        _snapshot = snapshot;
        _result = result;

        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        // Not fully transparent: a window with zero alpha everywhere can let clicks fall through to
        // whatever is underneath, and the selection interior must still receive the drag.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = true;

        Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;

        foreach (Rectangle rect in _scrim)
        {
            rect.Fill = Palette.Scrim;
            rect.IsHitTestVisible = false;
            _canvas.Children.Add(rect);
        }

        // Before a drag starts the whole screen is dimmed.
        SetScrim(0, 0, Width, Height, Width, Height, hasSelection: false);

        _selection = new Border { BorderBrush = Palette.Accent.Brush, BorderThickness = new Thickness(1), IsVisible = false, IsHitTestVisible = false };
        _canvas.Children.Add(_selection);

        Thickness[] tickShapes = [new(2, 2, 0, 0), new(0, 2, 2, 0), new(2, 0, 0, 2), new(0, 0, 2, 2)];
        for (int i = 0; i < _ticks.Length; i++)
        {
            _ticks[i].Width = 8;
            _ticks[i].Height = 8;
            _ticks[i].BorderBrush = Palette.AccentLight.Brush;
            _ticks[i].BorderThickness = tickShapes[i];
            _ticks[i].IsVisible = false;
            _ticks[i].IsHitTestVisible = false;
            _canvas.Children.Add(_ticks[i]);
        }

        _readout = BuildReadout();
        _readout.IsVisible = false;
        _loupe = BuildLoupe();
        _loupe.IsVisible = false;
        _canvas.Children.Add(_readout);
        _canvas.Children.Add(_loupe);

        var layers = new Grid();
        layers.Children.Add(_canvas);
        layers.Children.Add(BuildHint());
        Content = layers;

        PointerEntered += (_, _) =>
        {
            // Only the focused overlay receives Esc, so focus whichever monitor the cursor is on.
            if (!_activated)
            {
                _activated = true;
                Activate();
            }
        };
        PointerExited += (_, _) =>
        {
            _activated = false;
            _loupe.IsVisible = false;
        };
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Shows an overlay on every monitor and resolves with the chosen region in screen coordinates
    /// (physical pixels), or null if cancelled. Each monitor is captured once first, for the loupe: the
    /// screen is static while picking, so a live capture per mouse move would be pure cost.
    /// </summary>
    public static async Task<CorePixelRect?> PickAsync(Window owner)
    {
        var result = new TaskCompletionSource<CorePixelRect?>();
        IReadOnlyList<Screen> screens = owner.Screens.All;

        var windows = new List<RegionPickerWindow>();
        using (var capture = new GdiScreenCapture())
        {
            for (int i = 0; i < screens.Count; i++)
            {
                windows.Add(new RegionPickerWindow(screens[i], i + 1, TryCapture(capture, screens[i]), result));
            }
        }

        foreach (RegionPickerWindow window in windows)
        {
            window.Show();
        }

        windows.FirstOrDefault()?.Activate();

        try
        {
            return await result.Task;
        }
        finally
        {
            foreach (RegionPickerWindow window in windows)
            {
                window.Close();
            }
        }
    }

    private static Frame? TryCapture(GdiScreenCapture capture, Screen screen)
    {
        try
        {
            return capture.Capture(new CorePixelRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height));
        }
        catch (Exception)
        {
            // No loupe on this monitor is better than no picker.
            return null;
        }
    }

    private Control BuildHint()
    {
        var keycap = new Border
        {
            BorderBrush = Palette.TextDisabled.Brush,
            BorderThickness = new Thickness(1, 1, 1, 2),
            Padding = new Thickness(6, 1),
            Child = Ui.Mono("Esc", 11, Palette.TextBody.Brush),
        };

        var card = new Border
        {
            Background = Palette.TitleBar.WithOpacity(0.94),
            BorderBrush = Palette.ControlBorder.Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 0, 0),
            IsHitTestVisible = false,
            Child = Ui.Row(16,
                new Icon(IconKind.Region, Palette.Accent.Brush, 22, 1.2),
                Ui.Column(1,
                    Ui.Text("Drag around the Local member list", 15, Palette.TextStrong.Brush),
                    Ui.Label("Just the names column — leave out the chat text", 12)),
                new Border
                {
                    BorderBrush = Palette.Divider.Brush,
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Padding = new Thickness(16, 0, 0, 0),
                    Child = Ui.Row(6, keycap, Ui.Label("cancel", 12)),
                }),
        };
        return card;
    }

    private Border BuildReadout()
    {
        _widthWarn.IsVisible = false;
        return new Border
        {
            Background = Palette.TitleBar.WithOpacity(0.94),
            BorderBrush = Palette.ControlBorder.Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6),
            IsHitTestVisible = false,
            Child = Ui.Column(2, _size, _where, Ui.Row(5, _widthTick, _widthWarn, _widthCheck)),
        };
    }

    private Border BuildLoupe()
    {
        var image = new Image { Source = _loupeBitmap, Width = LoupeSize, Height = LoupeSize };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);

        double cell = LoupeSize / LoupeCells;
        double centre = (LoupeCells / 2) * cell;
        var cross = new Border
        {
            Width = cell,
            Height = cell,
            BorderBrush = Palette.Accent.Brush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(centre, centre, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var scale = Ui.Mono("8×", 10, Palette.TextData.Brush);
        scale.HorizontalAlignment = HorizontalAlignment.Right;
        scale.VerticalAlignment = VerticalAlignment.Bottom;
        scale.Margin = new Thickness(0, 0, 4, 2);

        return new Border
        {
            Width = LoupeSize + 2,
            Height = LoupeSize + 2,
            Background = Palette.Field.Brush,
            BorderBrush = Palette.Accent.Brush,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Child = new Panel { Children = { image, cross, scale } },
        };
    }

    private (int X, int Y) ToScreen(Point pointInWindow) => (
        CoordinateSpaces.DipToPhysical(_screen.Bounds.X, pointInWindow.X, _screen.Scaling),
        CoordinateSpaces.DipToPhysical(_screen.Bounds.Y, pointInWindow.Y, _screen.Scaling));

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            _result.TrySetResult(null);
            return;
        }

        _dragStartScreen = ToScreen(point.Position);
        _selection.IsVisible = true;
        _readout.IsVisible = true;
        foreach (Border tick in _ticks)
        {
            tick.IsVisible = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point position = e.GetPosition(this);
        UpdateLoupe(position);

        if (_dragStartScreen is not { } start) return;

        UpdateSelection(start, ToScreen(position));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStartScreen is not { } start) return;

        CorePixelRect region = BuildRegion(start, ToScreen(e.GetPosition(this)));
        _dragStartScreen = null;

        // Too small to be a deliberate selection — treat as a mis-click, not a 3px region.
        if (region.Width < 20 || region.Height < 20)
        {
            _selection.IsVisible = false;
            _readout.IsVisible = false;
            foreach (Border tick in _ticks)
            {
                tick.IsVisible = false;
            }

            SetScrim(0, 0, Width, Height, Width, Height, hasSelection: false);
            return;
        }

        _result.TrySetResult(region);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _result.TrySetResult(null);
        }
    }

    private CorePixelRect BuildRegion((int X, int Y) start, (int X, int Y) end)
    {
        CorePixelRect raw = CoordinateSpaces.FromCorners(start.X, start.Y, end.X, end.Y);
        var bounds = new CorePixelRect(_screen.Bounds.X, _screen.Bounds.Y, _screen.Bounds.Width, _screen.Bounds.Height);
        return CoordinateSpaces.ClampTo(raw, bounds);
    }

    private void UpdateSelection((int X, int Y) start, (int X, int Y) current)
    {
        CorePixelRect region = BuildRegion(start, current);

        double left = CoordinateSpaces.PhysicalToDip(_screen.Bounds.X, region.X, _screen.Scaling);
        double top = CoordinateSpaces.PhysicalToDip(_screen.Bounds.Y, region.Y, _screen.Scaling);
        double width = CoordinateSpaces.PhysicalSizeToDip(region.Width, _screen.Scaling);
        double height = CoordinateSpaces.PhysicalSizeToDip(region.Height, _screen.Scaling);

        Canvas.SetLeft(_selection, left);
        Canvas.SetTop(_selection, top);
        _selection.Width = width;
        _selection.Height = height;
        SetScrim(left, top, width, height, Width, Height, hasSelection: true);

        double[] tickX = [left - 3, left + width - 5, left - 3, left + width - 5];
        double[] tickY = [top - 3, top - 3, top + height - 5, top + height - 5];
        for (int i = 0; i < _ticks.Length; i++)
        {
            Canvas.SetLeft(_ticks[i], tickX[i]);
            Canvas.SetTop(_ticks[i], tickY[i]);
        }

        _size.Text = $"{region.Width} × {region.Height} px";
        _where.Text = $"at {region.X}, {region.Y} · monitor {_monitorNumber} · {Math.Round(_screen.Scaling * 100)}%";

        bool fits = RegionHeuristics.IsPlausibleListWidth(region.Width);
        _widthTick.IsVisible = fits;
        _widthWarn.IsVisible = !fits;
        _widthCheck.Text = fits ? "Width fits a member list" : "Wider than a member list — include just the names column";
        _widthCheck.Foreground = fits ? Palette.Accent.Brush : Palette.Warning.Brush;

        PlaceBeside(_readout, left, top, width, height);
    }

    /// <summary>Positions the readout to the right of the selection, flipping to the left near the screen edge.</summary>
    private void PlaceBeside(Control card, double left, double top, double width, double height)
    {
        card.Measure(Size.Infinity);
        Size size = card.DesiredSize;

        double x = left + width + CardGap;
        if (x + size.Width > Width)
        {
            x = left - size.Width - CardGap;
        }

        if (x < 0)
        {
            x = left + CardGap;
        }

        double y = Math.Clamp(top, 0, Math.Max(0, Height - size.Height));
        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, y);
    }

    /// <summary>Four rectangles around the selection, so its interior is left undimmed.</summary>
    private void SetScrim(double left, double top, double width, double height, double screenWidth, double screenHeight, bool hasSelection)
    {
        if (!hasSelection)
        {
            Place(_scrim[0], 0, 0, screenWidth, screenHeight);
            Place(_scrim[1], 0, 0, 0, 0);
            Place(_scrim[2], 0, 0, 0, 0);
            Place(_scrim[3], 0, 0, 0, 0);
            return;
        }

        double right = left + width;
        double bottom = top + height;
        Place(_scrim[0], 0, 0, screenWidth, top);
        Place(_scrim[1], 0, bottom, screenWidth, Math.Max(0, screenHeight - bottom));
        Place(_scrim[2], 0, top, left, height);
        Place(_scrim[3], right, top, Math.Max(0, screenWidth - right), height);

        static void Place(Rectangle rect, double x, double y, double w, double h)
        {
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = Math.Max(0, w);
            rect.Height = Math.Max(0, h);
        }
    }

    /// <summary>Redraws the 14 x 14 physical pixels around the cursor from the snapshot taken at open.</summary>
    private void UpdateLoupe(Point position)
    {
        if (_snapshot is not { } frame)
        {
            return;
        }

        (int screenX, int screenY) = ToScreen(position);
        int cx = screenX - _screen.Bounds.X;
        int cy = screenY - _screen.Bounds.Y;
        int half = LoupeCells / 2;

        using (ILockedFramebuffer fb = _loupeBitmap.Lock())
        {
            var row = new byte[LoupeCells * 4];
            Color blank = Palette.Field.Color;
            for (int y = 0; y < LoupeCells; y++)
            {
                for (int x = 0; x < LoupeCells; x++)
                {
                    int sx = cx - half + x;
                    int sy = cy - half + y;
                    int o = x * 4;
                    if ((uint)sx < (uint)frame.Width && (uint)sy < (uint)frame.Height)
                    {
                        int src = (sy * frame.Stride) + (sx * 4);
                        row[o] = frame.Pixels[src];
                        row[o + 1] = frame.Pixels[src + 1];
                        row[o + 2] = frame.Pixels[src + 2];
                    }
                    else
                    {
                        row[o] = blank.B;
                        row[o + 1] = blank.G;
                        row[o + 2] = blank.R;
                    }

                    row[o + 3] = 0xFF;
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + (y * fb.RowBytes), row.Length);
            }
        }

        _loupe.IsVisible = true;
        _loupe.Measure(Size.Infinity);
        Size size = _loupe.DesiredSize;

        double x0 = position.X + 24;
        double y0 = position.Y + 24;
        if (x0 + size.Width > Width) x0 = position.X - 24 - size.Width;
        if (y0 + size.Height > Height) y0 = position.Y - 24 - size.Height;
        Canvas.SetLeft(_loupe, Math.Max(0, x0));
        Canvas.SetTop(_loupe, Math.Max(0, y0));
    }
}
