using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EveLocalMon.App.Theme;

/// <summary>Small factories so views read as layout, not as property lists. No colour literals here
/// or anywhere else outside <see cref="Palette"/>.</summary>
public static class Ui
{
    public static TextBlock Text(
        string text,
        double size = 12,
        IBrush? brush = null,
        FontWeight weight = FontWeight.Normal,
        bool mono = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? Palette.TextBody.Brush,
        FontWeight = weight,
        FontFamily = mono ? Fonts.Mono : Fonts.Ui,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>11 px grey label.</summary>
    public static TextBlock Label(string text, double size = 11) => Text(text, size, Palette.TextLabel.Brush);

    public static TextBlock Mono(string text, double size = 12, IBrush? brush = null) => Text(text, size, brush ?? Palette.TextStrong.Brush, mono: true);

    public static Button Button(string text, ButtonKind kind = ButtonKind.Normal, Action? onClick = null, bool dense = false)
    {
        var button = new Button { Content = text };
        return Configure(button, kind, onClick, dense);
    }

    public static Button Button(Control content, ButtonKind kind = ButtonKind.Normal, Action? onClick = null, bool dense = false)
    {
        var button = new Button { Content = content };
        return Configure(button, kind, onClick, dense);
    }

    /// <summary>Icon on the left, label to its right.</summary>
    public static Button Button(IconKind icon, string text, ButtonKind kind = ButtonKind.Normal, Action? onClick = null, bool dense = false)
    {
        var button = new Button();
        Configure(button, kind, onClick, dense);

        // Follows the button's own foreground, so hover and pressed colours reach the icon too.
        var glyph = new Icon(icon, Palette.TextBody.Brush, 12);
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { glyph, label } };
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty && button.Foreground is { } foreground)
            {
                glyph.Brush = foreground;
            }
        };
        return button;
    }

    public static Button IconButton(IconKind icon, string automationName, Action? onClick = null, double iconSize = 12)
    {
        var button = new Button { Content = new Icon(icon, Palette.TextLabel.Brush, iconSize) };
        AutomationProperties.SetName(button, automationName);
        ToolTip.SetTip(button, automationName);
        EveTheme.SetKind(button, ButtonKind.Icon);
        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    private static Button Configure(Button button, ButtonKind kind, Action? onClick, bool dense)
    {
        EveTheme.SetKind(button, kind);
        if (dense)
        {
            button.Classes.Add(EveTheme.Dense);
        }

        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    public static Border HLine(IBrush? brush = null) => new() { Height = 1, Background = brush ?? Palette.Divider.Brush };

    public static Border VLine(IBrush? brush = null) => new() { Width = 1, Background = brush ?? Palette.Divider.Brush };

    public static StackPanel Row(double spacing = 8, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.AddRange(children);
        return panel;
    }

    public static StackPanel Column(double spacing = 8, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        panel.Children.AddRange(children);
        return panel;
    }

    /// <summary>A small square colour chip (category swatches, legend entries).</summary>
    public static Border Chip(Color color, double size = 8, bool outlineOnly = false) => new()
    {
        Width = size,
        Height = size,
        Background = outlineOnly ? null : new SolidColorBrush(color),
        BorderBrush = new SolidColorBrush(color),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center,
    };
}
