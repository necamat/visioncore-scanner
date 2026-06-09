namespace VisionCore.Domain.Imaging.Cropping;

public sealed record PixelBounds
{
    public PixelBounds(int x, int y, int width, int height)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "X must be non-negative.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Y must be non-negative.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}
