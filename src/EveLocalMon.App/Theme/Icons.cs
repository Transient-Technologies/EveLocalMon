using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace EveLocalMon.App.Theme;

public enum IconKind
{
    Radar,
    Pin,
    Minimise,
    Maximise,
    Close,
    Kebab,
    Mute,
    Region,
    Warning,
    EyeOff,
    ListOverflow,
    CheckCircle,
    Check,
    Play,
    Stop,
    Refresh,
    Plus,
    Minus,
    Person,
}

/// <summary>
/// A stroke icon drawn from path data in its own square coordinate space, scaled to any size.
/// Always a vector, so it stays sharp at 125% and 150% scaling, and recolourable through
/// <see cref="Brush"/>.
/// </summary>
public sealed class Icon : Viewbox
{
    // Path data and the side of the square it is drawn in. Filled icons draw solid shapes.
    private static readonly Dictionary<IconKind, (string Data, double Box, bool Filled)> Defs = new()
    {
        [IconKind.Radar] = ("M1.5 8a6.5 6.5 0 1 0 13 0a6.5 6.5 0 1 0 -13 0M5 8a3 3 0 1 0 6 0a3 3 0 1 0 -6 0M8 8L13 3.5", 16, false),
        [IconKind.Pin] = ("M5 1.5h4M6 1.5v4L3.5 8h7L8 5.5v-4M7 8v4.5", 14, false),
        [IconKind.Minimise] = ("M2 6h8", 12, false),
        [IconKind.Maximise] = ("M2.5 2.5h7v7h-7z", 12, false),
        [IconKind.Close] = ("M2.5 2.5l7 7M9.5 2.5l-7 7", 12, false),
        [IconKind.Kebab] = ("M5 2a1 1 0 1 0 2 0a1 1 0 1 0 -2 0M5 6a1 1 0 1 0 2 0a1 1 0 1 0 -2 0M5 10a1 1 0 1 0 2 0a1 1 0 1 0 -2 0", 12, true),
        [IconKind.Mute] = ("M2 5h2.5L8 2v10L4.5 9H2zM10 5l3 4M13 5l-3 4", 14, false),
        [IconKind.Region] = ("M1 4V1h3M8 1h3v3M11 8v3H8M4 11H1V8", 12, false),
        [IconKind.Warning] = ("M14 3l12 21H2zM14 11v6M14 20v1.5", 28, false),
        [IconKind.EyeOff] = ("M2 12s3.6-6 10-6 10 6 10 6-3.6 6-10 6S2 12 2 12zM9.2 12a2.8 2.8 0 1 0 5.6 0a2.8 2.8 0 1 0 -5.6 0M4 20L20 4", 24, false),
        [IconKind.ListOverflow] = ("M4 5h11M4 10h11M4 15h11M4 20h6M19 6v12M16.5 15.5L19 18l2.5-2.5", 24, false),
        [IconKind.CheckCircle] = ("M2 11a9 9 0 1 0 18 0a9 9 0 1 0 -18 0M6.5 11.3l3 3 6-6.3", 22, false),
        [IconKind.Check] = ("M1.5 5.2l2.3 2.3L8.5 2.5", 10, false),
        [IconKind.Play] = ("M2 1l7 4-7 4z", 10, true),
        [IconKind.Stop] = ("M1 1h8v8H1z", 10, true),
        [IconKind.Refresh] = ("M10.5 6A4.5 4.5 0 1 1 9 2.6M9.5 0.8v2.4H7.1", 12, false),
        [IconKind.Plus] = ("M1 4h6M4 1v6", 8, false),
        [IconKind.Minus] = ("M1 4h6", 8, false),
        [IconKind.Person] = ("M3 3a2 2 0 1 0 4 0a2 2 0 1 0 -4 0M1 9.5c0-2.5 1.8-4 4-4s4 1.5 4 4z", 10, true),
    };

    private readonly Path _path;
    private readonly bool _filled;

    public Icon(IconKind kind, IBrush brush, double size = 14, double strokeWidth = 1.3)
    {
        (string data, double box, bool filled) = Defs[kind];
        _filled = filled;

        _path = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = filled ? 0 : strokeWidth * box / size,
            StrokeLineCap = PenLineCap.Flat,
            StrokeJoin = PenLineJoin.Miter,
        };

        Width = size;
        Height = size;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Child = new Canvas { Width = box, Height = box, Children = { _path } };
        Brush = brush;
    }

    public IBrush Brush
    {
        set
        {
            if (_filled)
            {
                _path.Fill = value;
            }
            else
            {
                _path.Stroke = value;
            }
        }
    }
}
