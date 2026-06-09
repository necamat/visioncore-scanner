namespace VisionCore.Application.Abstractions;

using VisionCore.Application.Imaging;

/// <summary>
/// Runs a source document through its processing steps and returns the outcome.
/// </summary>
public interface IImageProcessingPipeline
{
    /// <summary>Processes the source file at <paramref name="imagePath"/>.</summary>
    Task<PipelineResult> ProcessAsync(string imagePath, CancellationToken ct);
}
