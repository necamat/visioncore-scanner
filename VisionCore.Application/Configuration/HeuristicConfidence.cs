namespace VisionCore.Application.Configuration;

/// <summary>
/// Fixed confidence tiers assigned to shape-heuristic digit reads. Heuristics
/// are educated guesses, not template evidence, so their values participate in
/// a contract with <see cref="ConfidenceEvaluationOptions"/>:
/// <see cref="ConfidenceEvaluationOptions.MinimumAcceptedConfidence"/> must sit
/// above <see cref="Strong"/> (a heuristic read must never auto-accept) and
/// <see cref="ConfidenceEvaluationOptions.MinimumReviewConfidence"/> must not
/// exceed <see cref="Weak"/> (a heuristic read must reach review, not be
/// rejected). The contract is enforced at startup by
/// <see cref="ConfidenceEvaluationOptionsValidator"/>. The tiers preserve the
/// relative strength of the heuristic rules so best-candidate selection still
/// prefers the stronger rule.
/// </summary>
public static class HeuristicConfidence
{
    public const float Strong = 0.70f;
    public const float Moderate = 0.68f;
    public const float Weak = 0.66f;
}
