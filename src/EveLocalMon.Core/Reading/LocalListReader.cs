using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Reading;

/// <summary>
/// Classifies each visible row of the EVE Local member list from a captured frame.
/// Pure function of its inputs — no OS or UI dependencies — so it is fully unit testable
/// against saved screenshots. See doc/M1-SPEC.md §2.3 for the algorithm this implements.
/// </summary>
public static class LocalListReader
{
    public static Snapshot Read(Frame frame, Calibration cal, TemplateSet templates, ReaderOptions? options = null)
    {
        ReaderOptions opt = options ?? new ReaderOptions();
        PixelRect listRect = cal.ListRect;

        int[] content = BuildContentProfile(frame, cal, opt);
        int phase = FindRowPhase(content, cal.RowPitch, cal.IconSize);
        List<(int Top, bool IsFull)> slots = FindSlots(listRect, phase, cal.RowPitch, cal.IconSize);

        bool[] defaultMask = IconMasking.Default(cal.IconSize);
        int defaultMaskedCount = defaultMask.Count(m => m);

        var rows = new List<RowResult>(slots.Count);
        int occupiedFullRows = 0;
        bool hasPartialWithContent = false;
        int enemyCount = 0, neutralCount = 0, unknownCount = 0, noIconCount = 0;

        foreach ((int top, bool isFull) in slots)
        {
            if (!isFull)
            {
                int clippedStart = Math.Max(top, listRect.Y);
                int clippedEnd = Math.Min(top + cal.IconSize, listRect.Bottom);
                int partialContent = 0;
                for (int y = clippedStart; y < clippedEnd; y++)
                {
                    partialContent += content[y - listRect.Y];
                }

                if (partialContent >= opt.PartialRowMinPixels)
                {
                    hasPartialWithContent = true;
                }

                rows.Add(new RowResult(top, true, RowCategory.Empty, null, 0, 0, partialContent, 0));
                continue;
            }

            RowResult row = ClassifyFullRow(frame, cal, opt, templates, top, defaultMask, defaultMaskedCount);
            rows.Add(row);

            if (row.Category != RowCategory.Empty)
            {
                occupiedFullRows++;
            }

            switch (row.Category)
            {
                case RowCategory.Enemy: enemyCount++; break;
                case RowCategory.Neutral: neutralCount++; break;
                case RowCategory.Unknown: unknownCount++; break;
                case RowCategory.NoIcon: noIconCount++; break;
            }
        }

        int? headerCount = cal.HeaderCountRect is { } rect
            ? HeaderCountReader.Read(frame, rect, templates.Digits, opt)
            : null;

        Readability readability = DetermineReadability(occupiedFullRows, hasPartialWithContent, headerCount);
        int extraNoIcon = Math.Max(0, noIconCount - opt.OwnPilotNoIconAllowance);
        var threats = new ThreatBreakdown(enemyCount, neutralCount, unknownCount, extraNoIcon);

        return new Snapshot(rows, occupiedFullRows, hasPartialWithContent, headerCount, readability, threats);
    }

    /// <summary>content[dy] = number of text-or-icon-like pixels on frame row (listRect.Y + dy),
    /// across the combined icon+name column span. Used both to find the row phase and to score
    /// partial rows for overflow detection.</summary>
    private static int[] BuildContentProfile(Frame frame, Calibration cal, ReaderOptions opt)
    {
        PixelRect listRect = cal.ListRect;
        var content = new int[listRect.Height];
        for (int dy = 0; dy < listRect.Height; dy++)
        {
            int y = listRect.Y + dy;
            int count = 0;
            for (int x = cal.IconX; x < cal.NameRight; x++)
            {
                Bgr px = frame.GetPixel(x, y);
                bool isContent = px.Luma >= opt.TextLumaMin
                    || px.Luma >= opt.IconPixelLumaMin
                    || px.Chroma >= opt.IconPixelChromaMin;
                if (isContent) count++;
            }

            content[dy] = count;
        }

        return content;
    }

    private static int FindRowPhase(int[] content, int rowPitch, int iconSize)
    {
        int bestPhase = 0;
        long bestScore = long.MinValue;
        for (int p = 0; p < rowPitch; p++)
        {
            long score = 0;
            for (int dy = 0; dy < content.Length; dy++)
            {
                int mod = ((dy - p) % rowPitch + rowPitch) % rowPitch;
                score += mod < iconSize ? content[dy] : -content[dy];
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestPhase = p;
            }
        }

        return bestPhase;
    }

    private static List<(int Top, bool IsFull)> FindSlots(PixelRect listRect, int phase, int rowPitch, int iconSize)
    {
        var slots = new List<(int Top, bool IsFull)>();
        int approxRows = listRect.Height / rowPitch + 2;
        for (int k = -1; k <= approxRows; k++)
        {
            int top = listRect.Y + phase + k * rowPitch;
            int bottom = top + iconSize;
            if (bottom <= listRect.Y || top >= listRect.Bottom)
            {
                continue;
            }

            bool isFull = top >= listRect.Y && bottom <= listRect.Bottom;
            slots.Add((top, isFull));
        }

        return slots;
    }

    private static RowResult ClassifyFullRow(
        Frame frame, Calibration cal, ReaderOptions opt, TemplateSet templates,
        int top, bool[] defaultMask, int defaultMaskedCount)
    {
        int textPixels = 0;
        for (int y = top; y < top + cal.IconSize; y++)
        {
            for (int x = cal.NameX; x < cal.NameRight; x++)
            {
                if (frame.GetPixel(x, y).Luma >= opt.TextLumaMin) textPixels++;
            }
        }

        bool occupied = textPixels >= opt.TextMinPixels;

        int iconPixels = 0;
        for (int dy = 0; dy < cal.IconSize; dy++)
        {
            for (int dx = 0; dx < cal.IconSize; dx++)
            {
                if (!defaultMask[dy * cal.IconSize + dx]) continue;
                Bgr px = frame.GetPixel(cal.IconX + dx, top + dy);
                if (px.Luma >= opt.IconPixelLumaMin || px.Chroma >= opt.IconPixelChromaMin) iconPixels++;
            }
        }

        bool iconPresent = iconPixels >= opt.IconPresentFraction * defaultMaskedCount;

        if (!iconPresent)
        {
            RowCategory bare = occupied ? RowCategory.NoIcon : RowCategory.Empty;
            return new RowResult(top, false, bare, null, 0, 0, textPixels, iconPixels);
        }

        (string? name, double best, double secondBest, TemplateCategory? category) =
            MatchTemplate(frame, cal.IconX, top, templates.Icons, opt.MatchSearchRadius, opt.MatchMaxBackgroundShift);

        RowCategory resolved = category.HasValue && best <= opt.MatchMaxMeanDiff
            ? category.Value switch
            {
                TemplateCategory.Friendly => RowCategory.Friendly,
                TemplateCategory.Enemy => RowCategory.Enemy,
                TemplateCategory.Neutral => RowCategory.Neutral,
                _ => RowCategory.Unknown,
            }
            : RowCategory.Unknown;

        return new RowResult(top, false, resolved, name, best, secondBest, textPixels, iconPixels);
    }

    private static (string? Name, double Best, double SecondBest, TemplateCategory? Category) MatchTemplate(
        Frame frame, int iconX, int slotTop, IReadOnlyList<IconTemplate> templates, int radius, int maxShift)
    {
        string? bestName = null;
        TemplateCategory? bestCategory = null;
        double best = double.MaxValue;
        double secondBest = double.MaxValue;

        foreach (IconTemplate template in templates)
        {
            int size = template.Image.Width;
            double templateBest = double.MaxValue;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    double? score = ScoreOffset(frame, iconX + dx, slotTop + dy, template, size, maxShift, templateBest);
                    if (score is { } s && s < templateBest) templateBest = s;
                }
            }

            if (templateBest < best)
            {
                secondBest = best;
                best = templateBest;
                bestName = template.Name;
                bestCategory = template.Category;
            }
            else if (templateBest < secondBest)
            {
                secondBest = templateBest;
            }
        }

        return (bestName, best, secondBest, bestCategory);
    }

    /// <summary>
    /// Mean per-channel abs diff after removing a per-channel offset. The icons' coloured fill is
    /// translucent (~40% of the window background shows through), so a brighter background — a
    /// nebula, or EVE's own row hover/selection highlight — shifts the whole fill uniformly and
    /// pushed a real fleet icon past the threshold into Unknown. The offset is the mean signed
    /// diff, clamped to ±<paramref name="maxShift"/> so a genuinely different colour (another
    /// standing) can't be explained away as background.
    /// </summary>
    /// <param name="cutoff">Scores at or above this are of no interest to the caller; once a lower
    /// bound proves the score can't beat it, that bound is returned instead of the exact score.</param>
    internal static double? ScoreOffset(
        Frame frame, int originX, int originY, IconTemplate template, int size, int maxShift, double cutoff = double.MaxValue)
    {
        if (originX < 0 || originY < 0 || originX + size > frame.Width || originY + size > frame.Height)
        {
            return null;
        }

        // Hot path (rows × templates × search offsets): index the BGRA bytes directly.
        ReadOnlySpan<byte> fp = frame.Pixels;
        ReadOnlySpan<byte> tp = template.Image.Pixels;
        bool[] mask = template.Mask;
        int sumB = 0, sumG = 0, sumR = 0, sumAbs = 0;
        int count = 0;
        for (int y = 0; y < size; y++)
        {
            int fRow = (originY + y) * frame.Stride + originX * 4;
            int tRow = y * template.Image.Stride;
            for (int x = 0; x < size; x++)
            {
                if (!mask[y * size + x]) continue;
                int f = fRow + x * 4, t = tRow + x * 4;
                int db = fp[f] - tp[t], dg = fp[f + 1] - tp[t + 1], dr = fp[f + 2] - tp[t + 2];
                sumB += db;
                sumG += dg;
                sumR += dr;
                sumAbs += Math.Abs(db) + Math.Abs(dg) + Math.Abs(dr);
                count++;
            }
        }

        if (count == 0) return null;

        int shiftB = (int)Math.Clamp(Math.Round((double)sumB / count), -maxShift, maxShift);
        int shiftG = (int)Math.Clamp(Math.Round((double)sumG / count), -maxShift, maxShift);
        int shiftR = (int)Math.Clamp(Math.Round((double)sumR / count), -maxShift, maxShift);

        // |d - s| >= |d| - |s|, so removing the shift can lower the total by at most count × |s|.
        double lowerBound = (sumAbs - count * (Math.Abs(shiftB) + Math.Abs(shiftG) + Math.Abs(shiftR))) / (count * 3.0);
        if (lowerBound >= cutoff) return lowerBound;

        long sum = 0;
        for (int y = 0; y < size; y++)
        {
            int fRow = (originY + y) * frame.Stride + originX * 4;
            int tRow = y * template.Image.Stride;
            for (int x = 0; x < size; x++)
            {
                if (!mask[y * size + x]) continue;
                int f = fRow + x * 4, t = tRow + x * 4;
                sum += Math.Abs(fp[f] - tp[t] - shiftB) + Math.Abs(fp[f + 1] - tp[t + 1] - shiftG) + Math.Abs(fp[f + 2] - tp[t + 2] - shiftR);
            }
        }

        return sum / (count * 3.0);
    }

    private static Readability DetermineReadability(int occupiedFullRows, bool hasPartialWithContent, int? headerCount)
    {
        if (occupiedFullRows == 0)
        {
            return Readability.NotVisible;
        }

        if (hasPartialWithContent || (headerCount.HasValue && headerCount.Value > occupiedFullRows))
        {
            return Readability.Overflow;
        }

        return Readability.Full;
    }
}
