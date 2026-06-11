namespace VisionCore.Infrastructure.Imaging;

using System.Drawing;

/// <summary>
/// Isolates a digit glyph inside a cropped form cell: removes box border
/// remnants near the edges, flood-fills away any ink connected to the image
/// edge (scan bleed, cut-off neighbours), and locates the remaining ink
/// bounds. Shared by every recognition engine — template matching and the
/// ONNX classifier prepare their input the same way.
/// </summary>
public static class GlyphIsolation
{
    /// <summary>
    /// Returns a copy of the source with border lines and edge-connected ink
    /// removed, leaving only glyph ink in the interior.
    /// </summary>
    public static GrayImage PrepareForRecognition(GrayImage source, int darkPixelThreshold)
    {
        var prepared = source.Clone();
        RemoveBorderLines(prepared, darkPixelThreshold);
        RemoveEdgeConnectedInk(prepared, darkPixelThreshold);
        return prepared;
    }

    /// <summary>
    /// Bounding rectangle of the ink, or null when the image is effectively
    /// blank (ink ratio below <paramref name="minimumInkRatio"/>).
    /// </summary>
    public static Rectangle? ExtractInkBounds(GrayImage source, int darkPixelThreshold, float minimumInkRatio)
    {
        var minX = source.Width;
        var minY = source.Height;
        var maxX = -1;
        var maxY = -1;
        var darkPixels = 0;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetIntensity(x, y) > darkPixelThreshold)
                {
                    continue;
                }

                darkPixels++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        var inkRatio = (float)darkPixels / (source.Width * source.Height);
        if (maxX < minX || maxY < minY || inkRatio < minimumInkRatio)
        {
            return null;
        }

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>
    /// Whites out rows/columns near the edges that are mostly dark — the
    /// remnants of the printed cell border caught by the crop.
    /// </summary>
    private static void RemoveBorderLines(GrayImage bitmap, int darkPixelThreshold)
    {
        const float rowThreshold = 0.35f;
        const float columnThreshold = 0.35f;
        var rowsToClear = new List<int>();
        var columnsToClear = new List<int>();
        var edgeRowBand = Math.Max(2, (int)Math.Round(bitmap.Height * 0.20f));
        var edgeColumnBand = Math.Max(2, (int)Math.Round(bitmap.Width * 0.20f));

        for (var y = 0; y < bitmap.Height; y++)
        {
            var darkCount = 0;
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetIntensity(x, y) <= darkPixelThreshold)
                {
                    darkCount++;
                }
            }

            var isNearEdge = y < edgeRowBand || y >= bitmap.Height - edgeRowBand;
            if (isNearEdge && (darkCount / (float)bitmap.Width) >= rowThreshold)
            {
                rowsToClear.Add(y);
            }
        }

        for (var x = 0; x < bitmap.Width; x++)
        {
            var darkCount = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetIntensity(x, y) <= darkPixelThreshold)
                {
                    darkCount++;
                }
            }

            var isNearEdge = x < edgeColumnBand || x >= bitmap.Width - edgeColumnBand;
            if (isNearEdge && (darkCount / (float)bitmap.Height) >= columnThreshold)
            {
                columnsToClear.Add(x);
            }
        }

        foreach (var y in rowsToClear)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetWhite(x, y);
            }
        }

        foreach (var x in columnsToClear)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                bitmap.SetWhite(x, y);
            }
        }
    }

    /// <summary>Flood-fills away any ink reachable from the image edge.</summary>
    private static void RemoveEdgeConnectedInk(GrayImage bitmap, int darkPixelThreshold)
    {
        var visited = new bool[bitmap.Width, bitmap.Height];
        var edgePixels = new Queue<Point>();

        for (var x = 0; x < bitmap.Width; x++)
        {
            EnqueueIfDark(bitmap, visited, edgePixels, x, 0, darkPixelThreshold);
            EnqueueIfDark(bitmap, visited, edgePixels, x, bitmap.Height - 1, darkPixelThreshold);
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            EnqueueIfDark(bitmap, visited, edgePixels, 0, y, darkPixelThreshold);
            EnqueueIfDark(bitmap, visited, edgePixels, bitmap.Width - 1, y, darkPixelThreshold);
        }

        while (edgePixels.Count > 0)
        {
            var current = edgePixels.Dequeue();
            bitmap.SetWhite(current.X, current.Y);

            EnqueueNeighbor(bitmap, visited, edgePixels, current.X - 1, current.Y, darkPixelThreshold);
            EnqueueNeighbor(bitmap, visited, edgePixels, current.X + 1, current.Y, darkPixelThreshold);
            EnqueueNeighbor(bitmap, visited, edgePixels, current.X, current.Y - 1, darkPixelThreshold);
            EnqueueNeighbor(bitmap, visited, edgePixels, current.X, current.Y + 1, darkPixelThreshold);
        }
    }

    private static void EnqueueNeighbor(
        GrayImage bitmap, bool[,] visited, Queue<Point> queue, int x, int y, int darkPixelThreshold)
    {
        if (x < 0 || x >= bitmap.Width || y < 0 || y >= bitmap.Height)
        {
            return;
        }

        EnqueueIfDark(bitmap, visited, queue, x, y, darkPixelThreshold);
    }

    private static void EnqueueIfDark(
        GrayImage bitmap, bool[,] visited, Queue<Point> queue, int x, int y, int darkPixelThreshold)
    {
        if (visited[x, y])
        {
            return;
        }

        visited[x, y] = true;

        if (bitmap.GetIntensity(x, y) <= darkPixelThreshold)
        {
            queue.Enqueue(new Point(x, y));
        }
    }
}
