namespace EveLocalMon.Core.Reading;

/// <summary>Sanity limits on what the user dragged, shared by the picker and the post-pick warning.</summary>
public static class RegionHeuristics
{
    /// <summary>A Local member list is about 150 px wide. Much wider and the region has caught the
    /// chat log, whose coloured text can masquerade as an icon column (doc/specs/M2-SPEC.md §4a).</summary>
    public const int MaxPlausibleListWidth = 400;

    public static bool IsPlausibleListWidth(int width) => width <= MaxPlausibleListWidth;
}
