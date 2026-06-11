using FluentAssertions;
using Microsoft.Extensions.Options;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Domain.Imaging.Recognition;
using VisionCore.Infrastructure.Imaging;
using VisionCore.Infrastructure.Implementations.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

/// <summary>
/// Drives the ONNX score recognizer over form-shaped digit cells (78x82, dark
/// ink on white, border remnants at the edges) that carry real handwritten
/// MNIST digits — the closest a deterministic test can get to a scanned
/// handwritten score.
/// </summary>
public sealed class OnnxScoreRecognizerTests : IDisposable
{
    private const int CellWidth = 78;
    private const int CellHeight = 82;

    private readonly OnnxScoreRecognizer sut = new(Options.Create(new DigitRecognitionOptions
    {
        OnnxModelPath = "Models/mnist-12.onnx"
    }));

    public void Dispose() => sut.Dispose();

    [Theory]
    [InlineData(5, 8, 3, 583)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(9, 9, 9, 999)]
    [InlineData(1, 7, 4, 174)]
    public async Task RecognizeAsync_Should_Read_Handwritten_Digits_Placed_In_Form_Cells(
        int first, int second, int third, int expectedScore)
    {
        var regions = new CroppedFormRegions(
        [
            HandwrittenCell(FormRegion.ScoreDigit1, first),
            HandwrittenCell(FormRegion.ScoreDigit2, second),
            HandwrittenCell(FormRegion.ScoreDigit3, third)
        ]);

        var result = await sut.RecognizeAsync(regions, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Number!.Value.Should().Be(expectedScore);

        // The value must be right; how confident the model is decides only
        // accept-vs-review, and an upscaled 28x28 sample can legitimately
        // land in the review band. A floor guards against garbage softmax.
        result.Number.Digits.Should().OnlyContain(digit => digit.Confidence > 0.3f);
    }

    [Fact]
    public async Task RecognizeAsync_Should_Fail_For_A_Blank_Cell()
    {
        var regions = new CroppedFormRegions(
        [
            HandwrittenCell(FormRegion.ScoreDigit1, 5),
            BlankCell(FormRegion.ScoreDigit2),
            HandwrittenCell(FormRegion.ScoreDigit3, 3)
        ]);

        var result = await sut.RecognizeAsync(regions, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure.Should().Be(RecognitionFailureCode.InvalidDigit);
    }

    [Fact]
    public async Task RecognizeAsync_Should_Fail_When_The_Digit_Regions_Are_Missing()
    {
        var regions = new CroppedFormRegions([BlankCell(FormRegion.TeamId)]);

        var result = await sut.RecognizeAsync(regions, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure.Should().Be(RecognitionFailureCode.MissingRegion);
    }

    /// <summary>
    /// A form-shaped cell: white background, 3px border remnants on every
    /// edge (as the crop catches them), and an inverted MNIST digit pasted
    /// centered — handwriting as the extractor would deliver it.
    /// </summary>
    private static CroppedRegion HandwrittenCell(FormRegion region, int digit)
    {
        var cell = GrayImage.CreateWhite(CellWidth, CellHeight);
        DrawBorderRemnants(cell);

        var bytes = File.ReadAllBytes(Path.Combine("TestData", "Mnist", $"{digit}_0.png"));
        var mnist = GrayImage.Load(bytes);

        // Scale the 28x28 sample up to roughly cell glyph size, inverted to
        // dark-on-white.
        var scaled = mnist.Resize(56, 56);
        var offsetX = (CellWidth - scaled.Width) / 2;
        var offsetY = (CellHeight - scaled.Height) / 2;
        for (var y = 0; y < scaled.Height; y++)
        {
            for (var x = 0; x < scaled.Width; x++)
            {
                var intensity = (byte)(255 - scaled.GetIntensity(x, y));
                if (intensity < 255)
                {
                    cell.SetIntensity(offsetX + x, offsetY + y, intensity);
                }
            }
        }

        return new CroppedRegion(
            region, CellWidth, CellHeight, cell.ToGray8Bytes(), new PixelBounds(0, 0, CellWidth, CellHeight));
    }

    private static CroppedRegion BlankCell(FormRegion region)
    {
        var cell = GrayImage.CreateWhite(CellWidth, CellHeight);
        DrawBorderRemnants(cell);
        return new CroppedRegion(
            region, CellWidth, CellHeight, cell.ToGray8Bytes(), new PixelBounds(0, 0, CellWidth, CellHeight));
    }

    private static void DrawBorderRemnants(GrayImage cell)
    {
        for (var x = 0; x < cell.Width; x++)
        {
            for (var line = 0; line < 3; line++)
            {
                cell.SetIntensity(x, line, 0);
                cell.SetIntensity(x, cell.Height - 1 - line, 0);
            }
        }

        for (var y = 0; y < cell.Height; y++)
        {
            for (var line = 0; line < 3; line++)
            {
                cell.SetIntensity(line, y, 0);
                cell.SetIntensity(cell.Width - 1 - line, y, 0);
            }
        }
    }
}
