using FluentAssertions;
using Microsoft.Extensions.Options;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Infrastructure.Implementations.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class TemplateMatchingTeamIdRecognizerTests
{
    private static readonly ConfidenceEvaluationOptions EvaluationDefaults = new();

    [Fact]
    public async Task RecognizeAsync_Should_Read_A_Single_Stroke_As_One_Via_The_Shape_Heuristic()
    {
        var recognizer = CreateRecognizer();
        var regions = new CroppedFormRegions(
        [
            NarrowRegionWithStrokes(FormRegion.TeamIdDigit1, strokeCount: 1),
            NarrowRegionWithStrokes(FormRegion.TeamIdDigit2, strokeCount: 1)
        ]);

        var result = await recognizer.RecognizeAsync(regions, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Number!.Value.Should().Be(11);
    }

    [Fact]
    public async Task RecognizeAsync_Should_Read_Two_Strokes_As_Zero_Via_The_Shape_Heuristic()
    {
        var recognizer = CreateRecognizer();
        var regions = new CroppedFormRegions(
        [
            NarrowRegionWithStrokes(FormRegion.TeamIdDigit1, strokeCount: 2),
            NarrowRegionWithStrokes(FormRegion.TeamIdDigit2, strokeCount: 2)
        ]);

        var result = await recognizer.RecognizeAsync(regions, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Number!.Value.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RecognizeAsync_Should_Cap_Heuristic_Reads_Inside_The_Review_Band(int strokeCount)
    {
        // Heuristic reads are educated guesses: every digit they produce must
        // land below the auto-accept threshold (so a human reviews it) but at
        // or above the review floor (so the sheet is not silently rejected).
        var recognizer = CreateRecognizer();
        var regions = new CroppedFormRegions(
        [
            NarrowRegionWithStrokes(FormRegion.TeamIdDigit1, strokeCount),
            NarrowRegionWithStrokes(FormRegion.TeamIdDigit2, strokeCount)
        ]);

        var result = await recognizer.RecognizeAsync(regions, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        foreach (var digit in result.Number!.Digits)
        {
            digit.Confidence.Should().BeLessThanOrEqualTo(
                HeuristicConfidence.Strong, "heuristic reads must stay within the shared tiers");
            digit.Confidence.Should().BeLessThan(EvaluationDefaults.MinimumAcceptedConfidence);
            digit.Confidence.Should().BeGreaterThanOrEqualTo(EvaluationDefaults.MinimumReviewConfidence);
        }
    }

    private static TemplateMatchingTeamIdRecognizer CreateRecognizer() =>
        new(Options.Create(new DigitRecognitionOptions()));

    /// <summary>
    /// A very narrow region (aspect ratio below 0.20) holding full-height
    /// vertical strokes: one stroke reads as "1", two strokes as "0". The
    /// strokes touch the top and bottom edge, so the prepare phase removes
    /// them as edge-connected ink and only the shape heuristic can answer.
    /// </summary>
    private static CroppedRegion NarrowRegionWithStrokes(FormRegion region, int strokeCount)
    {
        const int width = 10;
        const int height = 60;
        var pixels = new byte[width * height];
        Array.Fill(pixels, (byte)255);

        var strokeColumns = strokeCount == 1
            ? new[] { 4, 5 }
            : [2, 3, 6, 7];

        foreach (var x in strokeColumns)
        {
            for (var y = 0; y < height; y++)
            {
                pixels[(y * width) + x] = 0;
            }
        }

        return new CroppedRegion(region, width, height, pixels, new PixelBounds(0, 0, width, height));
    }
}
