using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Status;

namespace EveLocalMon.App.Controls;

/// <summary>
/// The small square that carries a monitor state's colour. It glows for the three live states;
/// Stopped, Setup needed and Checking are an outline only. It is never the only signal: the state
/// word always sits beside it (doc/specs/M4-SPEC.md §2.4).
/// </summary>
public sealed class StateDot : Border
{
    public StateDot(double size = 10)
    {
        Width = size;
        Height = size;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        SetState(MonitorState.Stopped);
    }

    public void SetState(MonitorState state)
    {
        Swatch colour = Palette.For(state);
        bool live = state is MonitorState.Clear or MonitorState.Hostile or MonitorState.Unreadable;

        if (live)
        {
            Background = colour.Brush;
            BorderThickness = new Thickness(0);
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, Color = Color.FromArgb(0xB0, colour.Color.R, colour.Color.G, colour.Color.B) });
        }
        else
        {
            Background = null;
            BorderBrush = Palette.TextLabel.Brush;
            BorderThickness = new Thickness(1);
            BoxShadow = default;
        }
    }
}
