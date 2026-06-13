using FluentAssertions;
using VisionCore.Infrastructure.Imaging;
using VisionCore.Infrastructure.Implementations.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

/// <summary>
/// Real-data verification of the ONNX digit classifier: thirty genuine MNIST
/// test images (three per digit, committed under TestData/Mnist) must all be
/// classified correctly. The samples are stored in MNIST polarity (white ink
/// on black); the helper converts them to the form's dark-on-white convention
/// and crops to ink bounds — the same shape the recognizer hands over.
/// </summary>
public sealed class OnnxDigitClassifierTests : IDisposable
{
    private const string ModelPath = "Models/mnist-12.onnx";

    private readonly OnnxDigitClassifier sut = new(ModelPath);

    public void Dispose() => sut.Dispose();

    public static TheoryData<int, int> AllSamples()
    {
        var data = new TheoryData<int, int>();
        for (var digit = 0; digit <= 9; digit++)
        {
            for (var sample = 0; sample <= 2; sample++)
            {
                data.Add(digit, sample);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllSamples))]
    public void Classify_Should_Read_A_Handwritten_Mnist_Digit(int digit, int sample)
    {
        var glyph = LoadSampleAsFormGlyph(digit, sample);

        var (read, confidence) = sut.Classify(glyph);

        read.Should().Be(digit);
        confidence.Should().BeGreaterThan(0.5f, "a clean handwritten digit must be a confident read");
    }

    [Fact]
    public void Constructor_Should_Fail_Fast_With_The_Resolved_Path_When_The_Model_Is_Missing()
    {
        var act = () => new OnnxDigitClassifier("Models/missing-model.onnx");

        act.Should().Throw<FileNotFoundException>().WithMessage("*missing-model.onnx*OnnxModelPath*");
    }

    /// <summary>Loads an MNIST PNG, inverts it to dark-on-white and crops to ink bounds.</summary>
    private static GrayImage LoadSampleAsFormGlyph(int digit, int sample)
    {
        var bytes = File.ReadAllBytes(Path.Combine("TestData", "Mnist", $"{digit}_{sample}.png"));
        var mnist = GrayImage.Load(bytes);

        var inverted = GrayImage.CreateWhite(mnist.Width, mnist.Height);
        for (var y = 0; y < mnist.Height; y++)
        {
            for (var x = 0; x < mnist.Width; x++)
            {
                inverted.SetIntensity(x, y, (byte)(255 - mnist.GetIntensity(x, y)));
            }
        }

        var bounds = GlyphIsolation.ExtractInkBounds(inverted, darkPixelThreshold: 160, minimumInkRatio: 0.01f);
        bounds.Should().NotBeNull("an MNIST sample always contains ink");
        return inverted.Crop(bounds!.Value);
    }
}
