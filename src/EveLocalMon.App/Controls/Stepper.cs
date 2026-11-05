using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using EveLocalMon.App.Theme;

namespace EveLocalMon.App.Controls;

/// <summary>
/// "−  17  +": a mono integer field with step buttons. Arrow keys step by 1 (Shift: 10); typing is
/// committed on Enter or when focus leaves, and an unparsable entry snaps back to the last value
/// (doc/specs/M4-SPEC.md §10).
/// </summary>
public sealed class Stepper : Grid
{
    private readonly TextBox _box;
    private int _value;

    public Stepper(string automationName, int minimum = 0, int maximum = 10000)
    {
        Minimum = minimum;
        Maximum = maximum;

        ColumnDefinitions = new ColumnDefinitions("Auto,52,Auto");

        _box = new TextBox
        {
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(0),
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            BorderThickness = new Thickness(0, 1),
            Text = "0",
        };
        _box.Classes.Add(EveTheme.Mono);
        AutomationProperties.SetName(_box, automationName);
        _box.KeyDown += OnBoxKeyDown;
        _box.LostFocus += (_, _) => CommitText();
        _box.GotFocus += (_, _) => _box.SelectAll();

        Button minus = StepButton(IconKind.Minus, $"Decrease {automationName}", -1);
        Button plus = StepButton(IconKind.Plus, $"Increase {automationName}", +1);
        SetColumn(_box, 1);
        SetColumn(plus, 2);
        Children.Add(minus);
        Children.Add(_box);
        Children.Add(plus);
    }

    public int Minimum { get; }
    public int Maximum { get; }

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, Minimum, Maximum);
            _box.Text = _value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Raised for a change the user made, with the new value.</summary>
    public event Action<int>? ValueChanged;

    private Button StepButton(IconKind icon, string name, int direction)
    {
        var button = new Button
        {
            Width = 24,
            Height = 26,
            Padding = new Thickness(0),
            Content = new Icon(icon, Palette.TextData.Brush, 8, 1.3),
        };
        AutomationProperties.SetName(button, name);

        // Click carries no modifier keys, so note Shift when the press arrives.
        bool shift = false;
        button.AddHandler(PointerPressedEvent, (_, e) => shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.Click += (_, _) => UserSet(_value + direction * (shift ? 10 : 1));
        return button;
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Up:
                UserSet(_value + step);
                e.Handled = true;
                break;
            case Key.Down:
                UserSet(_value - step);
                e.Handled = true;
                break;
            case Key.Enter:
                CommitText();
                e.Handled = true;
                break;
        }
    }

    private void CommitText()
    {
        if (int.TryParse(_box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int typed))
        {
            UserSet(typed);
        }
        else
        {
            Value = _value;
        }
    }

    private void UserSet(int value)
    {
        int before = _value;
        Value = value;
        if (_value != before)
        {
            ValueChanged?.Invoke(_value);
        }
    }
}
