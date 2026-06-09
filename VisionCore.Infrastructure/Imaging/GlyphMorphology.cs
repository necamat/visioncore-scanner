namespace VisionCore.Infrastructure.Imaging;

using System.Drawing;

/// <summary>
/// Binary-image (glyph mask) operations used by the printed-digit shape
/// heuristics: morphological dilate/erode/close, enclosed-hole counting via
/// connected-component flood fill, and a centre-band ink ratio.
///
/// A glyph mask is a <c>bool[width, height]</c> where <c>true</c> = ink.
/// These are pure, side-effect-free functions kept separate from the recognizer
/// so they can be unit-tested in isolation.
/// </summary>
public static class GlyphMorphology
{
    /// <summary>Morphological close (dilate then erode) with a 3×3 structuring element.</summary>
    public static bool[,] Close(bool[,] glyph) => Erode(Dilate(glyph));

    /// <summary>Morphological dilation with a 3×3 structuring element.</summary>
    public static bool[,] Dilate(bool[,] glyph)
    {
        var width = glyph.GetLength(0);
        var height = glyph.GetLength(1);
        var dilated = new bool[width, height];

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (!glyph[x, y])
                {
                    continue;
                }

                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        dilated[nx, ny] = true;
                    }
                }
            }
        }

        return dilated;
    }

    /// <summary>Morphological erosion with a 3×3 structuring element.</summary>
    public static bool[,] Erode(bool[,] glyph)
    {
        var width = glyph.GetLength(0);
        var height = glyph.GetLength(1);
        var eroded = new bool[width, height];

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var keep = true;

                for (var dx = -1; dx <= 1 && keep; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height || !glyph[nx, ny])
                        {
                            keep = false;
                            break;
                        }
                    }
                }

                eroded[x, y] = keep;
            }
        }

        return eroded;
    }

    /// <summary>
    /// Counts enclosed white regions (holes) — background components that do not
    /// touch the border. E.g. "8" has two, "0"/"6"/"9" one, "1"/"7" none.
    /// </summary>
    public static int CountHoles(bool[,] glyph)
    {
        var width = glyph.GetLength(0);
        var height = glyph.GetLength(1);
        var visited = new bool[width, height];
        var holes = 0;

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (glyph[x, y] || visited[x, y])
                {
                    continue;
                }

                if (!FloodFillTouchesBorder(glyph, visited, x, y))
                {
                    holes++;
                }
            }
        }

        return holes;
    }

    /// <summary>Fraction of ink pixels within a centred vertical band (18% of width).</summary>
    public static float CenterBandInkRatio(bool[,] glyph)
    {
        var width = glyph.GetLength(0);
        var height = glyph.GetLength(1);
        var bandWidth = Math.Max(1, (int)Math.Round(width * 0.18f));
        var startX = Math.Max(0, (width - bandWidth) / 2);
        var endX = Math.Min(width, startX + bandWidth);
        var inkPixels = 0;
        var totalPixels = (endX - startX) * height;

        for (var x = startX; x < endX; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (glyph[x, y])
                {
                    inkPixels++;
                }
            }
        }

        return totalPixels == 0 ? 0f : inkPixels / (float)totalPixels;
    }

    private static bool FloodFillTouchesBorder(bool[,] glyph, bool[,] visited, int startX, int startY)
    {
        var width = glyph.GetLength(0);
        var height = glyph.GetLength(1);
        var queue = new Queue<Point>();
        var touchesBorder = false;

        visited[startX, startY] = true;
        queue.Enqueue(new Point(startX, startY));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.X == 0 || current.Y == 0 || current.X == width - 1 || current.Y == height - 1)
            {
                touchesBorder = true;
            }

            foreach (var neighbor in Neighbors(current))
            {
                if (neighbor.X < 0 || neighbor.X >= width || neighbor.Y < 0 || neighbor.Y >= height)
                {
                    continue;
                }

                if (visited[neighbor.X, neighbor.Y] || glyph[neighbor.X, neighbor.Y])
                {
                    continue;
                }

                visited[neighbor.X, neighbor.Y] = true;
                queue.Enqueue(neighbor);
            }
        }

        return touchesBorder;
    }

    private static IEnumerable<Point> Neighbors(Point point)
    {
        yield return new Point(point.X - 1, point.Y);
        yield return new Point(point.X + 1, point.Y);
        yield return new Point(point.X, point.Y - 1);
        yield return new Point(point.X, point.Y + 1);
    }
}
