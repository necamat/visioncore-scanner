namespace VisionCore.Application.Imaging;

using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Domain.Imaging.Recognition;

/// <summary>
/// Carries the outputs of each pipeline stage for a single run. Holds only the
/// three genuine stage results — everything reported in <see cref="PipelineResult"/>
/// (detected values, traces) is derived from these, not stored separately. Each
/// property is written by exactly one step and read by later ones.
/// </summary>
public sealed class PipelineContext
{
    public required string ImagePath { get; init; }

    /// <summary>Set by the crop/region-extraction stage.</summary>
    public CroppedFormRegions? CroppedRegions { get; set; }

    /// <summary>Set by the digit-recognition stage.</summary>
    public DigitRecognitionResult? DigitRecognitionResult { get; set; }

    /// <summary>Set by the confidence-evaluation stage.</summary>
    public FinalScanResult? FinalScanResult { get; set; }
}
