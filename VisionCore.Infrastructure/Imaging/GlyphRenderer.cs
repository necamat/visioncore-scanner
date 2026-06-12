namespace VisionCore.Infrastructure.Imaging;

using System.Runtime.InteropServices;
using SkiaSharp;

/// <summary>
/// Renders digit glyphs to grayscale images for building match templates, using
/// SkiaSharp. Provides a small set of typefaces (sans-serif and monospace, in
/// regular and bold) so the templates cover the common digit shapes printed on
/// forms — e.g. the serifed "1" of monospace fonts as well as the plain sans "1".
/// Typefaces are resolved from the platform's font manager, so template
/// generation works on Windows, Linux and macOS.
/// </summary>
public static class GlyphRenderer
{
    /// <summary>The typeface variants used to build the digit template set.</summary>
    public static IReadOnlyList<SKTypeface> TemplateTypefaces { get; } = BuildTypefaces();

    /// <summary>Renders a single digit centered on a white image of the given size.</summary>
    public static GrayImage RenderDigit(int digit, int width, int height, float textSize, SKTypeface typeface)
    {
        var text = digit.ToString();

        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont(typeface, textSize);

        var textWidth = font.MeasureText(text, out var bounds, paint);
        var x = (width - textWidth) / 2f;
        var y = (height / 2f) - bounds.MidY;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        canvas.Flush();

        // Convert the rendered RGBA snapshot to Gray8 in one bulk draw instead
        // of reading pixel colors one at a time. Black-on-white glyph pixels are
        // neutral (R = G = B), so the conversion is exact.
        using var snapshot = surface.Snapshot();
        using var gray = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using (var grayCanvas = new SKCanvas(gray))
        {
            grayCanvas.DrawImage(snapshot, 0, 0);
        }

        var pixels = new byte[width * height];
        Marshal.Copy(gray.GetPixels(), pixels, 0, pixels.Length);
        return GrayImage.FromGray8(width, height, pixels);
    }

    private static IReadOnlyList<SKTypeface> BuildTypefaces()
    {
        var sans = new[] { "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica" };

        var typefaces = new List<SKTypeface>
        {
            Resolve(sans, bold: false),
            Resolve(sans, bold: true)
        };

        // De-duplicate in case the platform resolved the same fallback twice.
        return typefaces
            .GroupBy(t => t.FamilyName)
            .Select(g => g.First())
            .ToList();
    }

    private static SKTypeface Resolve(string[] families, bool bold)
    {
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        foreach (var name in families)
        {
            var match = SKFontManager.Default.MatchFamily(name, style);
            if (match is not null)
            {
                return match;
            }
        }

        return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
    }
}
