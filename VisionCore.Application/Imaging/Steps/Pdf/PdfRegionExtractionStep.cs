namespace VisionCore.Application.Imaging.Steps.Pdf;

using VisionCore.Application.Abstractions;
using VisionCore.Application.Imaging;

/// <summary>
/// Crops the form regions from a PDF by delegating to an <see cref="IRegionExtractor"/>,
/// then publishes them on the context for the recognition step. Runs in the
/// <see cref="PipelineStage.CropRegions"/> slot.
/// </summary>
public sealed class PdfRegionExtractionStep(IRegionExtractor regionExtractor) : IImageProcessingStep
{
    /// <inheritdoc />
    public PipelineStage Stage => PipelineStage.CropRegions;

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var result = await regionExtractor.ExtractAsync(context.ImagePath, ct);

        if (!result.IsSuccess || result.Regions is null)
        {
            return StepResult.Fail(result.Error ?? "Region extraction returned no regions.");
        }

        context.CroppedRegions = result.Regions;
        return StepResult.Continue;
    }
}
