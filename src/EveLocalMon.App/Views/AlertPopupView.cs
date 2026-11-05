using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Alerts;

namespace EveLocalMon.App.Views;

/// <summary>
/// The look of one alert (doc/specs/M4-SPEC.md §9): a 2 px kind-coloured bar on top, an icon, title,
/// time, body, a meta line and a countdown bar along the bottom. Hosted by <see cref="AlertPopupWindow"/>
/// for real alerts and drawn as a static sample on the Alerts tab, so the sample can never drift from
/// the real thing.
/// </summary>
public sealed class AlertPopupView : Border
{
    public const double ViewWidth = 360;
    public const double ViewHeight = 96;

    private readonly Border _countdown;

    public AlertPopupView(AlertMessage message, string meta)
    {
        (IconKind icon, Swatch colour) = Describe(message.Kind);

        Width = ViewWidth;
        Height = ViewHeight;
        Background = Palette.Window.WithOpacity(0.96);
        BorderBrush = Palette.ControlBorder.Brush;
        BorderThickness = new Thickness(1);
        BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, OffsetY = 6, Color = Color.FromArgb(0x73, 0, 0, 0) });
        ClipToBounds = true;

        var text = new StackPanel
        {
            Margin = new Thickness(0, 11, 12, 8),
            Spacing = 1,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        Ui.Text(message.Title, 15, colour.Brush, FontWeight.SemiBold),
                        Placed(Ui.Mono(message.At.ToLocalTime().ToString("HH:mm:ss"), 11, Palette.TextLabel.Brush), 1),
                    },
                },
                new TextBlock
                {
                    Text = message.Body,
                    FontSize = 12,
                    Foreground = Palette.TextBody.Brush,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                new TextBlock
                {
                    Text = meta,
                    FontSize = 11,
                    Foreground = Palette.TextLabel.Brush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };

        var iconColumn = new Panel
        {
            Width = 52,
            Children = { new Icon(icon, colour.Brush, 24, 1.8) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
        };

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("52,*"), Children = { iconColumn, text } };
        Grid.SetColumn(text, 1);

        _countdown = new Border
        {
            Height = 2,
            Background = Palette.TextDisabled.Brush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Width = ViewWidth - 2,
        };

        Child = new Panel
        {
            Children =
            {
                content,
                new Border { Height = 2, Background = colour.Brush, VerticalAlignment = VerticalAlignment.Top },
                _countdown,
            },
        };
    }

    /// <summary>1 = full time left, 0 = about to close.</summary>
    public void SetRemaining(double fraction) => _countdown.Width = (ViewWidth - 2) * Math.Clamp(fraction, 0, 1);

    private static Control Placed(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    /// <summary>Overflow and "pilots joined while unreadable" share an icon: both mean a list too long
    /// to read. Not-visible has its own, so the two kinds of "cannot confirm" look different.</summary>
    private static (IconKind Icon, Swatch Colour) Describe(AlertKind kind) => kind switch
    {
        AlertKind.ThreatIncreased => (IconKind.Warning, Palette.Hostile),
        AlertKind.Unreadable => (IconKind.EyeOff, Palette.Warning),
        AlertKind.Overflow or AlertKind.MemberCountWhileUnreadable => (IconKind.ListOverflow, Palette.Warning),
        _ => (IconKind.CheckCircle, Palette.Accent),
    };
}
