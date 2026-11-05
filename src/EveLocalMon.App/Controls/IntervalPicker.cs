using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using EveLocalMon.App.Theme;

namespace EveLocalMon.App.Controls;

/// <summary>"every 250 500 1000 2000 ms": the scan interval, a row of flat text buttons.</summary>
public sealed class IntervalPicker : StackPanel
{
    public static readonly int[] Choices = [250, 500, 1000, 2000];

    private readonly List<(int Ms, Button Button, TextBlock Text)> _options = [];
    private int _value = 500;

    public IntervalPicker()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 0;
        Children.Add(Ui.Label("every "));

        foreach (int ms in Choices)
        {
            var text = Ui.Text(ms.ToString(), 11, Palette.TextLabel.Brush, mono: true);
            var button = new Button { Content = text, Height = 18, MinWidth = 0 };
            EveTheme.SetKind(button, ButtonKind.Icon);
            button.Width = double.NaN;
            button.Height = 18;
            button.Padding = new Avalonia.Thickness(4, 0);
            AutomationProperties.SetName(button, $"Scan every {ms} milliseconds");
            button.Click += (_, _) => Pick(ms, notify: true);
            _options.Add((ms, button, text));
            Children.Add(button);
        }

        Children.Add(Ui.Label(" ms"));
        Refresh();
    }

    /// <summary>The nearest of the offered choices, so an old saved value like 400 still shows something selected.</summary>
    public int Value
    {
        get => _value;
        set
        {
            _value = Choices.MinBy(choice => Math.Abs(choice - value));
            Refresh();
        }
    }

    public event Action<int>? Changed;

    private void Pick(int ms, bool notify)
    {
        _value = ms;
        Refresh();
        if (notify)
        {
            Changed?.Invoke(ms);
        }
    }

    private void Refresh()
    {
        foreach ((int ms, _, TextBlock text) in _options)
        {
            text.Foreground = ms == _value ? Palette.Accent.Brush : Palette.TextLabel.Brush;
        }
    }
}
