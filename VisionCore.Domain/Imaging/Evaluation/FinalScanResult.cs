namespace VisionCore.Domain.Imaging.Evaluation;

public sealed record FinalScanResult(
    ReviewStatus Status,
    EvaluationFailureCode? Failure,
    int? TeamId,
    int? Score,
    float Confidence)
{
    public bool IsSuccess => Status != ReviewStatus.Rejected;

    public static FinalScanResult Accepted(int teamId, int score, float confidence) =>
        new(ReviewStatus.Accepted, null, teamId, score, confidence);

    public static FinalScanResult NeedsReview(int? teamId, int? score, float confidence) =>
        new(ReviewStatus.NeedsReview, null, teamId, score, confidence);

    public static FinalScanResult Rejected(EvaluationFailureCode failure, int? teamId, int? score, float confidence) =>
        new(ReviewStatus.Rejected, failure, teamId, score, confidence);
}
