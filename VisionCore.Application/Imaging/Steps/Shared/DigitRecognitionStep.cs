namespace VisionCore.Application.Imaging.Steps.Shared;

using VisionCore.Application.Abstractions;
using VisionCore.Application.Imaging;

/// <summary>
/// Recognizes the team-id and score from the cropped regions and records the
/// result on the context for the evaluation step.
/// </summary>
public sealed class DigitRecognitionStep(IDigitRecognizer digitRecognizer) : IImageProcessingStep
{
    /// <inheritdoc />
    public PipelineStage Stage => PipelineStage.RecognizeDigits;

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        if (context.CroppedRegions is null)
        {
            return StepResult.Fail("Cropped regions are unavailable.");
        }

        var result = await digitRecognizer.RecognizeAsync(context.CroppedRegions, ct);
        context.DigitRecognitionResult = result;

        if (!result.IsSuccess || result.TeamId is null || result.Score is null)
        {
            return StepResult.Fail(result.Failure?.ToString() ?? "Digit recognition failed.");
        }

        return StepResult.Continue;
    }
}
