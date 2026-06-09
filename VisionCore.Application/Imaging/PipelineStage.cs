namespace VisionCore.Application.Imaging;

/// <summary>
/// Ordered stages of the scanning pipeline. Steps are executed in ascending value.
/// </summary>
public enum PipelineStage
{
    CropRegions = 1,
    RecognizeDigits = 2,
    EvaluateConfidence = 3
}
