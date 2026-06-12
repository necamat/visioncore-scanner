using System.Drawing;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using VisionCore.Application.Configuration;

namespace VisionCore.Tests.Integration.EndToEnd;

/// <summary>
/// Builds synthetic scanned-style quiz-sheet PDFs that reproduce the printed
/// form:
///
///   * four L-shaped corner markers
///   * one team-id outline box (upper-right)
///   * one score outline box (lower-center) with three inner digit cells
///
/// The sheet is rendered cross-platform (SkiaSharp) as a single full-page bitmap
/// (A4 @ 200 DPI) and embedded as the sole image of a one-page PDF via PdfPig's
/// writer — exactly what PdfRegionExtractor reads back. The crop rectangles
/// exposed by <see cref="DefaultRegions"/> land on the same boxes the form draws,
/// so recognition is deterministic.
/// </summary>
public sealed class SyntheticPdfDatasetBuilder
{
    // A4 @ 200 DPI.
    private const int PageWidth = 1654;
    private const int PageHeight = 2338;

    // Form rectangle on the page (centered horizontally, near the top — as the
    // VBA PageSetup prints A1:J46 fit-to-page, CenterHorizontally, top-aligned).
    private static readonly Rectangle Form = new(130, 90, 1394, 2160);

    // VBA marker geometry (relative to the form rectangle).
    private const float MarkerInset = 0.018f;
    private const float MarkerArm = 0.016f;

    // Box rectangles, derived from the VBA relative coordinates within Form.
    private static readonly Rectangle TeamIdBox = new(1330, 200, 110, 120);
    private static readonly Rectangle TeamIdDigit1Box = new(1338, 214, 46, 92);
    private static readonly Rectangle TeamIdDigit2Box = new(1392, 214, 46, 92);
    private static readonly Rectangle ScoreBox = new(1090, 1980, 295, 120);
    private static readonly Rectangle ScoreDigit1Box = new(1108, 1999, 78, 82);
    private static readonly Rectangle ScoreDigit2Box = new(1198, 1999, 78, 82);
    private static readonly Rectangle ScoreDigit3Box = new(1288, 1999, 78, 82);

    private static readonly SKTypeface Typeface = ResolveTypeface();

    /// <summary>
    /// PDF crop coordinates matching the rendered form. Mirror these in
    /// appsettings.json "PdfRegions" so the console host crops the same boxes.
    /// </summary>
    public static PdfRegionOptions DefaultRegions() => new()
    {
        Dpi = 200,
        TeamId = ToBounds(TeamIdBox),
        TeamIdDigit1 = ToBounds(TeamIdDigit1Box),
        TeamIdDigit2 = ToBounds(TeamIdDigit2Box),
        Score = ToBounds(ScoreBox),
        ScoreDigit1 = ToBounds(ScoreDigit1Box),
        ScoreDigit2 = ToBounds(ScoreDigit2Box),
        ScoreDigit3 = ToBounds(ScoreDigit3Box)
    };

    public void Build(string rootFolder, RoundFolderDatasetManifest manifest)
    {
        Directory.CreateDirectory(rootFolder);

        foreach (var entry in manifest.Entries)
        {
            var roundFolder = Path.Combine(rootFolder, $"R{entry.Round}");
            Directory.CreateDirectory(roundFolder);
            CreateSheetPdf(
                Path.Combine(roundFolder, entry.FileName),
                entry.ExpectedTeamId,
                entry.ExpectedScore,
                SheetDistortion.None);
        }
    }

    /// <summary>
    /// Renders a single sheet PDF. When <paramref name="score"/> is null the
    /// three score boxes are left blank, driving the recognition-failure path.
    /// An optional <see cref="SheetDistortion"/> degrades the render the way a
    /// real scanner would (speckle noise, faint ink, compression, offset print).
    /// </summary>
    public void BuildSheet(string pdfPath, int teamId, int? score, SheetDistortion? distortion = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
        CreateSheetPdf(pdfPath, teamId, score, distortion ?? SheetDistortion.None);
    }

    private static void CreateSheetPdf(string pdfPath, int teamId, int? score, SheetDistortion distortion)
    {
        var jpegBytes = RenderPageJpeg(teamId, score, distortion);

        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddJpeg(jpegBytes, new PdfRectangle(0, 0, page.PageSize.Width, page.PageSize.Height));

        File.WriteAllBytes(pdfPath, builder.Build());
    }

    private static byte[] RenderPageJpeg(int teamId, int? score, SheetDistortion distortion)
    {
        var info = new SKImageInfo(PageWidth, PageHeight);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var inkColor = new SKColor(distortion.InkIntensity, distortion.InkIntensity, distortion.InkIntensity);
        using var ink = new SKPaint { Color = inkColor, IsAntialias = true };

        DrawCornerMarkers(canvas, ink);

        DrawBox(canvas, ink, TeamIdBox);
        DrawBox(canvas, ink, ScoreBox);
        DrawBox(canvas, ink, ScoreDigit1Box);
        DrawBox(canvas, ink, ScoreDigit2Box);
        DrawBox(canvas, ink, ScoreDigit3Box);

        var teamText = teamId.ToString("00");
        DrawDigit(canvas, ink, teamText[0].ToString(), TeamIdDigit1Box, distortion);
        DrawDigit(canvas, ink, teamText[1].ToString(), TeamIdDigit2Box, distortion);

        if (score is not null)
        {
            var scoreText = score.Value.ToString("000");
            DrawDigit(canvas, ink, scoreText[0].ToString(), ScoreDigit1Box, distortion);
            DrawDigit(canvas, ink, scoreText[1].ToString(), ScoreDigit2Box, distortion);
            DrawDigit(canvas, ink, scoreText[2].ToString(), ScoreDigit3Box, distortion);
        }

        DrawSpeckleNoise(canvas, distortion);

        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, distortion.JpegQuality);
        return data.ToArray();
    }

    /// <summary>
    /// Scatters small dark speckles over the whole page, like dust on the
    /// scanner glass. Seeded, so a given distortion renders identically on
    /// every run and platform. With
    /// <see cref="SheetDistortion.KeepSpecklesOffDigits"/> the digit cells are
    /// left clean (dust beside the print, not on it).
    /// </summary>
    private static void DrawSpeckleNoise(SKCanvas canvas, SheetDistortion distortion)
    {
        if (distortion.NoiseSpeckles <= 0)
        {
            return;
        }

        var random = new Random(distortion.RandomSeed);
        using var speckle = new SKPaint { Color = new SKColor(60, 60, 60) };

        Rectangle[] digitCells =
        [
            TeamIdDigit1Box, TeamIdDigit2Box, ScoreDigit1Box, ScoreDigit2Box, ScoreDigit3Box
        ];

        for (var i = 0; i < distortion.NoiseSpeckles; i++)
        {
            var x = random.Next(PageWidth);
            var y = random.Next(PageHeight);
            var size = random.Next(1, 3);

            if (distortion.KeepSpecklesOffDigits &&
                digitCells.Any(cell => cell.IntersectsWith(new Rectangle(x - 2, y - 2, size + 4, size + 4))))
            {
                continue;
            }

            canvas.DrawRect(x, y, size, size, speckle);
        }
    }

    /// <summary>Draws the four filled L-markers using the VBA marker geometry.</summary>
    private static void DrawCornerMarkers(SKCanvas canvas, SKPaint ink)
    {
        var armW = (int)Math.Round(Form.Width * MarkerArm);
        var armH = (int)Math.Round(Form.Height * MarkerArm);
        var thickness = Math.Max(2, (int)Math.Round(Math.Min(armW, armH) * 0.26));
        var insetX = (int)Math.Round(Form.Width * MarkerInset);
        var insetY = (int)Math.Round(Form.Height * MarkerInset);

        var left = Form.Left + insetX;
        var right = Form.Right - insetX - armW;
        var top = Form.Top + insetY;
        var bottom = Form.Bottom - insetY - armH;

        DrawLMarker(canvas, ink, left, top, armW, armH, thickness, cornerTop: true, cornerLeft: true);
        DrawLMarker(canvas, ink, right, top, armW, armH, thickness, cornerTop: true, cornerLeft: false);
        DrawLMarker(canvas, ink, left, bottom, armW, armH, thickness, cornerTop: false, cornerLeft: true);
        DrawLMarker(canvas, ink, right, bottom, armW, armH, thickness, cornerTop: false, cornerLeft: false);
    }

    private static void DrawLMarker(
        SKCanvas canvas, SKPaint ink, int x, int y, int armW, int armH, int thickness, bool cornerTop, bool cornerLeft)
    {
        var horizontalY = cornerTop ? y : y + armH - thickness;
        var verticalX = cornerLeft ? x : x + armW - thickness;
        canvas.DrawRect(x, horizontalY, armW, thickness, ink);
        canvas.DrawRect(verticalX, y, thickness, armH, ink);
    }

    /// <summary>Draws a hollow rectangle outline (3px) by filling its four edges.</summary>
    private static void DrawBox(SKCanvas canvas, SKPaint ink, Rectangle box)
    {
        const int w = 3;
        canvas.DrawRect(box.Left, box.Top, box.Width, w, ink);
        canvas.DrawRect(box.Left, box.Bottom - w, box.Width, w, ink);
        canvas.DrawRect(box.Left, box.Top, w, box.Height, ink);
        canvas.DrawRect(box.Right - w, box.Top, w, box.Height, ink);
    }

    private static void DrawDigit(SKCanvas canvas, SKPaint ink, string digit, Rectangle box, SheetDistortion distortion)
    {
        using var font = new SKFont(Typeface, box.Height * 0.58f);
        var textWidth = font.MeasureText(digit, out var bounds, ink);
        var x = box.Left + ((box.Width - textWidth) / 2f) + distortion.DigitOffsetX;
        var y = box.Top + (box.Height / 2f) - bounds.MidY + distortion.DigitOffsetY;
        canvas.DrawText(digit, x, y, SKTextAlign.Left, font, ink);
    }

    private static PdfRegionBounds ToBounds(Rectangle r) =>
        new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };

    private static SKTypeface ResolveTypeface()
    {
        var style = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        foreach (var name in new[] { "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica" })
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
