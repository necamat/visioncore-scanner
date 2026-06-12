using System.Drawing;
using FluentAssertions;
using SkiaSharp;
using VisionCore.Infrastructure.Imaging;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class GrayImageTests
{
    [Fact]
    public void CreateWhite_Fills_Every_Pixel_With_White()
    {
        var image = GrayImage.CreateWhite(4, 3);

        image.Width.Should().Be(4);
        image.Height.Should().Be(3);
        image.ToGray8Bytes().Should().AllBeEquivalentTo((byte)255);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    public void CreateWhite_Rejects_Non_Positive_Dimensions(int width, int height)
    {
        var act = () => GrayImage.CreateWhite(width, height);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FromGray8_Then_ToGray8Bytes_Round_Trips_The_Buffer()
    {
        var pixels = new byte[] { 0, 64, 128, 192, 255, 10 };

        var image = GrayImage.FromGray8(3, 2, pixels);

        image.ToGray8Bytes().Should().Equal(pixels);
    }

    [Fact]
    public void FromGray8_Rejects_A_Buffer_Of_The_Wrong_Length()
    {
        var act = () => GrayImage.FromGray8(3, 2, new byte[5]);

        act.Should().Throw<ArgumentException>().WithMessage("*6 grayscale bytes*");
    }

    [Fact]
    public void FromGray8_Copies_The_Buffer_So_Later_Mutation_Has_No_Effect()
    {
        var pixels = new byte[] { 1, 2, 3, 4 };
        var image = GrayImage.FromGray8(2, 2, pixels);

        pixels[0] = 99;

        image.GetIntensity(0, 0).Should().Be(1);
    }

    [Fact]
    public void SetIntensity_Is_Read_Back_By_GetIntensity_At_The_Same_Pixel()
    {
        var image = GrayImage.CreateWhite(3, 3);

        image.SetIntensity(2, 1, 42);

        image.GetIntensity(2, 1).Should().Be(42);
        image.GetIntensity(1, 2).Should().Be(255, "the transposed pixel must stay untouched");
    }

    [Fact]
    public void SetWhite_Resets_A_Pixel_To_White()
    {
        var image = GrayImage.FromGray8(2, 2, new byte[] { 0, 0, 0, 0 });

        image.SetWhite(1, 0);

        image.GetIntensity(1, 0).Should().Be(255);
        image.GetIntensity(0, 0).Should().Be(0);
    }

    [Fact]
    public void Clone_Returns_An_Independent_Copy()
    {
        var original = GrayImage.CreateWhite(2, 2);

        var clone = original.Clone();
        clone.SetIntensity(0, 0, 0);

        original.GetIntensity(0, 0).Should().Be(255);
        clone.GetIntensity(0, 0).Should().Be(0);
    }

    [Fact]
    public void Crop_Extracts_Exactly_The_Requested_Region()
    {
        // 4x3 image with row-major intensities 0..11.
        var pixels = Enumerable.Range(0, 12).Select(value => (byte)value).ToArray();
        var image = GrayImage.FromGray8(4, 3, pixels);

        var cropped = image.Crop(new Rectangle(1, 1, 2, 2));

        cropped.Width.Should().Be(2);
        cropped.Height.Should().Be(2);
        cropped.ToGray8Bytes().Should().Equal(5, 6, 9, 10);
    }

    [Theory]
    [InlineData(-1, 0, 2, 2)]
    [InlineData(0, -1, 2, 2)]
    [InlineData(3, 0, 2, 2)]
    [InlineData(0, 2, 2, 2)]
    [InlineData(0, 0, 0, 2)]
    [InlineData(0, 0, 2, 0)]
    public void Crop_Rejects_Bounds_Outside_The_Image(int x, int y, int width, int height)
    {
        var image = GrayImage.CreateWhite(4, 3);

        var act = () => image.Crop(new Rectangle(x, y, width, height));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Resize_Produces_The_Requested_Dimensions_And_Keeps_Flat_Areas_Flat()
    {
        var image = GrayImage.CreateWhite(8, 8);

        var resized = image.Resize(4, 4);

        resized.Width.Should().Be(4);
        resized.Height.Should().Be(4);
        resized.ToGray8Bytes().Should().AllBeEquivalentTo((byte)255, "scaling a uniform image must not invent detail");
    }

    [Fact]
    public void Load_Decodes_Encoded_Bytes_Into_Grayscale()
    {
        // Encode a 2x1 PNG: black pixel next to a white pixel.
        using var bitmap = new SKBitmap(2, 1);
        bitmap.SetPixel(0, 0, SKColors.Black);
        bitmap.SetPixel(1, 0, SKColors.White);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, quality: 100);

        var image = GrayImage.Load(encoded.ToArray());

        image.Width.Should().Be(2);
        image.Height.Should().Be(1);
        image.GetIntensity(0, 0).Should().Be(0);
        image.GetIntensity(1, 0).Should().Be(255);
    }

    [Fact]
    public void Load_Rejects_Corrupt_Image_Data()
    {
        var act = () => GrayImage.Load([1, 2, 3, 4]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported or corrupt*");
    }
}
