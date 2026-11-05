using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EveLocalMon.App.Theme;

namespace EveLocalMon.App.Controls;

/// <summary>
/// An 18 px square checkbox (doc/specs/M4-SPEC.md §3). Drawn directly so it matches the palette
/// exactly; focusable, toggled with Space or Enter, and given a focus ring by the theme.
/// </summary>
public sealed class ChannelCheck : Control
{
    private const double BoxSize = 18;

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ChannelCheck, bool>(nameof(IsChecked));

    private static readonly Geometry Tick = Geometry.Parse("M4.5 9.4l3 3 6-6.4");

    static ChannelCheck()
    {
        AffectsRender<ChannelCheck>(IsCheckedProperty, IsEnabledProperty);
        FocusableProperty.OverrideDefaultValue<ChannelCheck>(true);
    }

    public ChannelCheck(string automationName)
    {
        AutomationProperties.SetName(this, automationName);
        Cursor = new Cursor(StandardCursorType.Hand);
        Width = BoxSize;
        Height = BoxSize;
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>Raised only for a change the user made, not when code sets <see cref="IsChecked"/>.</summary>
    public event Action<bool>? Toggled;

    public override void Render(DrawingContext context)
    {
        bool on = IsChecked;
        IBrush fill = on ? Palette.AccentFill.Brush : Palette.Field.Brush;
        IBrush border = on ? Palette.Accent.Brush : IsPointerOver ? Palette.ControlBorderHover.Brush : Palette.ControlBorder.Brush;
        var rect = new Rect(0.5, 0.5, BoxSize - 1, BoxSize - 1);

        using (context.PushOpacity(IsEnabled ? 1 : 0.4))
        {
            context.DrawRectangle(fill, new Pen(border, 1), rect);
            if (on)
            {
                context.DrawGeometry(null, new Pen(Palette.AccentLight.Brush, 1.6), Tick);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(BoxSize, BoxSize);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Flip();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Space or Key.Enter)
        {
            Flip();
            e.Handled = true;
        }
    }

    private void Flip()
    {
        if (!IsEnabled)
        {
            return;
        }

        IsChecked = !IsChecked;
        Toggled?.Invoke(IsChecked);
    }
}
