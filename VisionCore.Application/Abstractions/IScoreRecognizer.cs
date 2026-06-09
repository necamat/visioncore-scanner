namespace VisionCore.Application.Abstractions;

using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Recognition;

/// <summary>
/// Recognizes the score number from the cropped form regions.
/// </summary>
public interface IScoreRecognizer
{
    /// <summary>Recognizes the score number from the supplied cropped regions.</summary>
    Task<NumberRecognitionResult> RecognizeAsync(CroppedFormRegions regions, CancellationToken ct);
}
