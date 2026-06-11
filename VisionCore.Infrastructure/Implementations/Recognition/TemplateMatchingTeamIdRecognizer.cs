namespace VisionCore.Infrastructure.Implementations.Recognition;

using System.Drawing;
using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Domain.Imaging.Recognition;
using VisionCore.Infrastructure.Imaging;

/// <summary>
/// Recognizes the printed team-id number, either from dedicated digit regions or
/// from a single container box, using template matching plus shape heuristics.
/// </summary>
public sealed class TemplateMatchingTeamIdRecognizer(DigitRecognitionOptions options)
    : TemplateMatchingNumberRecognizer(options), ITeamIdRecognizer
{
    private const int BoxTemplateWidth = 64;
    private const int BoxTemplateHeight = 112;

    // Shape heuristics are educated guesses, not template evidence, so their
    // confidence is capped below the accepted threshold (see
    // ConfidenceEvaluationOptions): a heuristic read must always land in the
    // needs-review band and reach a human, never auto-accept. The tiers sit
    // well below the weakest clean template read (~0.79) so the accept
    // threshold can be calibrated between the two, and they preserve the
    // relative strength of the rules so best-candidate selection still
    // prefers the stronger rule.
    private const float StrongHeuristicConfidence = 0.70f;
    private const float ModerateHeuristicConfidence = 0.68f;
    private const float WeakHeuristicConfidence = 0.66f;

    private static readonly FormRegion[] TeamIdRegions =
    [
        FormRegion.TeamIdDigit1,
        FormRegion.TeamIdDigit2
    ];

    private readonly IReadOnlyDictionary<int, bool[,]> _boxedTemplates = BuildBoxTemplates(options);

    public TemplateMatchingTeamIdRecognizer(IOptions<DigitRecognitionOptions> options)
        : this(options.Value)
    {
    }

    public Task<NumberRecognitionResult> RecognizeAsync(
        CroppedFormRegions regions,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var hasTeamIdContainer = regions.Contains(FormRegion.TeamId);
        var hasDigitRegions = TeamIdRegions.All(regions.Contains);

        if (!hasTeamIdContainer && !hasDigitRegions)
        {
            return Task.FromResult(Failure(RecognitionFailureCode.MissingRegion, null, 0f));
        }

        try
        {
            IReadOnlyList<RecognizedDigit?> digits;
            if (hasDigitRegions)
            {
                digits = TeamIdRegions
                    .Select(region => RecognizePrintedDigit(regions.GetRegion(region), ct))
                    .ToList();
            }
            else
            {
                digits = ReadDigitsFromContainer(regions.GetRegion(FormRegion.TeamId), ct);
            }

            if (digits.Count == 0 || digits.Any(digit => digit is null))
            {
                return Task.FromResult(Failure(RecognitionFailureCode.InvalidDigit, null, 0f));
            }

            var number = new RecognizedNumber(digits.Select(digit => digit!).ToList());
            return Task.FromResult(NumberRecognitionResult.Success(number));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(Failure(RecognitionFailureCode.InvalidDigit, null, 0f));
        }
    }

    private IReadOnlyList<RecognizedDigit?> ReadDigitsFromContainer(CroppedRegion region, CancellationToken ct)
    {
        var bitmap = Load(region);
        var extractedBoxes = ExtractBoxContents(bitmap, expectedRuns: 2);
        if (extractedBoxes.Count == 2)
        {
            return extractedBoxes
                .Select((box, index) => RecognizePrintedDigit(box, TeamIdRegions[index], ct))
                .ToList();
        }

        if (bitmap.Width > bitmap.Height * 1.35f)
        {
            return RecognizeMultipleDigits(region, ct)
                .Cast<RecognizedDigit?>()
                .ToList();
        }

        return
        [
            RecognizePrintedDigit(region, ct)
        ];
    }

    private RecognizedDigit? RecognizePrintedDigit(CroppedRegion region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var bitmap = Load(region);
        return RecognizePrintedDigit(bitmap, region.Region, ct);
    }

    private RecognizedDigit? RecognizePrintedDigit(GrayImage source, FormRegion region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        RecognizedDigit? bestMatch = TryRecognizeRawBoxPattern(source, region);

        foreach (var insetPercent in new[] { 0.12f, 0.16f, 0.20f, 0.24f, 0.28f })
        {
            var candidate = CropInset(source, insetPercent);
            var prepared = PrepareForRecognition(candidate);

            // Template matching is authoritative when it clears the match
            // threshold; the shape heuristic only disambiguates 0/1 when the
            // template match is weak (small thin glyphs normalize poorly).
            var template = RecognizeGlyph(prepared, region);
            var recognized = template is not null
                ? template
                : TryRecognizePrintedDigitHeuristically(prepared, region);
            if (recognized is null)
            {
                continue;
            }

            if (bestMatch is null || recognized.Confidence > bestMatch.Confidence)
            {
                bestMatch = recognized;
            }
        }

        return bestMatch ?? RecognizePrintedBox(region, source);
    }

    /// <summary>
    /// Fast path for a very narrow region: a tall thin glyph with two dark runs
    /// across its middle band reads as "0", a single run as "1". Thresholds are
    /// empirical for the 200-DPI form.
    /// </summary>
    private RecognizedDigit? TryRecognizeRawBoxPattern(GrayImage source, FormRegion region)
    {
        var sourceAspectRatio = source.Width / (float)source.Height;
        if (sourceAspectRatio > 0.20f)
        {
            return null;
        }

        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        var runCount = CountDarkRunsInMiddleBand(source, bounds, darkThreshold: 210, minDarkRowsRatio: 0.25f);

        return runCount switch
        {
            >= 2 => new RecognizedDigit(region, 0, StrongHeuristicConfidence),
            1 => new RecognizedDigit(region, 1, WeakHeuristicConfidence),
            _ => null
        };
    }

    private RecognizedDigit? RecognizePrintedBox(FormRegion region, GrayImage source)
    {
        var normalized = NormalizeFullRegion(source);
        var bestDigit = -1;
        var bestConfidence = 0f;

        foreach (var template in _boxedTemplates)
        {
            var confidence = CalculateTemplateConfidence(normalized, template.Value);
            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestDigit = template.Key;
            }
        }

        return bestDigit < 0
            ? null
            : new RecognizedDigit(region, bestDigit, bestConfidence);
    }

    /// <summary>
    /// Shape-based fallback that disambiguates the hardest printed cases (0 vs 1)
    /// when template matching is weak. The thresholds (aspect-ratio bands, hole
    /// count, centre-band ink ratio, dark-run count) are empirical tuning values
    /// for the 200-DPI form, not general constants — hence they are inlined here
    /// rather than surfaced as configuration.
    /// </summary>
    private RecognizedDigit? TryRecognizePrintedDigitHeuristically(GrayImage prepared, FormRegion region)
    {
        var bounds = ExtractInkBounds(prepared);
        var glyph = NormalizeGlyph(prepared);
        if (bounds is null || glyph is null)
        {
            return null;
        }

        var aspectRatio = bounds.Value.Width / (float)bounds.Value.Height;
        var closedGlyph = GlyphMorphology.Close(glyph);
        var holeCount = GlyphMorphology.CountHoles(closedGlyph);
        var centerBandInkRatio = GlyphMorphology.CenterBandInkRatio(glyph);
        var middleBandRunCount = CountDarkRunsInMiddleBand(prepared, bounds.Value);

        if (middleBandRunCount >= 2 && aspectRatio <= 0.20f)
        {
            return new RecognizedDigit(region, 0, StrongHeuristicConfidence);
        }

        if ((holeCount == 1 || centerBandInkRatio <= 0.08f) &&
            aspectRatio >= 0.20f &&
            aspectRatio <= 0.45f)
        {
            return new RecognizedDigit(region, 0, ModerateHeuristicConfidence);
        }

        if (aspectRatio <= 0.32f && holeCount == 0 && centerBandInkRatio >= 0.14f && middleBandRunCount == 1)
        {
            return new RecognizedDigit(region, 1, StrongHeuristicConfidence);
        }

        return null;
    }

    private int CountDarkRunsInMiddleBand(GrayImage prepared, Rectangle bounds)
    {
        return CountDarkRunsInMiddleBand(prepared, bounds, Options.DarkPixelThreshold, 0.50f);
    }

    private static int CountDarkRunsInMiddleBand(GrayImage prepared, Rectangle bounds, int darkThreshold, float minDarkRowsRatio)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        var startY = bounds.Top + Math.Max(0, (int)Math.Round(height * 0.45f));
        var endY = bounds.Top + Math.Min(height - 1, Math.Max((int)Math.Round(height * 0.55f), 1));
        var bandRows = Math.Max(1, endY - startY + 1);
        var minimumDarkRows = Math.Max(1, (int)Math.Ceiling(bandRows * minDarkRowsRatio));
        var runs = 0;
        var inRun = false;

        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var darkCount = 0;
            for (var y = startY; y <= endY; y++)
            {
                if (prepared.GetIntensity(x, y) <= darkThreshold)
                {
                    darkCount++;
                }
            }

            var isDark = darkCount >= minimumDarkRows;
            if (isDark && !inRun)
            {
                runs++;
                inRun = true;
            }
            else if (!isDark)
            {
                inRun = false;
            }
        }

        return runs;
    }

    private static IReadOnlyDictionary<int, bool[,]> BuildBoxTemplates(DigitRecognitionOptions options) =>
        Enumerable.Range(0, 10).ToDictionary(digit => digit, digit => BuildBoxTemplate(digit, options));

    private static bool[,] BuildBoxTemplate(int digit, DigitRecognitionOptions options)
    {
        // A boxed-digit template: the digit glyph centered inside a thin border,
        // matching how a single printed digit looks inside its form box.
        var glyph = GlyphRenderer.RenderDigit(
            digit, BoxTemplateWidth, BoxTemplateHeight, BoxTemplateHeight * 0.58f,
            GlyphRenderer.TemplateTypefaces[0]);

        for (var i = 2; i < BoxTemplateWidth - 2; i++)
        {
            glyph.SetIntensity(i, 2, 0);
            glyph.SetIntensity(i, BoxTemplateHeight - 3, 0);
        }

        for (var j = 2; j < BoxTemplateHeight - 2; j++)
        {
            glyph.SetIntensity(2, j, 0);
            glyph.SetIntensity(BoxTemplateWidth - 3, j, 0);
        }

        return NormalizeFullRegion(glyph, options);
    }

    private bool[,] NormalizeFullRegion(GrayImage source) => NormalizeFullRegion(source, Options);

    private static bool[,] NormalizeFullRegion(GrayImage source, DigitRecognitionOptions options)
    {
        var normalized = source.Resize(BoxTemplateWidth, BoxTemplateHeight);

        var pixels = new bool[BoxTemplateWidth, BoxTemplateHeight];
        for (var x = 0; x < BoxTemplateWidth; x++)
        {
            for (var y = 0; y < BoxTemplateHeight; y++)
            {
                pixels[x, y] = normalized.GetIntensity(x, y) <= options.DarkPixelThreshold;
            }
        }

        return pixels;
    }

    private static float CalculateTemplateConfidence(bool[,] candidate, bool[,] template)
    {
        var matches = 0;
        var total = candidate.GetLength(0) * candidate.GetLength(1);

        for (var x = 0; x < candidate.GetLength(0); x++)
        {
            for (var y = 0; y < candidate.GetLength(1); y++)
            {
                if (candidate[x, y] == template[x, y])
                {
                    matches++;
                }
            }
        }

        return matches / (float)total;
    }
}
