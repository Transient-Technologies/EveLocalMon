using Avalonia.Media;

namespace EveLocalMon.App.Theme;

/// <summary>
/// The two embedded families (doc/specs/M4-SPEC.md §2.2). Both are OFL, bundled as app resources
/// and never fetched at runtime. A missing font falls back to the platform default rather than
/// failing, so the worst case is a plainer look, not a broken window.
/// </summary>
public static class Fonts
{
    private const string Folder = "avares://EveLocalMon/Assets/Fonts";

    // The family name is whatever the TTF's name table says: "Saira SemiCondensed", one word.
    public static readonly FontFamily Ui = new($"{Folder}#Saira SemiCondensed");

    /// <summary>Counts, coordinates, scores and times.</summary>
    public static readonly FontFamily Mono = new($"{Folder}#Share Tech Mono");
}
