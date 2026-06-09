namespace VisionCore.Application.Abstractions;

using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Recognition;

/// <summary>
/// Recognizes the team-id and score numbers from a set of cropped form regions.
/// </summary>
public interface IDigitRecognizer
{
    /// <summary>
    /// Recognizes both the team-id and the score from the supplied cropped regions.
    /// </summary>
    Task<DigitRecognitionResult> RecognizeAsync(CroppedFormRegions regions, CancellationToken ct);
}
