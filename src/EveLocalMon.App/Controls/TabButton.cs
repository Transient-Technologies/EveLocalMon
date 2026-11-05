using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EveLocalMon.App.Theme;

namespace EveLocalMon.App.Controls;

/// <summary>A tab header: 13 px text with a 2 px underline when active (doc/specs/M4-SPEC.md §3). A
/// plain button rather than a TabControl, so the three tab views stay alive and keep their state.</summary>
public sealed class TabButton : Button
{
    private readonly TextBlock _text;
    private readonly Border _underline;

    public TabButton(string text)
    {
        _text = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        _underline = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom };

        EveTheme.SetKind(this, ButtonKind.Icon);
        Width = double.NaN;
        Height = 35;
        Padding = new Thickness(2, 0);
        Margin = new Thickness(0, 0, 22, 0);
        Content = new Panel { Children = { _text, _underline } };
        Avalonia.Automation.AutomationProperties.SetName(this, text);
        IsActive = false;
    }

    public string Text
    {
        get => _text.Text ?? string.Empty;
        set
        {
            _text.Text = value;
            Avalonia.Automation.AutomationProperties.SetName(this, value);
        }
    }

    public bool IsActive
    {
        set
        {
            // Active text is set explicitly; inactive text inherits the button's grey-to-white hover.
            if (value)
            {
                _text.Foreground = Palette.TextStrong.Brush;
            }
            else
            {
                _text.ClearValue(TextBlock.ForegroundProperty);
            }

            _underline.Background = value ? Palette.TextBody.Brush : Brushes.Transparent;
        }
    }
}
