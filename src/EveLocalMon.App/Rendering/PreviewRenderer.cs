using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.App.Rendering;

/// <summary>
/// Draws the captured region plus overlays into a <see cref="WriteableBitmap"/>. Overlays are
/// composited straight into the pixels rather than laid out as controls, so what you see is exactly
/// the pixels the reader saw, with no layout or scaling in between (doc/specs/M2-SPEC.md §7). Colours
/// come from <see cref="Palette"/> and from each template's own EVE colour (doc/specs/M4-SPEC.md §2.1).
/// </summary>
public static class PreviewRenderer
{
    private readonly record struct Pixel(byte B, byte G, byte R);

    public static WriteableBitmap CreateBitmap(int width, int height) =>
        new(new PixelSize(Math.Max(1, width), Math.Max(1, height)), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    /// <summary>The Monitor tab: each row outlined in the colour of the icon it matched.</summary>
    public static void RenderRows(WriteableBitmap target, Frame frame, Snapshot? snapshot, Calibration? calibration, TemplateSet? templates)
    {
        byte[] buffer = (byte[])frame.Pixels.Clone();

        if (snapshot is not null && calibration is not null)
        {
            var colours = (templates?.Icons ?? []).ToDictionary(t => t.Name, t => ToPixel(t.DisplayColor));

            int left = Math.Max(0, calibration.IconX - 1);
            int right = Math.Min(frame.Width, calibration.NameRight);
            foreach (RowResult row in snapshot.Rows)
            {
                Pixel? colour = row.Category switch
                {
                    RowCategory.Friendly or RowCategory.Enemy or RowCategory.Neutral
                        when row.TemplateName is { } name && colours.TryGetValue(name, out Pixel matched) => matched,
                    RowCategory.Friendly => ToPixel(IconTemplate.DefaultColor(TemplateCategory.Friendly)),
                    RowCategory.Enemy => ToPixel(IconTemplate.DefaultColor(TemplateCategory.Enemy)),
                    RowCategory.Neutral => ToPixel(IconTemplate.DefaultColor(TemplateCategory.Neutral)),
                    RowCategory.Unknown => ToPixel(Palette.Warning),
                    RowCategory.NoIcon => ToPixel(Palette.NoIcon),
                    _ => row.IsPartial ? ToPixel(Palette.TextDisabled) : null,
                };

                if (colour is null) continue;

                DrawRect(buffer, frame, left, row.SlotTop, right - left, calibration.IconSize, colour.Value, row.IsPartial);
            }
        }

        Blit(target, frame, buffer);
    }

    /// <summary>The Calibration tab: only the guides, so a wrong number is visible as a wrong line.</summary>
    public static void RenderGuides(WriteableBitmap target, Frame frame, Calibration? calibration)
    {
        byte[] buffer = (byte[])frame.Pixels.Clone();

        if (calibration is not null)
        {
            var list = calibration.ListRect;

            // Slot boundaries first, so the stronger guides draw over them.
            Pixel dotted = ToPixel(Palette.TextDisabled);
            for (int slot = 1; slot <= 2; slot++)
            {
                DrawHorizontal(buffer, frame, list.X, list.Y + (slot * calibration.RowPitch), list.Width, dotted, step: 2);
            }

            DrawRect(buffer, frame, list.X, list.Y, list.Width, list.Height, ToPixel(Palette.TextData), dashed: true);

            Pixel accent = ToPixel(Palette.Accent);
            DrawVertical(buffer, frame, calibration.IconX, list.Y, list.Height, accent);
            DrawVertical(buffer, frame, calibration.IconX + calibration.IconSize - 1, list.Y, list.Height, accent);
            DrawVertical(buffer, frame, calibration.NameX, list.Y, list.Height, ToPixel(Palette.TextStrong));

            if (calibration.HeaderCountRect is { } header)
            {
                DrawRect(buffer, frame, header.X, header.Y, header.Width, header.Height, ToPixel(Palette.Warning), dashed: false);
            }
        }

        Blit(target, frame, buffer);
    }

    private static Pixel ToPixel(Swatch swatch) => new(swatch.Color.B, swatch.Color.G, swatch.Color.R);

    private static Pixel ToPixel(Bgr colour) => new(colour.B, colour.G, colour.R);

    private static void Blit(WriteableBitmap target, Frame frame, byte[] buffer)
    {
        using ILockedFramebuffer fb = target.Lock();
        int rowBytes = Math.Min(fb.RowBytes, frame.Width * 4);
        int rows = Math.Min(frame.Height, fb.Size.Height);
        for (int y = 0; y < rows; y++)
        {
            Marshal.Copy(buffer, y * frame.Stride, fb.Address + (y * fb.RowBytes), rowBytes);
        }
    }

    private static void DrawRect(byte[] buffer, Frame frame, int x, int y, int width, int height, Pixel colour, bool dashed)
    {
        int right = x + width - 1;
        int bottom = y + height - 1;
        for (int px = x; px <= right; px++)
        {
            if (dashed && (px & 1) == 1) continue;
            SetPixel(buffer, frame, px, y, colour);
            SetPixel(buffer, frame, px, bottom, colour);
        }

        for (int py = y; py <= bottom; py++)
        {
            if (dashed && (py & 1) == 1) continue;
            SetPixel(buffer, frame, x, py, colour);
            SetPixel(buffer, frame, right, py, colour);
        }
    }

    private static void DrawVertical(byte[] buffer, Frame frame, int x, int y, int height, Pixel colour)
    {
        for (int py = y; py < y + height; py++)
        {
            SetPixel(buffer, frame, x, py, colour);
        }
    }

    private static void DrawHorizontal(byte[] buffer, Frame frame, int x, int y, int width, Pixel colour, int step)
    {
        for (int px = x; px < x + width; px += step)
        {
            SetPixel(buffer, frame, px, y, colour);
        }
    }

    private static void SetPixel(byte[] buffer, Frame frame, int x, int y, Pixel colour)
    {
        if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) return;

        int offset = (y * frame.Stride) + (x * 4);
        buffer[offset] = colour.B;
        buffer[offset + 1] = colour.G;
        buffer[offset + 2] = colour.R;
    }
}
