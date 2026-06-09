namespace VisionCore.Application.Imaging;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VisionCore.Application.Abstractions;
using VisionCore.Domain.Imaging.Recognition;

/// <summary>
/// Generic pipeline runner: executes its <see cref="IImageProcessingStep"/>s in
/// <see cref="PipelineStage"/> order over a shared <see cref="PipelineContext"/>,
/// stopping at the first step that fails, then projects the resulting context
/// into a <see cref="PipelineResult"/>.
///
/// Format-specific behaviour lives entirely in the injected steps, so adding a
/// new source format means supplying a different set of steps — not a new
/// pipeline class.
/// </summary>
public sealed class StepPipeline(IEnumerable<IImageProcessingStep> steps, ILogger<StepPipeline> logger)
    : IImageProcessingPipeline
{
    private readonly IReadOnlyList<IImageProcessingStep> _steps = OrderAndValidate(steps);

    public StepPipeline(IEnumerable<IImageProcessingStep> steps)
        : this(steps, NullLogger<StepPipeline>.Instance)
    {
    }

    public async Task<PipelineResult> ProcessAsync(string imagePath, CancellationToken ct)
    {
        var context = new PipelineContext { ImagePath = imagePath };

        try
        {
            foreach (var step in _steps)
            {
                ct.ThrowIfCancellationRequested();
                logger.LogDebug("Executing pipeline step {Step}", step.GetType().Name);

                var result = await step.ExecuteAsync(context, ct);
                if (!result.ShouldContinue)
                {
                    return Project(context, success: false, result.Error);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline failed for {Path}", imagePath);
            return Project(context, success: false, ex.Message);
        }

        return Project(context, success: true, error: null);
    }

    private static IReadOnlyList<IImageProcessingStep> OrderAndValidate(IEnumerable<IImageProcessingStep> steps)
    {
        var ordered = steps.OrderBy(step => step.Stage).ToList();

        if (ordered.Count == 0)
        {
            throw new InvalidOperationException("Pipeline must contain at least one step.");
        }

        var duplicate = ordered.GroupBy(step => step.Stage).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate pipeline stage: {duplicate.Key}.");
        }

        return ordered;
    }

    /// <summary>Derives the public result from the three stage outputs on the context.</summary>
    private static PipelineResult Project(PipelineContext context, bool success, string? error)
    {
        var recognition = context.DigitRecognitionResult;
        var teamId = recognition?.TeamId;
        var score = recognition?.Score;

        return new PipelineResult(
            IsSuccess: success,
            Error: error,
            TeamId: teamId?.Value,
            Score: score?.Value,
            Confidence: recognition?.GlobalConfidence ?? 0,
            RecognizedDigits: teamId is not null && score is not null ? $"{teamId.Text}:{score.Text}" : null,
            TeamIdDigitConfidenceTrace: FormatConfidenceTrace(teamId),
            ScoreDigitConfidenceTrace: FormatConfidenceTrace(score),
            CroppedRegionsTrace: FormatRegionsTrace(context),
            ReviewStatus: context.FinalScanResult?.Status,
            FailureCode: context.FinalScanResult?.Failure);
    }

    private static string? FormatConfidenceTrace(RecognizedNumber? number) =>
        number is null
            ? null
            : string.Join(",", number.Digits.Select(d => $"{d.Region}:{d.Value}@{d.Confidence:0.000}"));

    private static string? FormatRegionsTrace(PipelineContext context) =>
        context.CroppedRegions is null
            ? null
            : string.Join(
                ";",
                context.CroppedRegions.Regions
                    .OrderBy(r => r.Region)
                    .Select(r => $"{r.Region}={r.Width}x{r.Height}"));
}
