namespace VisionCore.Infrastructure.Imaging;

using System.Drawing;
using System.Runtime.InteropServices;
using SkiaSharp;

/// <summary>
/// A small cross-platform 8-bit grayscale image backed by a managed pixel
/// buffer (row-major, no padding). SkiaSharp (MIT) is used only at the
/// boundaries — decoding encoded bytes and high-quality resizing — so the
/// per-pixel operations the recognizers run in tight loops (intensity
/// reads/writes, ink scans, flood fills) are plain array accesses with no
/// native interop or per-pixel allocation.
///
/// <see cref="System.Drawing.Rectangle"/> (from System.Drawing.Primitives, which
/// is cross-platform) is reused as a plain geometry struct.
/// </summary>
public sealed class GrayImage
{
    private readonly byte[] _pixels;

    private GrayImage(byte[] pixels, int width, int height)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Creates a blank (white) grayscale image.</summary>
    public static GrayImage CreateWhite(int width, int height)
    {
        ValidateDimensions(width, height);

        var pixels = new byte[width * height];
        Array.Fill(pixels, (byte)255);
        return new GrayImage(pixels, width, height);
    }

    /// <summary>Decodes encoded image bytes (PNG/JPEG/…), converting to 8-bit grayscale.</summary>
    public static GrayImage Load(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var decoded = SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("Unsupported or corrupt image data.");

        // Decode lands in the codec's native format; most codecs cannot decode
        // straight to Gray8. Draw onto a Gray8 surface to convert.
        using var gray = NewGrayBitmap(decoded.Width, decoded.Height);
        using (var canvas = new SKCanvas(gray))
        {
            canvas.DrawBitmap(decoded, 0, 0);
        }

        return FromGrayBitmap(gray);
    }

    /// <summary>Copies a raw 8-bit grayscale pixel buffer (row-major, no padding).</summary>
    public static GrayImage FromGray8(int width, int height, byte[] pixels)
    {
        ValidateDimensions(width, height);

        if (pixels.Length != width * height)
        {
            throw new ArgumentException(
                $"Expected {width * height} grayscale bytes but got {pixels.Length}.", nameof(pixels));
        }

        var copy = new byte[pixels.Length];
        Array.Copy(pixels, copy, pixels.Length);
        return new GrayImage(copy, width, height);
    }

    /// <summary>Copies the pixels into a raw 8-bit grayscale buffer (row-major, no padding).</summary>
    public byte[] ToGray8Bytes()
    {
        var buffer = new byte[_pixels.Length];
        Array.Copy(_pixels, buffer, _pixels.Length);
        return buffer;
    }

    /// <summary>Intensity (0 = black, 255 = white) at the given pixel.</summary>
    public byte GetIntensity(int x, int y) => _pixels[(y * Width) + x];

    /// <summary>Sets the intensity (0 = black, 255 = white) at the given pixel.</summary>
    public void SetIntensity(int x, int y, byte value) => _pixels[(y * Width) + x] = value;

    public void SetWhite(int x, int y) => SetIntensity(x, y, 255);

    /// <summary>Returns an independent deep copy.</summary>
    public GrayImage Clone() => new(ToGray8Bytes(), Width, Height);

    /// <summary>Returns a cropped copy of the given region, which must lie within the image.</summary>
    public GrayImage Crop(Rectangle bounds)
    {
        if (bounds.Width < 1 || bounds.Height < 1 ||
            bounds.Left < 0 || bounds.Top < 0 ||
            bounds.Right > Width || bounds.Bottom > Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds), bounds, $"Crop bounds must lie within the {Width}x{Height} image.");
        }

        var pixels = new byte[bounds.Width * bounds.Height];
        for (var row = 0; row < bounds.Height; row++)
        {
            Array.Copy(
                _pixels, ((bounds.Top + row) * Width) + bounds.Left,
                pixels, row * bounds.Width,
                bounds.Width);
        }

        return new GrayImage(pixels, bounds.Width, bounds.Height);
    }

    /// <summary>Returns a resized (high-quality) copy.</summary>
    public GrayImage Resize(int width, int height)
    {
        ValidateDimensions(width, height);

        using var source = NewGrayBitmap(Width, Height);
        Marshal.Copy(_pixels, 0, source.GetPixels(), _pixels.Length);

        using var target = NewGrayBitmap(width, height);
        source.ScalePixels(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return FromGrayBitmap(target);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"Image dimensions must be positive but were {width}x{height}.");
        }
    }

    private static SKBitmap NewGrayBitmap(int width, int height) =>
        new(width, height, SKColorType.Gray8, SKAlphaType.Opaque);

    private static GrayImage FromGrayBitmap(SKBitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return new GrayImage(pixels, bitmap.Width, bitmap.Height);
    }
}
