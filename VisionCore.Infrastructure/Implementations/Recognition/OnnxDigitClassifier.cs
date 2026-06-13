namespace VisionCore.Infrastructure.Implementations.Recognition;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VisionCore.Infrastructure.Imaging;

/// <summary>
/// Classifies a single isolated digit glyph with an MNIST-class ONNX model
/// (input: float[1,1,28,28]; output: ten class scores). The glyph is prepared
/// the MNIST way — scaled into a 20x20 box preserving aspect ratio, centered
/// on a 28x28 field, ink-on-black polarity — and the reported confidence is
/// the softmax probability of the winning class, which is margin-aware by
/// construction: a near-tie between two digits cannot produce a confident
/// score. Thread-safe: <see cref="InferenceSession.Run(IReadOnlyCollection{NamedOnnxValue})"/>
/// supports concurrent calls.
/// </summary>
public sealed class OnnxDigitClassifier : IDisposable
{
    private const int InputSize = 28;
    private const int GlyphBox = 20;

    private readonly InferenceSession _session;
    private readonly string _inputName;

    public OnnxDigitClassifier(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"ONNX digit model not found at '{Path.GetFullPath(modelPath)}'. " +
                "Check DigitRecognitionOptions.OnnxModelPath.", modelPath);
        }

        _session = new InferenceSession(modelPath);
        _inputName = _session.InputMetadata.Keys.Single();
    }

    /// <summary>
    /// Classifies a glyph image cropped to its ink bounds (dark ink on a white
    /// background). Returns the winning digit and its softmax probability.
    /// </summary>
    public (int Digit, float Confidence) Classify(GrayImage glyph)
    {
        var tensor = ToMnistTensor(glyph);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var scores = results[0].AsEnumerable<float>().ToArray();
        return PickWithSoftmax(scores);
    }

    public void Dispose() => _session.Dispose();

    private static DenseTensor<float> ToMnistTensor(GrayImage glyph)
    {
        // Fit the glyph into a 20x20 box preserving aspect ratio, as the MNIST
        // training data was prepared, then center it on the 28x28 input field.
        var scale = GlyphBox / (float)Math.Max(glyph.Width, glyph.Height);
        var scaledWidth = Math.Max(1, (int)Math.Round(glyph.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(glyph.Height * scale));
        var scaled = glyph.Resize(scaledWidth, scaledHeight);

        var offsetX = (InputSize - scaledWidth) / 2;
        var offsetY = (InputSize - scaledHeight) / 2;

        var tensor = new DenseTensor<float>([1, 1, InputSize, InputSize]);
        for (var y = 0; y < scaledHeight; y++)
        {
            for (var x = 0; x < scaledWidth; x++)
            {
                // Invert: the form has dark ink on white, MNIST is the opposite.
                var ink = (255 - scaled.GetIntensity(x, y)) / 255f;
                tensor[0, 0, offsetY + y, offsetX + x] = ink;
            }
        }

        return tensor;
    }

    private static (int Digit, float Confidence) PickWithSoftmax(IReadOnlyList<float> scores)
    {
        var best = 0;
        for (var digit = 1; digit < scores.Count; digit++)
        {
            if (scores[digit] > scores[best])
            {
                best = digit;
            }
        }

        // Numerically stable softmax: shift by the maximum score.
        var sum = 0d;
        for (var digit = 0; digit < scores.Count; digit++)
        {
            sum += Math.Exp(scores[digit] - scores[best]);
        }

        return (best, (float)(1d / sum));
    }
}
