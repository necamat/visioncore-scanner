namespace VisionCore.Application.Configuration;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Tunables for template-matching digit recognition. Bound from the
/// "DigitRecognitionOptions" configuration section and validated at startup.
/// </summary>
public sealed record DigitRecognitionOptions
{
    /// <summary>Grayscale intensity (0-255) at or below which a pixel counts as ink.</summary>
    [Range(1, 254)]
    public int DarkPixelThreshold { get; init; } = 160;

    /// <summary>Normalized glyph template width in pixels.</summary>
    [Range(1, int.MaxValue)]
    public int TemplateWidth { get; init; } = 48;

    /// <summary>Normalized glyph template height in pixels.</summary>
    [Range(1, int.MaxValue)]
    public int TemplateHeight { get; init; } = 72;

    /// <summary>Minimum fraction of dark pixels for a region to be considered non-blank.</summary>
    [Range(0f, 1f)]
    public float MinimumInkRatio { get; init; } = 0.01f;

    /// <summary>Minimum width in pixels of a segmented digit run.</summary>
    [Range(1, int.MaxValue)]
    public int MinimumDigitWidth { get; init; } = 4;

    /// <summary>Minimum per-glyph template-match score for a digit to be accepted.</summary>
    [Range(0f, 1f)]
    public float TemplateMatchThreshold { get; init; } = 0.50f;
}
