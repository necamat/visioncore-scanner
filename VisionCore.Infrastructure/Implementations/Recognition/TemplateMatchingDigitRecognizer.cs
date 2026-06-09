namespace VisionCore.Infrastructure.Implementations.Recognition;

using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Recognition;

/// <summary>
/// Coordinates team-id and score recognition into a single digit-recognition
/// result. It reports the recognized values and a combined confidence; deciding
/// whether that confidence is good enough (accept / review / reject) is the
/// evaluation step's job, not the recognizer's.
/// </summary>
public sealed class TemplateMatchingDigitRecognizer(
    ITeamIdRecognizer teamIdRecognizer,
    IScoreRecognizer scoreRecognizer) : IDigitRecognizer
{
    /// <summary>Convenience constructor that builds the team-id and score recognizers.</summary>
    public TemplateMatchingDigitRecognizer(IOptions<DigitRecognitionOptions> options)
        : this(
            new TemplateMatchingTeamIdRecognizer(options.Value),
            new TemplateMatchingScoreRecognizer(options.Value))
    {
    }

    public async Task<DigitRecognitionResult> RecognizeAsync(
        CroppedFormRegions regions,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var teamId = await teamIdRecognizer.RecognizeAsync(regions, ct);
        if (!teamId.IsSuccess || teamId.Number is null)
        {
            return DigitRecognitionResult.FailureResult(
                teamId.Failure ?? RecognitionFailureCode.InvalidDigit,
                teamId.Number,
                null,
                0f);
        }

        var score = await scoreRecognizer.RecognizeAsync(regions, ct);
        if (!score.IsSuccess || score.Number is null)
        {
            return DigitRecognitionResult.FailureResult(
                score.Failure ?? RecognitionFailureCode.InvalidDigit,
                teamId.Number,
                score.Number,
                0f);
        }

        var globalConfidence = (teamId.Confidence + score.Confidence) / 2f;
        return DigitRecognitionResult.Success(teamId.Number, score.Number, globalConfidence);
    }
}
