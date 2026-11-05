using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EveLocalMon.App.Theme;

namespace EveLocalMon.App.Controls;

/// <summary>
/// A 0-100 slider: 2 px track, accent-filled up to a 10 x 14 rectangular thumb
/// (doc/specs/M4-SPEC.md §3). <see cref="Committed"/> fires when a drag or key press finishes, which
/// is when the volume slider plays its preview chime.
/// </summary>
public sealed class LevelSlider : Control
{
    private const double ThumbWidth = 10;
    private const double ThumbHeight = 14;

    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<LevelSlider, int>(nameof(Value), 70, coerce: (_, value) => Math.Clamp(value, 0, 100));

    private bool _dragging;

    static LevelSlider()
    {
        AffectsRender<LevelSlider>(ValueProperty);
        FocusableProperty.OverrideDefaultValue<LevelSlider>(true);
    }

    public LevelSlider(string automationName)
    {
        AutomationProperties.SetName(this, automationName);
        Cursor = new Cursor(StandardCursorType.Hand);
        Height = 18;
        MinWidth = 80;
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The user moved the thumb (drag or keys). Not raised when code sets <see cref="Value"/>.</summary>
    public event Action<int>? Changed;

    /// <summary>The user finished a drag or a key press.</summary>
    public event Action<int>? Committed;

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double mid = Bounds.Height / 2;
        double x = ThumbWidth / 2 + (width - ThumbWidth) * Value / 100.0;

        context.DrawRectangle(Palette.Hairline.Brush, null, new Rect(0, mid - 1, width, 2));
        context.DrawRectangle(Palette.Accent.Brush, null, new Rect(0, mid - 1, x, 2));

        IBrush thumb = _dragging || IsPointerOver ? Palette.TextStrong.Brush : Palette.TextBody.Brush;
        context.DrawRectangle(thumb, null, new Rect(x - ThumbWidth / 2, mid - ThumbHeight / 2, ThumbWidth, ThumbHeight));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        e.Pointer.Capture(this);
        SetFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            SetFromPoint(e.GetPosition(this));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
        Committed?.Invoke(Value);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 5;
        int? next = e.Key switch
        {
            Key.Left or Key.Down => Value - step,
            Key.Right or Key.Up => Value + step,
            Key.Home => 0,
            Key.End => 100,
            _ => null,
        };

        if (next is { } target)
        {
            Set(target);
            Committed?.Invoke(Value);
            e.Handled = true;
        }
    }

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

    private void SetFromPoint(Point point)
    {
        double usable = Math.Max(1, Bounds.Width - ThumbWidth);
        Set((int)Math.Round((point.X - ThumbWidth / 2) / usable * 100));
    }

    private void Set(int value)
    {
        int before = Value;
        Value = value;
        if (Value != before)
        {
            Changed?.Invoke(Value);
        }
    }
}
