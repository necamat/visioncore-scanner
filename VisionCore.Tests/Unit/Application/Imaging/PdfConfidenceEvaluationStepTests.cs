using FluentAssertions;
using VisionCore.Application.Configuration;
using VisionCore.Application.Imaging;
using VisionCore.Application.Imaging.Steps.Pdf;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Domain.Imaging.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Application.Imaging;

public sealed class PdfConfidenceEvaluationStepTests
{
    // Defaults: accepted >= 0.85, review >= 0.60.
    private static readonly ConfidenceEvaluationOptions Options = new();

    [Fact]
    public async Task ExecuteAsync_Accepts_When_Every_Digit_Clears_The_Accepted_Threshold()
    {
        var context = ContextWith(
            TeamId(0.95f, 0.92f),
            Score(0.97f, 0.90f, 0.93f));

        var result = await new PdfConfidenceEvaluationStep(Options).ExecuteAsync(context, CancellationToken.None);

        result.ShouldContinue.Should().BeTrue();
        context.FinalScanResult!.Status.Should().Be(ReviewStatus.Accepted);
    }

    [Fact]
    public async Task ExecuteAsync_Flags_For_Review_When_One_Digit_Is_Weak_Even_If_The_Average_Is_High()
    {
        // Average = 0.91 (well above accepted), but the weakest digit sits in
        // the review band — a heuristic read must never ride an otherwise
        // strong sheet into an auto-accept.
        var context = ContextWith(
            TeamId(0.71f, 0.99f),
            Score(0.99f, 0.99f, 0.99f));

        await new PdfConfidenceEvaluationStep(Options).ExecuteAsync(context, CancellationToken.None);

        context.FinalScanResult!.Status.Should().Be(ReviewStatus.NeedsReview);
    }

    [Fact]
    public async Task ExecuteAsync_Rejects_When_The_Weakest_Digit_Falls_Below_The_Review_Floor()
    {
        var context = ContextWith(
            TeamId(0.95f, 0.92f),
            Score(0.40f, 0.95f, 0.95f));

        await new PdfConfidenceEvaluationStep(Options).ExecuteAsync(context, CancellationToken.None);

        context.FinalScanResult!.Status.Should().Be(ReviewStatus.Rejected);
        context.FinalScanResult.Failure.Should().Be(EvaluationFailureCode.LowConfidence);
    }

    [Fact]
    public async Task ExecuteAsync_Reports_The_Global_Confidence_Not_The_Weakest_Digit()
    {
        var context = ContextWith(
            TeamId(0.71f, 0.99f),
            Score(0.99f, 0.99f, 0.99f));

        await new PdfConfidenceEvaluationStep(Options).ExecuteAsync(context, CancellationToken.None);

        context.FinalScanResult!.Confidence.Should().Be(
            context.DigitRecognitionResult!.GlobalConfidence,
            "the gate uses the weakest digit but the reported confidence stays the aggregate");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_The_Recognition_Result_Is_Missing()
    {
        var context = new PipelineContext { ImagePath = "sheet.pdf" };

        var result = await new PdfConfidenceEvaluationStep(Options).ExecuteAsync(context, CancellationToken.None);

        result.ShouldContinue.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    private static PipelineContext ContextWith(RecognizedNumber teamId, RecognizedNumber score)
    {
        var allDigits = teamId.Digits.Concat(score.Digits).ToList();
        var globalConfidence = allDigits.Average(digit => digit.Confidence);

        return new PipelineContext
        {
            ImagePath = "sheet.pdf",
            DigitRecognitionResult = DigitRecognitionResult.Success(teamId, score, globalConfidence)
        };
    }

    private static RecognizedNumber TeamId(float firstConfidence, float secondConfidence) =>
        new(
        [
            new RecognizedDigit(FormRegion.TeamIdDigit1, 1, firstConfidence),
            new RecognizedDigit(FormRegion.TeamIdDigit2, 2, secondConfidence)
        ]);

    private static RecognizedNumber Score(float first, float second, float third) =>
        new(
        [
            new RecognizedDigit(FormRegion.ScoreDigit1, 0, first),
            new RecognizedDigit(FormRegion.ScoreDigit2, 7, second),
            new RecognizedDigit(FormRegion.ScoreDigit3, 5, third)
        ]);
}
