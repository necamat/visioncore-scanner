namespace VisionCore.Application.Configuration;

using System.ComponentModel.DataAnnotations;

/// <summary>Engine used to recognize the handwritten score digits.</summary>
public enum ScoreRecognitionEngine
{
    /// <summary>Deterministic template matching against rendered font glyphs.</summary>
    TemplateMatching,

    /// <summary>An ONNX digit-classification model (MNIST-class CNN).</summary>
    Onnx
}

/// <summary>
/// Tunables for digit recognition. Bound from the "DigitRecognitionOptions"
/// configuration section and validated at startup.
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

    /// <summary>
    /// Engine for the handwritten score digits. The printed team id always
    /// uses template matching — its glyphs come from a known font, which is
    /// exactly what templates are good at; handwriting is not.
    /// </summary>
    public ScoreRecognitionEngine ScoreEngine { get; init; } = ScoreRecognitionEngine.TemplateMatching;

    /// <summary>Path to the ONNX digit-classification model (used when <see cref="ScoreEngine"/> is Onnx).</summary>
    public string OnnxModelPath { get; init; } = "./Models/mnist-12.onnx";
}
