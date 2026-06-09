using FluentAssertions;
using Microsoft.Extensions.Options;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Infrastructure.Implementations.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class TemplateMatchingDigitRecognizerTests
{
    [Fact]
    public async Task RecognizeAsync_Should_Return_Failure_For_Blank_Regions()
    {
        var recognizer = new TemplateMatchingDigitRecognizer(Options.Create(new DigitRecognitionOptions()));
        var regions = new CroppedFormRegions(
        [
            BlankRegion(FormRegion.TeamId),
            BlankRegion(FormRegion.Score)
        ]);

        var result = await recognizer.RecognizeAsync(regions, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    private static CroppedRegion BlankRegion(FormRegion region)
    {
        const int width = 40;
        const int height = 60;
        var pixels = new byte[width * height];
        Array.Fill(pixels, (byte)255); // all white = no ink
        return new CroppedRegion(region, width, height, pixels, new PixelBounds(0, 0, width, height));
    }
}
