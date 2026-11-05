using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using EveLocalMon.App.Theme;

namespace EveLocalMon.App.Controls;

/// <summary>
/// The window's own title bar (doc/specs/M4-SPEC.md §5.1). The window uses
/// <c>WindowDecorations.BorderOnly</c>, which keeps the native resize edges but draws no caption, so
/// dragging, double-click-to-maximise and the three buttons are ours.
/// </summary>
public sealed class TitleBar : Border
{
    public const double BarHeight = 30;

    private readonly TextBlock _subtitle = Ui.Label("");
    private readonly Button _pin;
    private readonly Icon _pinIcon = new(IconKind.Pin, Palette.TextLabel.Brush, 14);
    private bool _pinned;

    public TitleBar(string version)
    {
        Height = BarHeight;
        Background = Palette.TitleBar.Brush;
        BorderBrush = Palette.Divider.Brush;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Subtitle = $"v{version} · stopped";

        _pin = new Button { Content = _pinIcon };
        EveTheme.SetKind(_pin, ButtonKind.Icon);
        _pin.Height = BarHeight - 1;
        Avalonia.Automation.AutomationProperties.SetName(_pin, "Keep window on top");
        ToolTip.SetTip(_pin, "Keep on top");
        _pin.Click += (_, _) => SetPinned(!_pinned, notify: true);

        Button minimise = Ui.IconButton(IconKind.Minimise, "Minimise", () => Window()?.SetCurrentValue(Avalonia.Controls.Window.WindowStateProperty, WindowState.Minimized));
        Button close = Ui.IconButton(IconKind.Close, "Close", () => Window()?.Close());
        minimise.Height = close.Height = BarHeight - 1;

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Icon(IconKind.Radar, Palette.Accent.Brush, 16),
                Ui.Text("EveLocalMon", 13, Palette.TextStrong.Brush, Avalonia.Media.FontWeight.Medium),
                _subtitle,
            },
        };

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 2, 0),
            Children = { _pin, minimise, close },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        Child = grid;

        PointerPressed += OnPointerPressed;
    }

    /// <summary>The grey text beside the app name: "v0.3 · watching Local" / "v0.3 · stopped".</summary>
    public string Subtitle
    {
        set => _subtitle.Text = value;
    }

    public bool IsPinned
    {
        get => _pinned;
        set => SetPinned(value, notify: false);
    }

    /// <summary>Raised when the user toggles the pin, with the new state.</summary>
    public event Action<bool>? PinToggled;

    private Window? Window() => TopLevel.GetTopLevel(this) as Window;

    private void SetPinned(bool pinned, bool notify)
    {
        _pinned = pinned;
        _pinIcon.Brush = pinned ? Palette.AccentLight.Brush : Palette.TextLabel.Brush;
        EveTheme.SetKind(_pin, pinned ? ButtonKind.Selected : ButtonKind.Icon);
        _pin.Classes.Add(EveTheme.Icon);

        if (notify)
        {
            PinToggled?.Invoke(pinned);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Window() is not { } window)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        window.BeginMoveDrag(e);
    }
}
