using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls.Templates;

namespace EveLocalMon.App.Theme;

/// <summary>
/// The EVE look, layered over Fluent (doc/specs/M4-SPEC.md §3). Fluent keeps supplying control
/// templates and state handling; this replaces its colours, shape and type. Colour comes from
/// <see cref="Palette"/> only. Where Fluent reads a theme brush we override that brush
/// (<see cref="ApplyResources"/>) rather than re-template the control, which survives Fluent upgrades
/// far better than replacing template parts.
/// </summary>
public enum ButtonKind
{
    Normal,
    Primary,
    Selected,
    Danger,
    Icon,
}

public sealed class EveTheme : Styles
{
    public const string Icon = "icon";
    public const string Dense = "dense";
    public const string Mono = "mono";

    public EveTheme()
    {
        Add(WindowStyle());
        Add(ButtonBase());
        Add(IconButton());
        Add(new Style(x => x.OfType<Button>().Class(Dense)) { Setters = { new Setter(HeightProperty, 24.0), new Setter(PaddingProperty, new Thickness(10, 0)) } });
        Add(TextBoxStyle());
        Add(new Style(x => x.OfType<TextBox>().Class(Mono)) { Setters = { new Setter(TemplatedControl.FontFamilyProperty, Fonts.Mono) } });
        Add(FocusRing());
    }

    /// <summary>Overrides the Fluent theme brushes that decide every control's colours. Set on the
    /// application, which is consulted before Fluent's own dictionaries.</summary>
    public static void ApplyResources(IResourceDictionary resources)
    {
        // Buttons
        resources["ButtonBackground"] = Palette.ControlFill.Brush;
        resources["ButtonBackgroundPointerOver"] = Palette.ControlFillHover.Brush;
        resources["ButtonBackgroundPressed"] = Palette.ControlBorder.Brush;
        resources["ButtonBackgroundDisabled"] = Palette.Panel.Brush;
        resources["ButtonForeground"] = Palette.TextBody.Brush;
        resources["ButtonForegroundPointerOver"] = Palette.TextStrong.Brush;
        resources["ButtonForegroundPressed"] = Palette.TextStrong.Brush;
        resources["ButtonForegroundDisabled"] = Palette.TextDisabled.Brush;
        resources["ButtonBorderBrush"] = Palette.ControlBorder.Brush;
        resources["ButtonBorderBrushPointerOver"] = Palette.ControlBorderHover.Brush;
        resources["ButtonBorderBrushPressed"] = Palette.ControlBorderHover.Brush;
        resources["ButtonBorderBrushDisabled"] = Palette.Hairline.Brush;

        // Text boxes
        resources["TextControlBackground"] = Palette.Field.Brush;
        resources["TextControlBackgroundPointerOver"] = Palette.Field.Brush;
        resources["TextControlBackgroundFocused"] = Palette.Field.Brush;
        resources["TextControlBackgroundDisabled"] = Palette.Panel.Brush;
        resources["TextControlBorderBrush"] = Palette.ControlBorder.Brush;
        resources["TextControlBorderBrushPointerOver"] = Palette.ControlBorderHover.Brush;
        resources["TextControlBorderBrushFocused"] = Palette.Accent.Brush;
        resources["TextControlBorderBrushDisabled"] = Palette.Hairline.Brush;
        resources["TextControlForeground"] = Palette.TextStrong.Brush;
        resources["TextControlForegroundPointerOver"] = Palette.TextStrong.Brush;
        resources["TextControlForegroundFocused"] = Palette.TextStrong.Brush;
        resources["TextControlForegroundDisabled"] = Palette.TextDisabled.Brush;
        resources["TextControlPlaceholderForeground"] = Palette.TextLabel.Brush;
        resources["TextControlPlaceholderForegroundPointerOver"] = Palette.TextLabel.Brush;
        resources["TextControlPlaceholderForegroundFocused"] = Palette.TextLabel.Brush;
        resources["TextControlSelectionHighlightColor"] = Palette.AccentBorder.Color;

        // Scroll bars: thin, no arrows
        resources["ScrollBarThumbFill"] = Palette.ControlBorder.Brush;
        resources["ScrollBarThumbFillPointerOver"] = Palette.ControlBorderHover.Brush;
        resources["ScrollBarThumbFillPressed"] = Palette.TextDisabled.Brush;
        resources["ScrollBarTrackFill"] = Brushes.Transparent;
        resources["ScrollBarTrackFillPointerOver"] = Palette.Panel.Brush;
        resources["ScrollBarTrackStroke"] = Brushes.Transparent;
        resources["ScrollBarTrackStrokePointerOver"] = Brushes.Transparent;

        // Square everything.
        resources["ControlCornerRadius"] = new CornerRadius(0);
        resources["OverlayCornerRadius"] = new CornerRadius(0);
        resources["ContentControlThemeFontFamily"] = Fonts.Ui;
        resources["SystemAccentColor"] = Palette.Accent.Color;
    }

    private static Style WindowStyle() => new(x => x.OfType<Window>())
    {
        Setters =
        {
            new Setter(TemplatedControl.BackgroundProperty, Palette.Window.Brush),
            new Setter(TemplatedControl.ForegroundProperty, Palette.TextBody.Brush),
            new Setter(TemplatedControl.FontFamilyProperty, Fonts.Ui),
            new Setter(TemplatedControl.FontSizeProperty, 12.0),
        },
    };

    private static Style ButtonBase() => new(x => x.OfType<Button>())
    {
        Setters =
        {
            new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
            new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 0)),
            new Setter(TemplatedControl.FontSizeProperty, 12.0),
            new Setter(HeightProperty, 28.0),
            new Setter(MinWidthProperty, 0.0),
            new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
            new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
        },
    };

    /// <summary>
    /// Re-colours one button by overriding the Fluent brushes it reads, on the instance. (Style-scoped
    /// resources did not reach Fluent's template in 12.1.2 — see doc/specs/M4-SPEC.md §0.) Safe to call
    /// again when a button changes kind, e.g. a segment becoming the selected one.
    /// </summary>
    public static void SetKind(Button button, ButtonKind kind)
    {
        foreach (string key in VariantKeys)
        {
            button.Resources.Remove(key);
        }

        switch (kind)
        {
            case ButtonKind.Primary:
                Colour(button, Palette.AccentFill, Palette.AccentFillHover, Palette.AccentBorder, Palette.AccentLight, Palette.TextStrong);
                break;
            case ButtonKind.Selected:
                Colour(button, Palette.AccentFill, Palette.AccentFillHover, Palette.AccentBorder, Palette.TextStrong, Palette.TextStrong);
                break;
            case ButtonKind.Danger:
                Colour(button, Palette.DangerFill, Palette.DangerFill, Palette.DangerBorder, Palette.DangerText, Palette.DangerText);
                break;
            case ButtonKind.Icon:
                button.Classes.Add(Icon);
                button.Resources["ButtonBackground"] = Brushes.Transparent;
                button.Resources["ButtonBackgroundPointerOver"] = Palette.ControlFill.Brush;
                button.Resources["ButtonBackgroundPressed"] = Palette.ControlFillHover.Brush;
                button.Resources["ButtonForeground"] = Palette.TextLabel.Brush;
                button.Resources["ButtonForegroundPointerOver"] = Palette.TextStrong.Brush;
                button.Resources["ButtonBorderBrush"] = Brushes.Transparent;
                button.Resources["ButtonBorderBrushPointerOver"] = Brushes.Transparent;
                button.Resources["ButtonBorderBrushPressed"] = Brushes.Transparent;
                break;
        }
    }

    private static readonly string[] VariantKeys =
    [
        "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
        "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
        "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
    ];

    private static void Colour(Button button, Swatch fill, Swatch fillHover, Swatch border, Swatch text, Swatch textHover)
    {
        button.Resources["ButtonBackground"] = fill.Brush;
        button.Resources["ButtonBackgroundPointerOver"] = fillHover.Brush;
        button.Resources["ButtonBackgroundPressed"] = fillHover.Brush;
        button.Resources["ButtonForeground"] = text.Brush;
        button.Resources["ButtonForegroundPointerOver"] = textHover.Brush;
        button.Resources["ButtonForegroundPressed"] = textHover.Brush;
        button.Resources["ButtonBorderBrush"] = border.Brush;
        button.Resources["ButtonBorderBrushPointerOver"] = border.Brush;
        button.Resources["ButtonBorderBrushPressed"] = border.Brush;
    }

    /// <summary>Sizing for title-bar and panel-header buttons. Colour comes from <see cref="SetKind"/>.</summary>
    private static Style IconButton() => new(x => x.OfType<Button>().Class(Icon))
    {
        Setters =
        {
            new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
            new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            new Setter(WidthProperty, 28.0),
            new Setter(HeightProperty, 28.0),
        },
    };

    private static Style TextBoxStyle() => new(x => x.OfType<TextBox>())
    {
        Setters =
        {
            new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
            new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 0)),
            new Setter(TemplatedControl.FontSizeProperty, 12.0),
            new Setter(MinHeightProperty, 28.0),
            new Setter(HeightProperty, 28.0),
            new Setter(TextBox.CaretBrushProperty, Palette.Accent.Brush),
            new Setter(TextBox.SelectionBrushProperty, Palette.AccentBorder.Brush),
            new Setter(TextBox.SelectionForegroundBrushProperty, Palette.TextStrong.Brush),
            new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
        },
    };

    /// <summary>Fluent's default focus ring is a thick white-and-black double border; EVE's is a
    /// single thin accent line, and it must stay visible for keyboard-only use.</summary>
    private static Style FocusRing() => new(x => x.Is<Control>())
    {
        Setters =
        {
            new Setter(
                Control.FocusAdornerProperty,
                new FuncTemplate<Control>(() => new Border
                {
                    BorderBrush = Palette.Accent.Brush,
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false,
                })),
        },
    };

    private static readonly StyledProperty<double> HeightProperty = Layoutable.HeightProperty;
    private static readonly StyledProperty<double> WidthProperty = Layoutable.WidthProperty;
    private static readonly StyledProperty<double> MinHeightProperty = Layoutable.MinHeightProperty;
    private static readonly StyledProperty<double> MinWidthProperty = Layoutable.MinWidthProperty;
    private static readonly StyledProperty<Thickness> PaddingProperty = Decorator.PaddingProperty;
}
