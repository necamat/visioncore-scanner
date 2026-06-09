using FluentAssertions;
using VisionCore.Infrastructure.Imaging;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class GlyphMorphologyTests
{
    [Fact]
    public void Dilate_Grows_A_Single_Pixel_Into_A_3x3_Block()
    {
        var glyph = new bool[3, 3];
        glyph[1, 1] = true;

        var dilated = GlyphMorphology.Dilate(glyph);

        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                dilated[x, y].Should().BeTrue($"({x},{y}) is in the 3x3 neighbourhood of the centre");
            }
        }
    }

    [Fact]
    public void Erode_Removes_Pixels_Without_A_Full_Neighbourhood()
    {
        var glyph = Filled(3, 3);

        var eroded = GlyphMorphology.Erode(glyph);

        // Only the centre has all 8 neighbours set, so only it survives.
        eroded[1, 1].Should().BeTrue();
        eroded[0, 0].Should().BeFalse();
        eroded[2, 2].Should().BeFalse();
    }

    [Fact]
    public void CountHoles_Returns_Zero_For_A_Solid_Block()
    {
        GlyphMorphology.CountHoles(Filled(5, 5)).Should().Be(0);
    }

    [Fact]
    public void CountHoles_Returns_One_For_A_Ring()
    {
        // 5x5 ring: ink border, single enclosed white centre (like "0").
        var glyph = Filled(5, 5);
        glyph[2, 2] = false;

        GlyphMorphology.CountHoles(glyph).Should().Be(1);
    }

    [Fact]
    public void CountHoles_Returns_Two_For_A_Figure_Eight()
    {
        // 5x5 with two stacked enclosed cells separated by an ink row (like "8").
        var glyph = Filled(5, 5);
        glyph[2, 1] = false; // upper hole
        glyph[2, 3] = false; // lower hole

        GlyphMorphology.CountHoles(glyph).Should().Be(2);
    }

    [Fact]
    public void CountHoles_Ignores_Background_That_Touches_The_Border()
    {
        // A notch open to the edge is not an enclosed hole.
        var glyph = Filled(5, 5);
        glyph[2, 0] = false;
        glyph[2, 1] = false;

        GlyphMorphology.CountHoles(glyph).Should().Be(0);
    }

    [Fact]
    public void CenterBandInkRatio_Is_One_For_A_Solid_Block_And_Zero_For_Empty()
    {
        GlyphMorphology.CenterBandInkRatio(Filled(10, 10)).Should().Be(1f);
        GlyphMorphology.CenterBandInkRatio(new bool[10, 10]).Should().Be(0f);
    }

    private static bool[,] Filled(int width, int height)
    {
        var glyph = new bool[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                glyph[x, y] = true;
            }
        }

        return glyph;
    }
}
