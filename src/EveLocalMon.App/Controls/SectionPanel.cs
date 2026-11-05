using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using EveLocalMon.App.Theme;

namespace EveLocalMon.App.Controls;

/// <summary>
/// A titled panel in EVE's style: 28 px header with a sentence-case title and an optional
/// <c>[count]</c>, hairline, then the body (doc/specs/M4-SPEC.md §2.3).
/// </summary>
public sealed class SectionPanel : Border
{
    private readonly TextBlock _count = Ui.Label("");

    public SectionPanel(string title, Control body, string? count = null, Control? headerRight = null)
    {
        Background = Palette.Panel.Brush;
        BorderBrush = Palette.Hairline.Brush;
        BorderThickness = new Thickness(1);
        ClipToBounds = true;

        _count.IsVisible = false;
        _count.Margin = new Thickness(6, 0, 0, 0);
        if (count is not null)
        {
            SetCount(count);
        }

        var header = new Grid
        {
            Height = 28,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(10, 0),
                    Children = { Ui.Text(title, 13, Palette.TextBody.Brush, Avalonia.Media.FontWeight.Medium), _count },
                },
            },
        };

        if (headerRight is not null)
        {
            headerRight.Margin = new Thickness(0, 0, 6, 0);
            headerRight.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(headerRight, 1);
            header.Children.Add(headerRight);
        }

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        layout.Children.Add(header);

        Border divider = Ui.HLine(Palette.Divider.Brush);
        Grid.SetRow(divider, 1);
        layout.Children.Add(divider);

        Grid.SetRow(body, 2);
        layout.Children.Add(body);

        Child = layout;
    }

    /// <summary>Shown as "[18]" beside the title; null hides it.</summary>
    public void SetCount(string? count)
    {
        _count.IsVisible = !string.IsNullOrEmpty(count);
        _count.Text = count is null ? string.Empty : $"[{count}]";
    }
}
