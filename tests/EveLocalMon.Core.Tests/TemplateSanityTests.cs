using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Tests.Support;

namespace EveLocalMon.Core.Tests;

/// <summary>Confirms the crop coordinates in doc/M1-SPEC.md §4.2 still land on the right icon,
/// and that the match threshold comfortably separates "same icon" from "different icon" —
/// see the measurements in doc/PLAN.md §4.2.</summary>
public class TemplateSanityTests
{
    private static readonly (string Name, string Image, int X, int Y)[] SourceSlots =
    [
        ("green-star", "sample-local-mixed-overflow.png", 303, 81),
        ("purple-person", "sample-local-mixed-overflow.png", 303, 98),
        ("blue-star", "sample-local-mixed-overflow.png", 303, 166),
        ("red-minus", "sample-local-mixed-overflow.png", 303, 217),
        ("gray-equals", "sample-local-mixed-overflow.png", 303, 472),
        ("blue-plus", "sample-local-nonscrolled-one-neut-one-friendly.png", 314, 95),
    ];

    public static IEnumerable<object[]> Cases() => SourceSlots.Select(s => new object[] { s.Name, s.Image, s.X, s.Y });

    [Theory]
    [MemberData(nameof(Cases))]
    public void TemplateMatchesItsOwnSourceSlot(string name, string image, int x, int y)
    {
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);
        IconTemplate template = templates.Icons.Single(t => t.Name == name);
        Frame frame = FrameIo.LoadPng(FixtureLoader.Path_(image));

        double diff = MeanMaskedDiff(frame, x, y, template);
        Assert.True(diff <= 2.5, $"{name} vs its own source slot: expected <=2.5, was {diff:F2}");
    }

    [Fact]
    public void GrayEqualsMatchesAcrossSamplesDespiteDifferentBackground()
    {
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);
        IconTemplate grayEquals = templates.Icons.Single(t => t.Name == "gray-equals");
        Frame frame = FrameIo.LoadPng(FixtureLoader.Path_("sample-local-nonscrolled-one-neut-one-friendly.png"));

        // Same icon, different sample/background: doc/PLAN.md §4.2 measured this at 2.15.
        double diff = MeanMaskedDiff(frame, 314, 112, grayEquals);
        Assert.True(diff <= 2.5, $"cross-sample gray-equals: expected <=2.5, was {diff:F2}");
    }

    /// <summary>
    /// The 44-pilot sample carries all four icon types at once, over a different window background
    /// from the ones the templates were cut from. Each template must not merely score well on its
    /// own icon — it must beat every other template, since that comparison is what actually decides
    /// a row's category.
    /// </summary>
    [Theory]
    [InlineData("purple-person", 340, 67)]
    [InlineData("blue-star", 340, 84)]
    [InlineData("green-star", 340, 135)]
    [InlineData("orange-minus", 340, 356)]
    [InlineData("blue-plus", 340, 390)]
    public void EachTemplateWinsItsOwnIconInTheFortyFourPilotSample(string expectedTemplate, int x, int y)
    {
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);
        Frame frame = FrameIo.LoadPng(FixtureLoader.Path_("sample-18fleet-1hostile-15friendly-9corp.png"));

        (string Name, double Score)[] ranked = templates.Icons
            .Select(t => (t.Name, Score: MeanMaskedDiff(frame, x, y, t)))
            .OrderBy(s => s.Score)
            .ToArray();

        Assert.Equal(expectedTemplate, ranked[0].Name);

        // Measured across all four samples: 0.6-5.1 within a class, >= 15.3 between classes.
        // The reader's 8.0 threshold sits in that gap, and these bounds guard both edges of it.
        Assert.True(ranked[0].Score <= 6.0, $"{expectedTemplate} scored {ranked[0].Score:F1} on its own icon");
        Assert.True(ranked[1].Score >= 12.0, $"runner-up {ranked[1].Name} was only {ranked[1].Score:F1} away");
    }

    // The reader's own scorer (with background-shift compensation), at the default options,
    // so these bounds guard exactly what classifies a row.
    private static double MeanMaskedDiff(Frame frame, int originX, int originY, IconTemplate template) =>
        LocalListReader.ScoreOffset(frame, originX, originY, template, template.Image.Width,
            new ReaderOptions().MatchMaxBackgroundShift) ?? double.MaxValue;
}
