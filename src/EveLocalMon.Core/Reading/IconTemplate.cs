using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Reading;

public enum TemplateCategory
{
    Friendly,
    Enemy,
    Neutral,
}

/// <summary>
/// A cropped 12x12 icon used as a template. <see cref="Mask"/> says which of the
/// IconSize*IconSize pixels (row-major) participate in matching; corner pixels are excluded
/// by default because EVE's rounded-square icon corners anti-alias against varying backgrounds.
/// </summary>
/// <param name="Color">Optional colour for outlines and chips, from templates.json. It is only ever
/// shown; matching never looks at it. Use <see cref="DisplayColor"/> to get one that always exists.</param>
public sealed record IconTemplate(string Name, TemplateCategory Category, Frame Image, bool[] Mask, Bgr? Color = null)
{
    /// <summary>EVE's own standing colours, brightened to read as 1px outlines on dark panels
    /// (doc/specs/M4-SPEC.md §2.1). Used when a template has no colour of its own.</summary>
    public static Bgr DefaultColor(TemplateCategory category) => category switch
    {
        TemplateCategory.Friendly => new Bgr(0xBC, 0x6C, 0x30),
        TemplateCategory.Enemy => new Bgr(0x44, 0x44, 0xFC),
        _ => new Bgr(0x88, 0x88, 0x88),
    };

    public Bgr DisplayColor => Color ?? DefaultColor(Category);
}
