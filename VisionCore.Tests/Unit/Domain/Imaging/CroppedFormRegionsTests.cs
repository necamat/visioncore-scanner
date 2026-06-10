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

    [Fact]
    public void Constructor_Should_Throw_When_No_Regions_Are_Supplied()
    {
        Action act = () => _ = new CroppedFormRegions([]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("At least one cropped region is required.");
    }

    [Fact]
    public void Contains_Should_Report_Only_The_Supplied_Regions()
    {
        var regions = new CroppedFormRegions([Region(FormRegion.TeamId), Region(FormRegion.Score)]);

        regions.Contains(FormRegion.TeamId).Should().BeTrue();
        regions.Contains(FormRegion.Score).Should().BeTrue();
        regions.Contains(FormRegion.ScoreDigit1).Should().BeFalse();
    }

    [Fact]
    public void GetRegion_Should_Return_The_Region_For_Its_Key()
    {
        var teamId = Region(FormRegion.TeamId);
        var regions = new CroppedFormRegions([teamId, Region(FormRegion.Score)]);

        regions.GetRegion(FormRegion.TeamId).Should().BeSameAs(teamId);
    }

    [Fact]
    public void Regions_Should_Return_The_Same_Cached_Collection_On_Every_Access()
    {
        var regions = new CroppedFormRegions([Region(FormRegion.TeamId)]);

        regions.Regions.Should().BeSameAs(regions.Regions, "the collection must be built once, not per access");
    }

    private static CroppedRegion Region(FormRegion region) =>
        new(region, 10, 10, new byte[100], new PixelBounds(0, 0, 10, 10));
}
