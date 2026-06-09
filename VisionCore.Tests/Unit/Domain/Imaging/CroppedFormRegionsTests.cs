using FluentAssertions;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using Xunit;

namespace VisionCore.Tests.Unit.Domain.Imaging;

public class CroppedFormRegionsTests
{
    [Fact]
    public void Constructor_Should_Throw_When_Cropped_Regions_Are_Duplicated()
    {
        // Arrange
        var regions = new[]
        {
            new CroppedRegion(FormRegion.TeamId, 10, 10, new byte[100], new PixelBounds(1, 1, 10, 10)),
            new CroppedRegion(FormRegion.TeamId, 10, 10, new byte[100], new PixelBounds(2, 2, 10, 10))
        };

        // Act
        Action act = () => _ = new CroppedFormRegions(regions);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Duplicate cropped regions detected.");
    }
}
