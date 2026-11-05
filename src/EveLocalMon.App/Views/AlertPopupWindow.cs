using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using EveLocalMon.Core.Alerts;

namespace EveLocalMon.App.Views;

/// <summary>
/// The app's own notification, deliberately not a Windows toast: Focus Assist / Do Not Disturb
/// routinely suppresses toast banners during fullscreen games, which is exactly when this alert
/// matters. <see cref="Window.ShowActivated"/> is false so it never takes focus off the game.
///
/// The window is larger than the card by <see cref="ShadowMargin"/> on every side, so the card's
/// shadow has somewhere to draw on a transparent window.
/// </summary>
public sealed class AlertPopupWindow : Window
{
    public const double ShadowMargin = 8;
    public const double PopupWidth = AlertPopupView.ViewWidth + (2 * ShadowMargin);
    public const double PopupHeight = AlertPopupView.ViewHeight + (2 * ShadowMargin);

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(33);

    private readonly AlertPopupView _view;
    private readonly DispatcherTimer _timer;
    private readonly TimeSpan _lifetime;
    private TimeSpan _remaining;
    private bool _hovering;

    public AlertPopupWindow(AlertMessage message, string meta, TimeSpan lifetime)
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = PopupWidth;
        Height = PopupHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        _lifetime = lifetime;
        _remaining = lifetime;
        _view = new AlertPopupView(message, meta) { Margin = new Thickness(ShadowMargin) };
        Content = _view;

        PointerPressed += (_, _) => Close();

        // Hovering holds the card open and refills it: the user is reading it.
        PointerEntered += (_, _) =>
        {
            _hovering = true;
            _remaining = _lifetime;
            _view.SetRemaining(1);
        };
        PointerExited += (_, _) => _hovering = false;

        _timer = new DispatcherTimer { Interval = Tick };
        _timer.Tick += OnTick;
        _timer.Start();

        Closed += (_, _) => _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_hovering)
        {
            return;
        }

        _remaining -= Tick;
        _view.SetRemaining(_remaining / _lifetime);

        if (_remaining <= TimeSpan.Zero)
        {
            Close();
        }
    }
}
