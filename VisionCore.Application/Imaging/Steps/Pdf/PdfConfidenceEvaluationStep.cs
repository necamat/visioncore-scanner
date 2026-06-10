namespace VisionCore.Application.Imaging.Steps.Pdf;

using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Application.Imaging;
using VisionCore.Domain.Imaging.Evaluation;

/// <summary>
/// Turns the digit-recognition result into a final review decision
/// (accepted / needs-review / rejected) by applying the configured confidence
/// thresholds. The decision gates on the weakest digit rather than the average:
/// one uncertain digit changes the whole number, so an otherwise strong sheet
/// must not absorb a low-confidence read into an auto-accept.
/// </summary>
public sealed class PdfConfidenceEvaluationStep(ConfidenceEvaluationOptions options) : IImageProcessingStep
{
    public PdfConfidenceEvaluationStep(IOptions<ConfidenceEvaluationOptions> options)
        : this(options.Value)
    {
    }

    /// <inheritdoc />
    public PipelineStage Stage => PipelineStage.EvaluateConfidence;

    /// <inheritdoc />
    public Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Earlier steps guarantee a successful recognition result with both
        // numbers present; guard defensively in case the step order changes.
        var recognition = context.DigitRecognitionResult;
        if (recognition?.TeamId is null || recognition.Score is null)
        {
            return Task.FromResult(StepResult.Fail("Digit recognition result is missing."));
        }

        var teamId = recognition.TeamId.Value;
        var score = recognition.Score.Value;
        var confidence = recognition.GlobalConfidence;

        // The reported confidence stays the global aggregate; only the gate
        // uses the weakest digit.
        var weakestDigitConfidence = recognition.TeamId.Digits
            .Concat(recognition.Score.Digits)
            .Min(digit => digit.Confidence);

        context.FinalScanResult = weakestDigitConfidence switch
        {
            _ when weakestDigitConfidence >= options.MinimumAcceptedConfidence =>
                FinalScanResult.Accepted(teamId, score, confidence),
            _ when weakestDigitConfidence >= options.MinimumReviewConfidence =>
                FinalScanResult.NeedsReview(teamId, score, confidence),
            _ => FinalScanResult.Rejected(EvaluationFailureCode.LowConfidence, teamId, score, confidence)
        };

        return Task.FromResult(StepResult.Continue);
    }
}
