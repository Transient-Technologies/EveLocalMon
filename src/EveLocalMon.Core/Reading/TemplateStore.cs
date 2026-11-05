using System.Text.Json;
using System.Text.Json.Serialization;
using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Reading;

/// <summary>
/// Loads a template set from disk: <c>templates.json</c> + icon PNGs directly in
/// <paramref name="directory"/>, and digit glyph PNGs (named "0.png", "1.png", ...) under a
/// <c>digits/</c> subfolder. See doc/M1-SPEC.md §4.1.
/// </summary>
public static class TemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TemplateSet Load(string directory, int digitLumaMin = 120)
    {
        string manifestPath = Path.Combine(directory, "templates.json");
        List<TemplateEntry>? entries = JsonSerializer.Deserialize<List<TemplateEntry>>(File.ReadAllText(manifestPath), JsonOptions);
        if (entries is null)
        {
            throw new InvalidDataException($"'{manifestPath}' did not contain a template list.");
        }

        var icons = new List<IconTemplate>();
        foreach (TemplateEntry entry in entries)
        {
            Frame image = FrameIo.LoadPng(Path.Combine(directory, entry.File));
            bool[] mask = IconMasking.Default(image.Width);
            var category = Enum.Parse<TemplateCategory>(entry.Category, ignoreCase: true);
            icons.Add(new IconTemplate(entry.Name, category, image, mask, ParseColor(entry.Color)));
        }

        var digits = new Dictionary<char, bool[,]>();
        string digitsDir = Path.Combine(directory, "digits");
        if (Directory.Exists(digitsDir))
        {
            foreach (string file in Directory.EnumerateFiles(digitsDir, "*.png"))
            {
                char ch = Path.GetFileNameWithoutExtension(file)[0];
                Frame glyphFrame = FrameIo.LoadPng(file);
                digits[ch] = BinarizeAndCrop(glyphFrame, digitLumaMin);
            }
        }

        return new TemplateSet(icons, digits);
    }

    /// <summary>"#rrggbb" or "rrggbb". Anything else is null rather than an error: a colour is cosmetic
    /// and must never stop the app loading the templates that do the matching.</summary>
    internal static Bgr? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        ReadOnlySpan<char> hex = text.AsSpan().Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.AllowHexSpecifier, null, out int rgb))
        {
            return null;
        }

        return new Bgr((byte)(rgb & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)((rgb >> 16) & 0xFF));
    }

    private static bool[,] BinarizeAndCrop(Frame frame, int lumaMin)
    {
        var lit = new bool[frame.Height, frame.Width];
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                lit[y, x] = frame.GetPixel(x, y).Luma >= lumaMin;
            }
        }

        return DigitGlyphs.CropToBoundingBox(lit, 0, frame.Width, frame.Height) ?? lit;
    }

    private sealed record TemplateEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("color")] string? Color = null);
}
