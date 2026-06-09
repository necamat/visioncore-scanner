namespace VisionCore.Domain.Imaging;

using VisionCore.Domain.Imaging.Cropping;

/// <summary>
/// Outcome of a region-extraction attempt: the cropped regions on success,
/// or a diagnostic error message on failure.
/// </summary>
public sealed record RegionExtractionResult(
    bool IsSuccess,
    CroppedFormRegions? Regions,
    string? Error)
{
    public static RegionExtractionResult Success(CroppedFormRegions regions) =>
        new(true, regions, null);

    public static RegionExtractionResult Failure(string error) =>
        new(false, null, error);
}
