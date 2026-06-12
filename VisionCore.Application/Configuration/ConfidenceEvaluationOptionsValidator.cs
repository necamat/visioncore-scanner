namespace VisionCore.Application.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates the relationships DataAnnotations cannot express: the thresholds
/// must be ordered, and they must respect the <see cref="HeuristicConfidence"/>
/// contract — otherwise a configuration edit could silently auto-accept
/// heuristic guesses, which is exactly what the review mechanism exists to
/// prevent.
/// </summary>
public sealed class ConfidenceEvaluationOptionsValidator : IValidateOptions<ConfidenceEvaluationOptions>
{
    public ValidateOptionsResult Validate(string? name, ConfidenceEvaluationOptions options)
    {
        var failures = new List<string>();

        if (options.MinimumReviewConfidence >= options.MinimumAcceptedConfidence)
        {
            failures.Add(
                $"MinimumReviewConfidence ({options.MinimumReviewConfidence}) must be below " +
                $"MinimumAcceptedConfidence ({options.MinimumAcceptedConfidence}).");
        }

        if (options.MinimumAcceptedConfidence <= HeuristicConfidence.Strong)
        {
            failures.Add(
                $"MinimumAcceptedConfidence ({options.MinimumAcceptedConfidence}) must be above the strongest " +
                $"heuristic confidence ({HeuristicConfidence.Strong}); otherwise heuristic guesses would " +
                "auto-accept instead of routing to human review.");
        }

        if (options.MinimumReviewConfidence > HeuristicConfidence.Weak)
        {
            failures.Add(
                $"MinimumReviewConfidence ({options.MinimumReviewConfidence}) must not exceed the weakest " +
                $"heuristic confidence ({HeuristicConfidence.Weak}); otherwise heuristic reads would be " +
                "rejected instead of reviewed.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
