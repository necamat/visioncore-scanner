namespace VisionCore.Infrastructure.Implementations.Recognition;

using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Domain.Imaging.Recognition;
using VisionCore.Infrastructure.Imaging;

/// <summary>
/// Recognizes the handwritten score number with an ONNX digit-classification
/// model (MNIST-class CNN) — handwriting is what that model was trained on,
/// unlike the font-rendered templates. Requires the three dedicated score
/// digit regions; each cell goes through the shared glyph isolation
/// (border/edge-ink removal, ink bounds) before classification, and the
/// reported per-digit confidence is the model's softmax probability, so the
/// confidence evaluation thresholds apply unchanged.
/// </summary>
public sealed class OnnxScoreRecognizer : IScoreRecognizer, IDisposable
{
    private static readonly FormRegion[] ScoreRegions =
    [
        FormRegion.ScoreDigit1,
        FormRegion.ScoreDigit2,
        FormRegion.ScoreDigit3
    ];

    private readonly DigitRecognitionOptions _options;
    private readonly OnnxDigitClassifier _classifier;

    public OnnxScoreRecognizer(DigitRecognitionOptions options)
    {
        _options = options;
        _classifier = new OnnxDigitClassifier(options.OnnxModelPath);
    }

    public OnnxScoreRecognizer(IOptions<DigitRecognitionOptions> options)
        : this(options.Value)
    {
    }

    public Task<NumberRecognitionResult> RecognizeAsync(
        CroppedFormRegions regions,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!ScoreRegions.All(regions.Contains))
        {
            return Task.FromResult(
                NumberRecognitionResult.FailureResult(RecognitionFailureCode.MissingRegion, null, 0f));
        }

        var digits = new List<RecognizedDigit>(ScoreRegions.Length);
        foreach (var region in ScoreRegions)
        {
            ct.ThrowIfCancellationRequested();

            var digit = RecognizeDigit(regions.GetRegion(region));
            if (digit is null)
            {
                return Task.FromResult(
                    NumberRecognitionResult.FailureResult(RecognitionFailureCode.InvalidDigit, null, 0f));
            }

            digits.Add(digit);
        }

        var number = new RecognizedNumber(digits);
        return Task.FromResult(NumberRecognitionResult.Success(number));
    }

    public void Dispose() => _classifier.Dispose();

    private RecognizedDigit? RecognizeDigit(CroppedRegion region)
    {
        var bitmap = GrayImage.FromGray8(region.Width, region.Height, region.Pixels);
        var prepared = GlyphIsolation.PrepareForRecognition(bitmap, _options.DarkPixelThreshold);
        var bounds = GlyphIsolation.ExtractInkBounds(
            prepared, _options.DarkPixelThreshold, _options.MinimumInkRatio);

        if (bounds is null)
        {
            return null;
        }

        var glyph = prepared.Crop(bounds.Value);
        var (digit, confidence) = _classifier.Classify(glyph);
        return new RecognizedDigit(region.Region, digit, confidence);
    }
}
