namespace VisionCore.Application.Abstractions;

using VisionCore.Application.Imaging;

/// <summary>
/// A single stage in a processing pipeline. Reads earlier stages' outputs from
/// the shared <see cref="PipelineContext"/>, writes its own, and returns a
/// <see cref="StepResult"/> telling the runner whether to continue.
/// </summary>
public interface IImageProcessingStep
{
    /// <summary>The stage slot that determines this step's execution order.</summary>
    PipelineStage Stage { get; }

    /// <summary>Executes this step against the shared pipeline context.</summary>
    Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct);
}
