using FluentAssertions;
using Moq;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Imaging;
using VisionCore.Application.Imaging.Steps.Shared;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Domain.Imaging.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Application.Imaging;

public sealed class DigitRecognitionStepTests
{
    private static CroppedFormRegions SingleRegion() => new(new[]
    {
        new CroppedRegion(FormRegion.TeamId, 10, 10, new byte[100], new PixelBounds(0, 0, 10, 10))
    });

    [Fact]
    public async Task ExecuteAsync_Should_Populate_Context_On_Success()
    {
        var teamNumber = new RecognizedNumber(new[] { new RecognizedDigit(FormRegion.TeamId, 1, 0.9f) });
        var scoreNumber = new RecognizedNumber(new[] { new RecognizedDigit(FormRegion.Score, 2, 0.9f) });
        var recognizerMock = new Mock<IDigitRecognizer>();
        recognizerMock
            .Setup(r => r.RecognizeAsync(It.IsAny<CroppedFormRegions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DigitRecognitionResult.Success(teamNumber, scoreNumber, 0.9f));

        var context = new PipelineContext { ImagePath = "sheet.pdf", CroppedRegions = SingleRegion() };

        var result = await new DigitRecognitionStep(recognizerMock.Object).ExecuteAsync(context, CancellationToken.None);

        result.ShouldContinue.Should().BeTrue();
        context.DigitRecognitionResult!.TeamId!.Value.Should().Be(1);
        context.DigitRecognitionResult.Score!.Value.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Fail_When_Recognizer_Reports_Failure()
    {
        var recognizerMock = new Mock<IDigitRecognizer>();
        recognizerMock
            .Setup(r => r.RecognizeAsync(It.IsAny<CroppedFormRegions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DigitRecognitionResult.FailureResult(RecognitionFailureCode.LowConfidence, null, null, 0f));

        var context = new PipelineContext { ImagePath = "sheet.pdf", CroppedRegions = SingleRegion() };

        var result = await new DigitRecognitionStep(recognizerMock.Object).ExecuteAsync(context, CancellationToken.None);

        result.ShouldContinue.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_Fail_When_Cropped_Regions_Missing()
    {
        var recognizerMock = new Mock<IDigitRecognizer>();
        var context = new PipelineContext { ImagePath = "sheet.pdf" };

        var result = await new DigitRecognitionStep(recognizerMock.Object).ExecuteAsync(context, CancellationToken.None);

        result.ShouldContinue.Should().BeFalse();
    }
}
