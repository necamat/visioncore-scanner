namespace VisionCore.Domain.Imaging.Cropping;

using VisionCore.Domain.Imaging.Layout;

/// <summary>
/// One cropped form region, carried in memory as a raw 8-bit grayscale pixel
/// buffer (row-major, one byte per pixel, no padding) together with the bounds
/// it was cropped from. Keeping pixels in memory avoids round-tripping crops
/// through temporary files between extraction and recognition.
/// </summary>
public sealed record CroppedRegion(
    FormRegion Region,
    int Width,
    int Height,
    byte[] Pixels,
    PixelBounds Bounds);
