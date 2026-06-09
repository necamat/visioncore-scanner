namespace VisionCore.Application.Abstractions;

using VisionCore.Domain.Imaging;

/// <summary>
/// Strategy for obtaining the form regions from a source document. The PDF
/// implementation crops fixed pixel coordinates; other formats (e.g. photos)
/// can plug in their own extraction behind this interface.
/// </summary>
public interface IRegionExtractor
{
    /// <summary>
    /// Extracts the form regions (TeamId, Score and their digit sub-regions)
    /// from the source file at <paramref name="sourcePath"/>.
    /// </summary>
    Task<RegionExtractionResult> ExtractAsync(string sourcePath, CancellationToken ct);
}
