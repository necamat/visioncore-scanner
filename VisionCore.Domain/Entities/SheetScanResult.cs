namespace VisionCore.Domain.Entities;

using VisionCore.Domain.Imaging.Evaluation;

public sealed record SheetScanResult(
    int Round,
    string SourcePath,
    int? TeamId,
    int? Score,
    double Confidence,
    ReviewStatus Status,
    EvaluationFailureCode? FailureCode);
