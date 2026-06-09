namespace VisionCore.Infrastructure.Imaging;

using System.Drawing;
using System.Runtime.InteropServices;
using SkiaSharp;

/// <summary>
/// A small cross-platform 8-bit grayscale image backed by SkiaSharp (MIT).
/// Replaces the previous System.Drawing.Bitmap (GDI+, Windows-only) usage so the
/// imaging code runs on any OS. Exposes only the pixel-level operations the
/// recognizers and extractor need — read/write intensity, crop, resize, load,
/// save — keeping the recognition algorithms unchanged.
///
/// <see cref="System.Drawing.Rectangle"/> (from System.Drawing.Primitives, which
/// is cross-platform) is reused as a plain geometry struct.
/// </summary>
public sealed class GrayImage : IDisposable
{
    private readonly SKBitmap _bitmap; // Gray8 color type.

    private GrayImage(SKBitmap bitmap)
    {
        _bitmap = bitmap;
    }

    public int Width => _bitmap.Width;

    public int Height => _bitmap.Height;

    /// <summary>Creates a blank (white) grayscale image.</summary>
    public static GrayImage CreateWhite(int width, int height)
    {
        var bitmap = NewGray(width, height);
        bitmap.Erase(SKColors.White);
        return new GrayImage(bitmap);
    }

    /// <summary>Decodes encoded image bytes (PNG/JPEG/…), converting to 8-bit grayscale.</summary>
    public static GrayImage Load(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        return Decode(data);
    }

    /// <summary>Wraps a raw 8-bit grayscale pixel buffer (row-major, no padding).</summary>
    public static GrayImage FromGray8(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height)
        {
            throw new ArgumentException(
                $"Expected {width * height} grayscale bytes but got {pixels.Length}.", nameof(pixels));
        }

        var bitmap = NewGray(width, height);
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return new GrayImage(bitmap);
    }

    /// <summary>Copies the pixels into a raw 8-bit grayscale buffer (row-major, no padding).</summary>
    public byte[] ToGray8Bytes()
    {
        var buffer = new byte[Width * Height];
        Marshal.Copy(_bitmap.GetPixels(), buffer, 0, buffer.Length);
        return buffer;
    }

    /// <summary>Intensity (0 = black, 255 = white) at the given pixel.</summary>
    public byte GetIntensity(int x, int y) => _bitmap.GetPixel(x, y).Red;

    /// <summary>Sets the intensity (0 = black, 255 = white) at the given pixel.</summary>
    public void SetIntensity(int x, int y, byte value) =>
        _bitmap.SetPixel(x, y, new SKColor(value, value, value));

    public void SetWhite(int x, int y) => SetIntensity(x, y, 255);

    /// <summary>Returns an independent deep copy.</summary>
    public GrayImage Clone() => new(_bitmap.Copy());

    /// <summary>Returns a cropped copy of the given region.</summary>
    public GrayImage Crop(Rectangle bounds)
    {
        var target = NewGray(bounds.Width, bounds.Height);
        using (var canvas = new SKCanvas(target))
        {
            // Draw the source shifted so that (bounds.Left, bounds.Top) maps to (0, 0).
            canvas.DrawBitmap(_bitmap, -bounds.Left, -bounds.Top);
        }

        return new GrayImage(target);
    }

    /// <summary>Returns a resized (high-quality) copy.</summary>
    public GrayImage Resize(int width, int height)
    {
        var target = NewGray(width, height);
        _bitmap.ScalePixels(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return new GrayImage(target);
    }

    public void Dispose() => _bitmap.Dispose();

    private static SKBitmap NewGray(int width, int height) =>
        new(width, height, SKColorType.Gray8, SKAlphaType.Opaque);

    private static GrayImage Decode(SKData data)
    {
        // Decode into the codec's native format first; most codecs cannot decode
        // straight to Gray8. Then draw onto a Gray8 surface to convert.
        using var decoded = SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("Unsupported or corrupt image data.");

        var target = NewGray(decoded.Width, decoded.Height);
        using (var canvas = new SKCanvas(target))
        {
            canvas.DrawBitmap(decoded, 0, 0);
        }

        return new GrayImage(target);
    }
}
