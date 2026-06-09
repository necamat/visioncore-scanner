namespace VisionCore.Application.Configuration;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Confidence thresholds that turn a recognition score into a review decision.
/// Bound from the "ConfidenceEvaluationOptions" section and validated at startup.
/// </summary>
public sealed record ConfidenceEvaluationOptions
{
    /// <summary>At or above this confidence a scan is auto-accepted.</summary>
    [Range(0f, 1f)]
    public float MinimumAcceptedConfidence { get; init; } = 0.85f;

    /// <summary>At or above this (but below accepted) a scan is flagged for review.</summary>
    [Range(0f, 1f)]
    public float MinimumReviewConfidence { get; init; } = 0.60f;
}
