namespace VisionCore.Infrastructure.Implementations.Recognition;

using System.Drawing;
using SkiaSharp;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Domain.Imaging.Recognition;
using VisionCore.Infrastructure.Imaging;

/// <summary>
/// Base class for the template-matching number recognizers. Builds per-digit
/// glyph templates and compares prepared region images against them. Image
/// handling uses the cross-platform <see cref="GrayImage"/> (SkiaSharp).
/// </summary>
public abstract class TemplateMatchingNumberRecognizer(DigitRecognitionOptions options)
{
    // Option ranges are validated at startup (ValidateDataAnnotations + ValidateOnStart).
    private readonly IReadOnlyDictionary<int, IReadOnlyList<bool[,]>> _templates = BuildTemplates(options);

    protected DigitRecognitionOptions Options => options;

    /// <summary>Materializes a cropped region's in-memory pixels into a <see cref="GrayImage"/>.</summary>
    protected static GrayImage Load(CroppedRegion region) =>
        GrayImage.FromGray8(region.Width, region.Height, region.Pixels);

    protected IReadOnlyList<RecognizedDigit> RecognizeMultipleDigits(CroppedRegion region, CancellationToken ct)
    {
        var bitmap = Load(region);
        var prepared = PrepareForRecognition(bitmap);
        var segments = SegmentDigits(prepared);
        var digits = new List<RecognizedDigit>();

        foreach (var segment in segments)
        {
            ct.ThrowIfCancellationRequested();
            var recognized = RecognizeGlyph(segment, region.Region);
            if (recognized is null)
            {
                throw new InvalidOperationException("Digit segment could not be recognized.");
            }

            digits.Add(recognized);
        }

        return digits;
    }

    protected RecognizedDigit? RecognizeSingleDigit(CroppedRegion region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var bitmap = Load(region);
        var prepared = PrepareForRecognition(bitmap);
        return RecognizeGlyph(prepared, region.Region);
    }

    protected List<GrayImage> ExtractBoxContents(GrayImage source, int expectedRuns)
    {
        var prepared = PrepareForRecognition(source);
        var bestRuns = FindBestHorizontalRuns(prepared, expectedRuns);
        if (bestRuns.Count != expectedRuns)
        {
            return [];
        }

        return bestRuns
            .Select(run =>
            {
                var insetX = Math.Max(1, (int)Math.Round(run.Width * 0.12f));
                var insetY = Math.Max(1, (int)Math.Round(prepared.Height * 0.12f));
                var left = Math.Clamp(run.Left + insetX, 0, prepared.Width - 1);
                var right = Math.Clamp(run.Right - insetX, left + 1, prepared.Width);
                var top = Math.Clamp(insetY, 0, prepared.Height - 1);
                var bottom = Math.Clamp(prepared.Height - insetY, top + 1, prepared.Height);
                var bounds = Rectangle.FromLTRB(left, top, right, bottom);
                return prepared.Crop(bounds);
            })
            .ToList();
    }

    protected RecognizedDigit? RecognizeGlyph(GrayImage source, FormRegion region)
    {
        var glyph = NormalizeGlyph(source);
        if (glyph is null)
        {
            return null;
        }

        var bestDigit = -1;
        var bestConfidence = 0f;

        foreach (var templateSet in _templates)
        {
            foreach (var template in templateSet.Value)
            {
                var confidence = CalculateConfidence(glyph, template);
                if (confidence > bestConfidence)
                {
                    bestConfidence = confidence;
                    bestDigit = templateSet.Key;
                }
            }
        }

        if (bestDigit < 0 || bestConfidence < options.TemplateMatchThreshold)
        {
            return null;
        }

        return new RecognizedDigit(region, bestDigit, bestConfidence);
    }

    protected static NumberRecognitionResult Failure(
        RecognitionFailureCode failure,
        RecognizedNumber? number,
        float confidence)
    {
        return NumberRecognitionResult.FailureResult(failure, number, confidence);
    }

    protected GrayImage PrepareForRecognition(GrayImage source)
    {
        var prepared = source.Clone();
        RemoveBorderLines(prepared);
        RemoveEdgeConnectedInk(prepared);
        return prepared;
    }

    /// <summary>Crops the image inward by the given percentage on every side (at least one pixel).</summary>
    protected static GrayImage CropInset(GrayImage source, float insetPercent)
    {
        var insetX = Math.Max(1, (int)Math.Round(source.Width * insetPercent));
        var insetY = Math.Max(1, (int)Math.Round(source.Height * insetPercent));
        var left = Math.Clamp(insetX, 0, source.Width - 2);
        var top = Math.Clamp(insetY, 0, source.Height - 2);
        var right = Math.Clamp(source.Width - insetX, left + 1, source.Width);
        var bottom = Math.Clamp(source.Height - insetY, top + 1, source.Height);
        var bounds = Rectangle.FromLTRB(left, top, right, bottom);
        return source.Crop(bounds);
    }

    private void RemoveBorderLines(GrayImage bitmap)
    {
        const float rowThreshold = 0.35f;
        const float columnThreshold = 0.35f;
        var rowsToClear = new List<int>();
        var columnsToClear = new List<int>();
        var edgeRowBand = Math.Max(2, (int)Math.Round(bitmap.Height * 0.20f));
        var edgeColumnBand = Math.Max(2, (int)Math.Round(bitmap.Width * 0.20f));

        for (var y = 0; y < bitmap.Height; y++)
        {
            var darkCount = 0;
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (IsDark(bitmap.GetIntensity(x, y)))
                {
                    darkCount++;
                }
            }

            var isNearEdge = y < edgeRowBand || y >= bitmap.Height - edgeRowBand;
            if (isNearEdge && (darkCount / (float)bitmap.Width) >= rowThreshold)
            {
                rowsToClear.Add(y);
            }
        }

        for (var x = 0; x < bitmap.Width; x++)
        {
            var darkCount = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (IsDark(bitmap.GetIntensity(x, y)))
                {
                    darkCount++;
                }
            }

            var isNearEdge = x < edgeColumnBand || x >= bitmap.Width - edgeColumnBand;
            if (isNearEdge && (darkCount / (float)bitmap.Height) >= columnThreshold)
            {
                columnsToClear.Add(x);
            }
        }

        foreach (var y in rowsToClear)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetWhite(x, y);
            }
        }

        foreach (var x in columnsToClear)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                bitmap.SetWhite(x, y);
            }
        }
    }

    private void RemoveEdgeConnectedInk(GrayImage bitmap)
    {
        var visited = new bool[bitmap.Width, bitmap.Height];
        var edgePixels = new Queue<Point>();

        for (var x = 0; x < bitmap.Width; x++)
        {
            EnqueueIfDark(bitmap, visited, edgePixels, x, 0);
            EnqueueIfDark(bitmap, visited, edgePixels, x, bitmap.Height - 1);
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            EnqueueIfDark(bitmap, visited, edgePixels, 0, y);
            EnqueueIfDark(bitmap, visited, edgePixels, bitmap.Width - 1, y);
        }

        while (edgePixels.Count > 0)
        {
            var current = edgePixels.Dequeue();
            bitmap.SetWhite(current.X, current.Y);

            EnqueueNeighbor(bitmap, visited, edgePixels, current.X - 1, current.Y);
            EnqueueNeighbor(bitmap, visited, edgePixels, current.X + 1, current.Y);
            EnqueueNeighbor(bitmap, visited, edgePixels, current.X, current.Y - 1);
            EnqueueNeighbor(bitmap, visited, edgePixels, current.X, current.Y + 1);
        }
    }

    private void EnqueueNeighbor(GrayImage bitmap, bool[,] visited, Queue<Point> queue, int x, int y)
    {
        if (x < 0 || x >= bitmap.Width || y < 0 || y >= bitmap.Height)
        {
            return;
        }

        EnqueueIfDark(bitmap, visited, queue, x, y);
    }

    private void EnqueueIfDark(GrayImage bitmap, bool[,] visited, Queue<Point> queue, int x, int y)
    {
        if (visited[x, y])
        {
            return;
        }

        visited[x, y] = true;

        if (IsDark(bitmap.GetIntensity(x, y)))
        {
            queue.Enqueue(new Point(x, y));
        }
    }

    private List<Rectangle> FindBestHorizontalRuns(GrayImage source, int expectedRuns)
    {
        List<Rectangle> bestRuns = [];
        var bestScore = 0;

        for (var y = 0; y < source.Height; y++)
        {
            var runs = FindRuns(source, y);
            if (runs.Count != expectedRuns)
            {
                continue;
            }

            var totalWidth = runs.Sum(run => run.Width);
            if (totalWidth > bestScore)
            {
                bestScore = totalWidth;
                bestRuns = runs
                    .Select(run => Rectangle.FromLTRB(run.Left, 0, run.Right, source.Height))
                    .ToList();
            }
        }

        return bestRuns;
    }

    private List<Rectangle> FindRuns(GrayImage source, int y)
    {
        var runs = new List<Rectangle>();
        var start = -1;

        for (var x = 0; x < source.Width; x++)
        {
            if (IsDark(source.GetIntensity(x, y)))
            {
                if (start < 0)
                {
                    start = x;
                }
            }
            else if (start >= 0)
            {
                AddRunIfValid(runs, start, x);
                start = -1;
            }
        }

        if (start >= 0)
        {
            AddRunIfValid(runs, start, source.Width);
        }

        return runs;
    }

    private void AddRunIfValid(List<Rectangle> runs, int start, int endExclusive)
    {
        var width = endExclusive - start;
        if (width < options.MinimumDigitWidth * 2)
        {
            return;
        }

        runs.Add(Rectangle.FromLTRB(start, 0, endExclusive, 1));
    }

    private List<GrayImage> SegmentDigits(GrayImage source)
    {
        var glyph = ExtractInkBounds(source);
        if (glyph is null)
        {
            return [];
        }

        var columns = new int[glyph.Value.Width];
        for (var x = 0; x < glyph.Value.Width; x++)
        {
            for (var y = 0; y < glyph.Value.Height; y++)
            {
                if (IsDark(source.GetIntensity(glyph.Value.Left + x, glyph.Value.Top + y)))
                {
                    columns[x]++;
                }
            }
        }

        var segments = new List<GrayImage>();
        var start = -1;

        for (var x = 0; x < columns.Length; x++)
        {
            if (columns[x] > 0 && start < 0)
            {
                start = x;
                continue;
            }

            if (columns[x] == 0 && start >= 0)
            {
                AddSegmentIfValid(source, glyph.Value, start, x - 1, segments);
                start = -1;
            }
        }

        if (start >= 0)
        {
            AddSegmentIfValid(source, glyph.Value, start, columns.Length - 1, segments);
        }

        return segments;
    }

    private void AddSegmentIfValid(GrayImage source, Rectangle glyphBounds, int start, int end, List<GrayImage> segments)
    {
        var width = end - start + 1;
        if (width < options.MinimumDigitWidth)
        {
            return;
        }

        var segmentBounds = Rectangle.FromLTRB(
            glyphBounds.Left + start,
            glyphBounds.Top,
            glyphBounds.Left + end + 1,
            glyphBounds.Bottom);
        segments.Add(source.Crop(segmentBounds));
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<bool[,]>> BuildTemplates(DigitRecognitionOptions options) =>
        Enumerable.Range(0, 10).ToDictionary(digit => digit, digit => BuildTemplateSet(digit, options));

    private static IReadOnlyList<bool[,]> BuildTemplateSet(int digit, DigitRecognitionOptions options) =>
        GlyphRenderer.TemplateTypefaces
            .Select(typeface => BuildTemplate(digit, typeface, options))
            .ToList();

    private static bool[,] BuildTemplate(int digit, SKTypeface typeface, DigitRecognitionOptions options)
    {
        var glyph = GlyphRenderer.RenderDigit(
            digit, options.TemplateWidth, options.TemplateHeight, options.TemplateHeight * 0.70f, typeface);

        return NormalizeGlyph(glyph, options)
            ?? throw new InvalidOperationException("Digit template could not be created.");
    }

    protected bool[,]? NormalizeGlyph(GrayImage source) => NormalizeGlyph(source, Options);

    private static bool[,]? NormalizeGlyph(GrayImage source, DigitRecognitionOptions options)
    {
        var bounds = ExtractInkBounds(source, options);
        if (bounds is null)
        {
            return null;
        }

        var cropped = source.Crop(bounds.Value);
        var normalized = cropped.Resize(options.TemplateWidth, options.TemplateHeight);

        var pixels = new bool[options.TemplateWidth, options.TemplateHeight];
        for (var x = 0; x < options.TemplateWidth; x++)
        {
            for (var y = 0; y < options.TemplateHeight; y++)
            {
                pixels[x, y] = IsDark(normalized.GetIntensity(x, y), options);
            }
        }

        return pixels;
    }

    protected Rectangle? ExtractInkBounds(GrayImage source) => ExtractInkBounds(source, Options);

    private static Rectangle? ExtractInkBounds(GrayImage source, DigitRecognitionOptions options)
    {
        var minX = source.Width;
        var minY = source.Height;
        var maxX = -1;
        var maxY = -1;
        var darkPixels = 0;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (!IsDark(source.GetIntensity(x, y), options))
                {
                    continue;
                }

                darkPixels++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        var inkRatio = (float)darkPixels / (source.Width * source.Height);
        if (maxX < minX || maxY < minY || inkRatio < options.MinimumInkRatio)
        {
            return null;
        }

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static float CalculateConfidence(bool[,] glyph, bool[,] template)
    {
        var matches = 0;
        var total = glyph.GetLength(0) * glyph.GetLength(1);

        for (var x = 0; x < glyph.GetLength(0); x++)
        {
            for (var y = 0; y < glyph.GetLength(1); y++)
            {
                if (glyph[x, y] == template[x, y])
                {
                    matches++;
                }
            }
        }

        return (float)matches / total;
    }

    private bool IsDark(byte intensity) => IsDark(intensity, Options);

    private static bool IsDark(byte intensity, DigitRecognitionOptions options) =>
        intensity <= options.DarkPixelThreshold;
}
