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

    /// <summary>
    /// Pixel agreement alone is a poor certainty signal: a wrong template can
    /// still agree on 85% of pixels of a degraded glyph. The margin to the
    /// best *other* digit is what separates a confident read from a guess, so
    /// the reported confidence is the agreement scaled by a certainty factor
    /// that falls from 1 (margin at or above <see cref="FullCertaintyMargin"/>)
    /// to <see cref="MinimumCertainty"/> (a dead tie). The floor keeps cleanly
    /// scanned digits — whose margins shrink through JPEG encoding, cropping
    /// and border removal — comfortably above the accept threshold, while a
    /// near-tie still loses enough confidence to land in the review band.
    /// </summary>
    private const float FullCertaintyMargin = 0.02f;
    private const float MinimumCertainty = 0.85f;

    protected RecognizedDigit? RecognizeGlyph(GrayImage source, FormRegion region)
    {
        var glyph = NormalizeGlyph(source);
        if (glyph is null)
        {
            return null;
        }

        var bestDigit = -1;
        var bestAgreement = 0f;
        var bestOtherDigitAgreement = 0f;

        foreach (var templateSet in _templates)
        {
            foreach (var template in templateSet.Value)
            {
                var agreement = CalculateConfidence(glyph, template);
                if (agreement > bestAgreement)
                {
                    if (templateSet.Key != bestDigit)
                    {
                        bestOtherDigitAgreement = bestAgreement;
                    }

                    bestAgreement = agreement;
                    bestDigit = templateSet.Key;
                }
                else if (templateSet.Key != bestDigit && agreement > bestOtherDigitAgreement)
                {
                    bestOtherDigitAgreement = agreement;
                }
            }
        }

        // The raw agreement decides whether a glyph was matched at all; the
        // margin only lowers the *reported* confidence so an ambiguous read
        // routes to human review instead of being dropped.
        if (bestDigit < 0 || bestAgreement < options.TemplateMatchThreshold)
        {
            return null;
        }

        return new RecognizedDigit(region, bestDigit, ScaleByMargin(bestAgreement, bestOtherDigitAgreement));
    }

    /// <summary>
    /// Applies the margin-aware certainty factor to a raw agreement score.
    /// Shared by every template-matching path so no fallback can report a
    /// near-tie at full confidence.
    /// </summary>
    protected static float ScaleByMargin(float bestAgreement, float bestOtherDigitAgreement)
    {
        var margin = bestAgreement - bestOtherDigitAgreement;
        var certainty = MinimumCertainty +
            ((1f - MinimumCertainty) * Math.Clamp(margin / FullCertaintyMargin, 0f, 1f));
        return bestAgreement * certainty;
    }

    protected static NumberRecognitionResult Failure(
        RecognitionFailureCode failure,
        RecognizedNumber? number,
        float confidence)
    {
        return NumberRecognitionResult.FailureResult(failure, number, confidence);
    }

    protected GrayImage PrepareForRecognition(GrayImage source) =>
        GlyphIsolation.PrepareForRecognition(source, options.DarkPixelThreshold);

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

    private static Rectangle? ExtractInkBounds(GrayImage source, DigitRecognitionOptions options) =>
        GlyphIsolation.ExtractInkBounds(source, options.DarkPixelThreshold, options.MinimumInkRatio);

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
