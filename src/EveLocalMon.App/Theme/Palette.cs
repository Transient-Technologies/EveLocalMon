using Avalonia.Media;
using Avalonia.Media.Immutable;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Status;

namespace EveLocalMon.App.Theme;

/// <summary>A palette entry usable as a colour or, via the implicit conversion, directly as a brush.</summary>
public readonly record struct Swatch(Color Color, ImmutableSolidColorBrush Brush)
{
    public static implicit operator Color(Swatch swatch) => swatch.Color;

    public static implicit operator ImmutableSolidColorBrush(Swatch swatch) => swatch.Brush;

    /// <summary>The same colour at a fraction of its opacity, e.g. a row tint.</summary>
    public ImmutableSolidColorBrush WithOpacity(double opacity) => new(Color, opacity);
}

/// <summary>
/// Every colour in the app (doc/specs/M4-SPEC.md §2.1). They are sampled from the EVE client and
/// its standing icons, not invented; no view may hard-code a colour of its own.
/// </summary>
public static class Palette
{
    private static Swatch Make(string hex)
    {
        Color color = Color.Parse(hex);
        return new Swatch(color, new ImmutableSolidColorBrush(color));
    }

    // Surfaces
    public static readonly Swatch Window = Make("#121616");
    public static readonly Swatch Panel = Make("#181c1c");
    public static readonly Swatch TitleBar = Make("#101414");
    public static readonly Swatch TabStrip = Make("#141818");
    public static readonly Swatch Field = Make("#0c1010");
    public static readonly Swatch Hairline = Make("#2a3030");
    public static readonly Swatch Divider = Make("#222828");

    // Controls
    public static readonly Swatch ControlBorder = Make("#3c4444");
    public static readonly Swatch ControlBorderHover = Make("#586060");
    public static readonly Swatch ControlFill = Make("#1e2424");
    public static readonly Swatch ControlFillHover = Make("#283030");
    public static readonly Swatch RowSelect = Make("#203038");

    // Text
    public static readonly Swatch TextStrong = Make("#e8e8e8");
    public static readonly Swatch TextBody = Make("#c0c0c0");
    public static readonly Swatch TextData = Make("#a4a4a4");
    public static readonly Swatch TextLabel = Make("#888888");
    public static readonly Swatch TextDisabled = Make("#5c6060");

    // Meaning
    public static readonly Swatch Accent = Make("#14b0d0");
    public static readonly Swatch AccentLight = Make("#5cc8e0");
    public static readonly Swatch AccentFill = Make("#10303a");
    public static readonly Swatch AccentFillHover = Make("#16404e");
    public static readonly Swatch AccentBorder = Make("#1490ac");
    public static readonly Swatch Hostile = Make("#fc4444");
    public static readonly Swatch Warning = Make("#fce400");
    public static readonly Swatch Ok = Make("#8cc068");
    public static readonly Swatch Idle = Make("#3c4444");

    public static readonly Swatch DangerFill = Make("#3a1414");
    public static readonly Swatch DangerBorder = Make("#a84c4c");
    public static readonly Swatch DangerText = Make("#ffd0d0");

    /// <summary>Dimming layer behind the region picker.</summary>
    public static readonly ImmutableSolidColorBrush Scrim = new(Colors.Black, 0.55);

    /// <summary>Fallback outline for a row whose template has no colour of its own.</summary>
    public static readonly Swatch NoIcon = TextDisabled;

    /// <summary>The colour of a monitor state: the 2px line under the title bar, the dot, the state word.</summary>
    public static Swatch For(MonitorState state) => state switch
    {
        MonitorState.Clear => Accent,
        MonitorState.Hostile => Hostile,
        MonitorState.Unreadable => Warning,
        _ => Idle,
    };

    /// <summary>The state word's colour. Idle states use label grey, not the (very dark) line colour.</summary>
    public static Swatch TextFor(MonitorState state) => state switch
    {
        MonitorState.Clear => Accent,
        MonitorState.Hostile => Hostile,
        MonitorState.Unreadable => Warning,
        _ => TextLabel,
    };

    /// <summary>A template's EVE colour as an Avalonia colour.</summary>
    public static Color FromBgr(Bgr colour) => Color.FromRgb(colour.R, colour.G, colour.B);
}
