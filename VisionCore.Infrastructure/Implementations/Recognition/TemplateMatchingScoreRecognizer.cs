namespace VisionCore.Infrastructure.Implementations.Recognition;

using Microsoft.Extensions.Options;
using System.Drawing;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Domain.Imaging.Recognition;
using VisionCore.Infrastructure.Imaging;

/// <summary>
/// Recognizes the handwritten score number from dedicated digit regions or a
/// single score container box, taking the higher-confidence reading of the two.
/// </summary>
public sealed class TemplateMatchingScoreRecognizer(DigitRecognitionOptions options)
    : TemplateMatchingNumberRecognizer(options), IScoreRecognizer
{
    private static readonly FormRegion[] ScoreRegions =
    [
        FormRegion.ScoreDigit1,
        FormRegion.ScoreDigit2,
        FormRegion.ScoreDigit3
    ];

    public TemplateMatchingScoreRecognizer(IOptions<DigitRecognitionOptions> options)
        : this(options.Value)
    {
    }

    public Task<NumberRecognitionResult> RecognizeAsync(
        CroppedFormRegions regions,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ScoreRegions.Any(region => !regions.Regions.Any(item => item.Region == region)) &&
            !regions.Regions.Any(item => item.Region == FormRegion.Score))
        {
            return Task.FromResult(Failure(RecognitionFailureCode.MissingRegion, null, 0f));
        }

        try
        {
            var directResult = TryRecognizeFromDigitRegions(regions, ct);
            var containerResult = TryRecognizeFromContainer(regions, ct);
            var bestResult = ChooseBetter(directResult, containerResult);

            if (bestResult is null)
            {
                return Task.FromResult(Failure(RecognitionFailureCode.InvalidDigit, null, 0f));
            }

            return Task.FromResult(bestResult);
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(Failure(RecognitionFailureCode.InvalidDigit, null, 0f));
        }
    }

    private NumberRecognitionResult? TryRecognizeFromDigitRegions(CroppedFormRegions regions, CancellationToken ct)
    {
        if (ScoreRegions.Any(region => !regions.Regions.Any(item => item.Region == region)))
        {
            return null;
        }

        var digits = ScoreRegions
            .Select(region => RecognizeScoreDigit(regions.GetRegion(region), ct))
            .ToList();

        if (digits.Any(digit => digit is null))
        {
            return null;
        }

        var number = new RecognizedNumber(digits.Select(digit => digit!).ToList());
        return NumberRecognitionResult.Success(number);
    }

    private NumberRecognitionResult? TryRecognizeFromContainer(CroppedFormRegions regions, CancellationToken ct)
    {
        if (!regions.Regions.Any(item => item.Region == FormRegion.Score))
        {
            return null;
        }

        var bitmap = Load(regions.GetRegion(FormRegion.Score));
        var boxes = ExtractBoxContents(bitmap, expectedRuns: 3);
        if (boxes.Count != 3)
        {
            return null;
        }

        var digits = boxes
            .Select((box, index) =>
            {
                ct.ThrowIfCancellationRequested();
                return RecognizeFromBitmap(box, ScoreRegions[index]);
            })
            .ToList();

        if (digits.Any(digit => digit is null))
        {
            return null;
        }

        var number = new RecognizedNumber(digits.Select(digit => digit!).ToList());
        return NumberRecognitionResult.Success(number);
    }

    private RecognizedDigit? RecognizeScoreDigit(CroppedRegion region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bitmap = Load(region);
        return RecognizeFromBitmap(bitmap, region.Region);
    }

    private RecognizedDigit? RecognizeFromBitmap(GrayImage source, FormRegion region)
    {
        RecognizedDigit? best = null;

        foreach (var inset in new[] { 0.08f, 0.12f, 0.16f, 0.20f })
        {
            var candidate = CropInset(source, inset);
            var prepared = PrepareForRecognition(candidate);
            var recognized = RecognizeGlyph(prepared, region);
            if (recognized is null)
            {
                continue;
            }

            if (best is null || recognized.Confidence > best.Confidence)
            {
                best = recognized;
            }
        }

        return best;
    }

    private static NumberRecognitionResult? ChooseBetter(
        NumberRecognitionResult? first,
        NumberRecognitionResult? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return first.Confidence >= second.Confidence
            ? first
            : second;
    }

    private static GrayImage CropInset(GrayImage source, float insetPercent)
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
}
