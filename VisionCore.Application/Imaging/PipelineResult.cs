namespace VisionCore.Application.Imaging;

using VisionCore.Domain.Imaging.Evaluation;

/// <summary>
/// Outcome of running a processing pipeline over a single source document:
/// the recognized values, the review decision and diagnostic traces.
/// </summary>
public sealed record PipelineResult(
    bool IsSuccess,
    string? Error,
    int? TeamId,
    int? Score,
    double Confidence,
    string? RecognizedDigits,
    string? TeamIdDigitConfidenceTrace,
    string? ScoreDigitConfidenceTrace,
    string? CroppedRegionsTrace,
    ReviewStatus? ReviewStatus,
    EvaluationFailureCode? FailureCode);
