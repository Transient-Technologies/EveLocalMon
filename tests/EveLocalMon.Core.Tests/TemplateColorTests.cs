using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Tests.Support;

namespace EveLocalMon.Core.Tests;

public class TemplateColorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EveLocalMonTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("green-star", 0x3C, 0xA0, 0x3C)]
    [InlineData("purple-person", 0x9A, 0x40, 0xD8)]
    [InlineData("blue-star", 0x30, 0x6C, 0xBC)]
    [InlineData("blue-plus", 0x30, 0x6C, 0xBC)]
    [InlineData("red-minus", 0xFC, 0x44, 0x44)]
    [InlineData("orange-minus", 0xE0, 0x5A, 0x10)]
    [InlineData("gray-equals", 0x88, 0x88, 0x88)]
    public void ShippedTemplatesCarryTheColourFromTheSpec(string name, int r, int g, int b)
    {
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);

        IconTemplate template = templates.Icons.Single(t => t.Name == name);

        Assert.Equal(new Bgr((byte)b, (byte)g, (byte)r), template.Color);
    }

    [Fact]
    public void EveryShippedTemplateHasAColour()
    {
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);

        Assert.All(templates.Icons, t => Assert.NotNull(t.Color));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-colour")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#gg0000")]
    public void MissingOrMalformedColoursLoadAsNullAndDoNotThrow(string? color)
    {
        CopyTemplatesWithColor(color);

        TemplateSet templates = TemplateStore.Load(_directory);

        Assert.Equal(7, templates.Icons.Count);
        Assert.All(templates.Icons, t => Assert.Null(t.Color));
    }

    [Fact]
    public void ColourParsingAcceptsAMissingHash()
    {
        Assert.Equal(new Bgr(0x30, 0x20, 0x10), TemplateStore.ParseColor("102030"));
        Assert.Equal(new Bgr(0x30, 0x20, 0x10), TemplateStore.ParseColor(" #102030 "));
    }

    [Theory]
    [InlineData(TemplateCategory.Friendly)]
    [InlineData(TemplateCategory.Enemy)]
    [InlineData(TemplateCategory.Neutral)]
    public void ATemplateWithoutAColourFallsBackToItsCategoryDefault(TemplateCategory category)
    {
        var template = new IconTemplate("t", category, new Frame(1, 1, 4, new byte[4]), []);

        Assert.Null(template.Color);
        Assert.Equal(IconTemplate.DefaultColor(category), template.DisplayColor);
    }

    private void CopyTemplatesWithColor(string? color)
    {
        Directory.CreateDirectory(_directory);
        foreach (string file in Directory.EnumerateFiles(FixtureLoader.TemplatesDir, "*.png"))
        {
            File.Copy(file, Path.Combine(_directory, Path.GetFileName(file)));
        }

        string json = File.ReadAllText(Path.Combine(FixtureLoader.TemplatesDir, "templates.json"));
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"color\"\\s*:\\s*\"[^\"]*\"", string.Empty);
        if (color is not null)
        {
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"category\": \"(\\w+)\"", $"\"category\": \"$1\", \"color\": \"{color}\"");
        }

        File.WriteAllText(Path.Combine(_directory, "templates.json"), json);
    }
}
