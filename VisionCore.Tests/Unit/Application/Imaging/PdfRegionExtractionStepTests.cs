using FluentAssertions;
using Moq;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Imaging;
using VisionCore.Application.Imaging.Steps.Pdf;
using VisionCore.Domain.Imaging;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using Xunit;

namespace VisionCore.Tests.Unit.Application.Imaging;

public sealed class PdfRegionExtractionStepTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Fail_Context_When_Extractor_Returns_Failure()
    {
        var regionExtractor = new Mock<IRegionExtractor>();
        regionExtractor
            .Setup(e => e.ExtractAsync("form.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RegionExtractionResult.Failure("PDF has no embedded images."));

        var sut = new PdfRegionExtractionStep(regionExtractor.Object);
        var context = new PipelineContext { ImagePath = "form.pdf" };

        var result = await sut.ExecuteAsync(context, CancellationToken.None);

        result.ShouldContinue.Should().BeFalse();
        result.Error.Should().Be("PDF has no embedded images.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Populate_CroppedRegions_When_Extraction_Succeeds()
    {
        var croppedRegions = CreateCroppedRegions();
        var regionExtractor = new Mock<IRegionExtractor>();
        regionExtractor
            .Setup(e => e.ExtractAsync("form.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RegionExtractionResult.Success(croppedRegions));

        var sut = new PdfRegionExtractionStep(regionExtractor.Object);
        var context = new PipelineContext { ImagePath = "form.pdf" };

        var result = await sut.ExecuteAsync(context, CancellationToken.None);

        result.ShouldContinue.Should().BeTrue();
        context.CroppedRegions.Should().Be(croppedRegions);
    }

    [Fact]
    public void Stage_Should_Be_CropRegions()
    {
        var sut = new PdfRegionExtractionStep(Mock.Of<IRegionExtractor>());

        sut.Stage.Should().Be(PipelineStage.CropRegions);
    }

    private static CroppedFormRegions CreateCroppedRegions() =>
        new(new[]
        {
            new CroppedRegion(FormRegion.TeamId, 110, 120, new byte[110 * 120], new PixelBounds(1330, 200, 110, 120)),
            new CroppedRegion(FormRegion.Score, 295, 120, new byte[295 * 120], new PixelBounds(1090, 1980, 295, 120))
        });
}
